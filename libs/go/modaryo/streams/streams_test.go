package streams_test

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"log/slog"
	"os"
	"strconv"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/streams"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/tenant"
)

const testType = "test.thing-happened"

func testRedis(t *testing.T) *redis.Client {
	t.Helper()
	rdb := redis.NewClient(&redis.Options{Addr: "localhost:6379", DB: 4})
	if err := rdb.Ping(context.Background()).Err(); err != nil {
		t.Fatalf("Valkey erişilemiyor (infra/up.sh çalıştırın): %v", err)
	}
	t.Cleanup(func() { _ = rdb.Close() })
	return rdb
}

func newStream(t *testing.T, rdb *redis.Client) string {
	t.Helper()
	b := make([]byte, 8)
	_, err := rand.Read(b)
	require.NoError(t, err)
	name := "test.stream." + hex.EncodeToString(b)
	t.Cleanup(func() {
		rdb.Del(context.Background(), name, streams.DLQStream(name))
	})
	return name
}

func fastOpts() streams.Options {
	return streams.Options{ClaimIdle: 150 * time.Millisecond, Block: 50 * time.Millisecond, StartFrom: "0"}
}

func envelope(typ string, data string) streams.Envelope {
	return streams.Envelope{
		V: 1, ID: uuid.New(), Type: typ, TenantID: uuid.New(),
		OccurredAt: time.Now().UTC(), Actor: "Ali Veli", Data: json.RawMessage(data),
	}
}

// start runs the consumer until the test ends.
func start(t *testing.T, c *streams.Consumer) (stop func() error) {
	t.Helper()
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { done <- c.Run(ctx) }()
	var once sync.Once
	var result error
	stop = func() error {
		once.Do(func() {
			cancel()
			select {
			case result = <-done:
			case <-time.After(5 * time.Second):
				result = errors.New("Run did not return after cancel")
			}
		})
		return result
	}
	t.Cleanup(func() { _ = stop() })
	return stop
}

func pendingCount(t *testing.T, rdb *redis.Client, stream, group string) int64 {
	t.Helper()
	p, err := rdb.XPending(context.Background(), stream, group).Result()
	require.NoError(t, err)
	return p.Count
}

func dlq(t *testing.T, rdb *redis.Client, stream string) []redis.XMessage {
	t.Helper()
	msgs, err := rdb.XRange(context.Background(), streams.DLQStream(stream), "-", "+").Result()
	require.NoError(t, err)
	return msgs
}

func TestEnvelope_ExampleRoundTrips(t *testing.T) {
	raw, err := os.ReadFile("../../../../contracts/event-v1.example.json")
	require.NoError(t, err)
	var e streams.Envelope
	require.NoError(t, json.Unmarshal(raw, &e))
	assert.Equal(t, 1, e.V)
	assert.Equal(t, "current-account.entry-added", e.Type)
	assert.Equal(t, "0f8fad5b-d9cb-469f-a165-70867728950e", e.TenantID.String())
	assert.Equal(t, time.Date(2026, 11, 22, 21, 30, 15, 123_000_000, time.UTC), e.OccurredAt.UTC())
	assert.Equal(t, "Ali Veli", e.Actor)

	out, err := json.Marshal(e)
	require.NoError(t, err)
	assert.JSONEq(t, string(raw), string(out))
	assert.Contains(t, string(out), `"occurred_at":"2026-11-22T21:30:15.123Z"`)
}

func TestProduce_WritesErpEntryFormat(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	e := envelope(testType, `{"a":1}`)
	e.V = 0 // Produce sets 1
	require.NoError(t, streams.Produce(context.Background(), rdb, stream, e))

	msgs, err := rdb.XRange(context.Background(), stream, "-", "+").Result()
	require.NoError(t, err)
	require.Len(t, msgs, 1)
	assert.Equal(t, testType, msgs[0].Values["type"])
	assert.Equal(t, e.TenantID.String(), msgs[0].Values["tenant_id"])
	var got map[string]any
	require.NoError(t, json.Unmarshal([]byte(msgs[0].Values["event"].(string)), &got))
	assert.EqualValues(t, 1, got["v"])
	assert.Equal(t, e.ID.String(), got["id"])
	assert.Equal(t, e.OccurredAt.UTC().Format("2006-01-02T15:04:05.000Z"), got["occurred_at"])

	e.V = 2
	assert.Error(t, streams.Produce(context.Background(), rdb, stream, e))
}

func TestConsumer_ProcessesAndAcks(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	e := envelope(testType, `{"n":1}`)
	require.NoError(t, streams.Produce(context.Background(), rdb, stream, e))

	var mu sync.Mutex
	var got []streams.Envelope
	var gotTenant uuid.UUID
	c := streams.NewConsumer(rdb, stream, "g", "c1", fastOpts())
	c.Handle(testType, func(ctx context.Context, env streams.Envelope) error {
		mu.Lock()
		defer mu.Unlock()
		got = append(got, env)
		gotTenant, _ = tenant.FromContext(ctx)
		return nil
	})
	stop := start(t, c)

	require.Eventually(t, func() bool {
		mu.Lock()
		defer mu.Unlock()
		return len(got) == 1 && pendingCount(t, rdb, stream, "g") == 0
	}, 5*time.Second, 20*time.Millisecond)
	mu.Lock()
	assert.Equal(t, e.ID, got[0].ID)
	assert.JSONEq(t, `{"n":1}`, string(got[0].Data))
	assert.Equal(t, e.TenantID, gotTenant)
	mu.Unlock()

	assert.ErrorIs(t, stop(), context.Canceled)
	assert.Empty(t, dlq(t, rdb, stream))
}

func TestConsumer_HandlerErrorRetriesViaAutoclaim(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	require.NoError(t, streams.Produce(context.Background(), rdb, stream, envelope(testType, `{}`)))

	var calls int
	var mu sync.Mutex
	c := streams.NewConsumer(rdb, stream, "g", "c1", fastOpts())
	c.Handle(testType, func(context.Context, streams.Envelope) error {
		mu.Lock()
		defer mu.Unlock()
		calls++
		if calls < 3 {
			return errors.New("geçici hata")
		}
		return nil
	})
	start(t, c)

	require.Eventually(t, func() bool {
		mu.Lock()
		defer mu.Unlock()
		return calls == 3 && pendingCount(t, rdb, stream, "g") == 0
	}, 8*time.Second, 20*time.Millisecond)
	assert.Empty(t, dlq(t, rdb, stream))
}

func TestConsumer_HandlerPanicIsRetried(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	require.NoError(t, streams.Produce(context.Background(), rdb, stream, envelope(testType, `{}`)))

	var calls int
	var mu sync.Mutex
	c := streams.NewConsumer(rdb, stream, "g", "c1", fastOpts())
	c.Handle(testType, func(context.Context, streams.Envelope) error {
		mu.Lock()
		defer mu.Unlock()
		calls++
		if calls == 1 {
			panic("boom")
		}
		return nil
	})
	start(t, c)

	require.Eventually(t, func() bool {
		mu.Lock()
		defer mu.Unlock()
		return calls == 2 && pendingCount(t, rdb, stream, "g") == 0
	}, 8*time.Second, 20*time.Millisecond)
}

func TestPoisonMessage_GoesToDlq_OthersProceed(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	a := envelope("test.poison", `{"secret":"do-not-log"}`)
	b := envelope(testType, `{}`)
	ctx := context.Background()
	require.NoError(t, streams.Produce(ctx, rdb, stream, a))
	require.NoError(t, streams.Produce(ctx, rdb, stream, b))

	var mu sync.Mutex
	var order []string // "A" per poison attempt, "B" per good message
	var logs syncBuffer
	opts := fastOpts()
	opts.Logger = slog.New(slog.NewTextHandler(&logs, &slog.HandlerOptions{Level: slog.LevelDebug}))
	c := streams.NewConsumer(rdb, stream, "g", "c1", opts)
	c.Handle("test.poison", func(context.Context, streams.Envelope) error {
		mu.Lock()
		defer mu.Unlock()
		order = append(order, "A")
		return errors.New("hep başarısız")
	})
	c.Handle(testType, func(context.Context, streams.Envelope) error {
		mu.Lock()
		defer mu.Unlock()
		order = append(order, "B")
		return nil
	})
	start(t, c)

	require.Eventually(t, func() bool {
		return len(dlq(t, rdb, stream)) == 1 && pendingCount(t, rdb, stream, "g") == 0
	}, 10*time.Second, 20*time.Millisecond)

	mu.Lock()
	defer mu.Unlock()
	attempts := 0
	lastA, bIdx := -1, -1
	for i, s := range order {
		if s == "A" {
			attempts++
			lastA = i
		} else {
			bIdx = i
		}
	}
	assert.Equal(t, 5, attempts, "exactly 5 handler attempts")
	assert.Equal(t, 1, countOf(order, "B"), "B processed once")
	assert.Less(t, bIdx, lastA, "B is processed while A is still being retried")

	d := dlq(t, rdb, stream)[0]
	assert.Equal(t, "hep başarısız", d.Values["error"])
	assert.Equal(t, "5", d.Values["deliveries"])
	assert.Equal(t, "g", d.Values["group"])
	assert.Equal(t, "test.poison", d.Values["type"])
	assert.NotEmpty(t, d.Values["original_id"])
	assert.NotEmpty(t, d.Values["event"])
	_, err := time.Parse(streams.TimeFormat, d.Values["failed_at"].(string))
	assert.NoError(t, err)

	assert.NotContains(t, logs.String(), "do-not-log", "logs must not carry message bodies")
	assert.NotContains(t, logs.String(), a.TenantID.String()+"\"", "no event JSON in logs")
}

func TestUnknownVersion_GoesToDlq(t *testing.T) {
	rdb := testRedis(t)
	ctx := context.Background()
	cases := map[string]map[string]any{
		"v2":            {"type": testType, "event": `{"v":2,"id":"` + uuid.NewString() + `","type":"` + testType + `","tenant_id":"` + uuid.NewString() + `","occurred_at":"2026-11-22T21:30:15.123Z","actor":"x","data":{}}`},
		"malformed":     {"type": testType, "event": `{not json`},
		"missing-event": {"type": testType},
	}
	for name, fields := range cases {
		t.Run(name, func(t *testing.T) {
			stream := newStream(t, rdb)
			_, err := rdb.XAdd(ctx, &redis.XAddArgs{Stream: stream, Values: fields}).Result()
			require.NoError(t, err)

			called := false
			var mu sync.Mutex
			c := streams.NewConsumer(rdb, stream, "g", "c1", fastOpts())
			c.Handle(testType, func(context.Context, streams.Envelope) error {
				mu.Lock()
				called = true
				mu.Unlock()
				return nil
			})
			start(t, c)

			require.Eventually(t, func() bool {
				return len(dlq(t, rdb, stream)) == 1 && pendingCount(t, rdb, stream, "g") == 0
			}, 5*time.Second, 20*time.Millisecond)
			mu.Lock()
			assert.False(t, called)
			mu.Unlock()
			d := dlq(t, rdb, stream)[0]
			assert.NotEmpty(t, d.Values["error"])
			assert.Equal(t, "1", d.Values["deliveries"])
		})
	}
}

func TestUnhandledType_AckedIgnored(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	require.NoError(t, streams.Produce(context.Background(), rdb, stream, envelope("other.type", `{}`)))
	require.NoError(t, streams.Produce(context.Background(), rdb, stream, envelope(testType, `{}`)))

	var n int
	var mu sync.Mutex
	c := streams.NewConsumer(rdb, stream, "g", "c1", fastOpts())
	c.Handle(testType, func(context.Context, streams.Envelope) error {
		mu.Lock()
		n++
		mu.Unlock()
		return nil
	})
	start(t, c)

	require.Eventually(t, func() bool {
		mu.Lock()
		defer mu.Unlock()
		return n == 1 && pendingCount(t, rdb, stream, "g") == 0
	}, 5*time.Second, 20*time.Millisecond)
	assert.Empty(t, dlq(t, rdb, stream))
}

func TestTwoConsumersSameGroup_EachMessageOnce(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	const total = 40
	for i := 0; i < total; i++ {
		require.NoError(t, streams.Produce(context.Background(), rdb, stream, envelope(testType, `{"i":`+strconv.Itoa(i)+`}`)))
	}

	var mu sync.Mutex
	seen := map[uuid.UUID]int{}
	h := func(_ context.Context, e streams.Envelope) error {
		mu.Lock()
		seen[e.ID]++
		mu.Unlock()
		return nil
	}
	opts := fastOpts()
	opts.Count = 5
	c1 := streams.NewConsumer(rdb, stream, "g", "c1", opts)
	c2 := streams.NewConsumer(rdb, stream, "g", "c2", opts)
	c1.Handle(testType, h)
	c2.Handle(testType, h)
	start(t, c1)
	start(t, c2)

	require.Eventually(t, func() bool {
		mu.Lock()
		defer mu.Unlock()
		return len(seen) == total && pendingCount(t, rdb, stream, "g") == 0
	}, 8*time.Second, 20*time.Millisecond)
	time.Sleep(400 * time.Millisecond) // let a duplicate delivery show up if there were one
	mu.Lock()
	defer mu.Unlock()
	for id, n := range seen {
		assert.Equal(t, 1, n, "event %s handled %d times", id, n)
	}
}

func TestNewGroup_StartFromZeroReadsHistory_StartFromDollarSkipsIt(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	ctx := context.Background()
	old1, old2 := envelope(testType, `{}`), envelope(testType, `{}`)
	require.NoError(t, streams.Produce(ctx, rdb, stream, old1))
	require.NoError(t, streams.Produce(ctx, rdb, stream, old2))

	collect := func() (func(context.Context, streams.Envelope) error, func() []uuid.UUID) {
		var mu sync.Mutex
		var ids []uuid.UUID
		return func(_ context.Context, e streams.Envelope) error {
				mu.Lock()
				ids = append(ids, e.ID)
				mu.Unlock()
				return nil
			}, func() []uuid.UUID {
				mu.Lock()
				defer mu.Unlock()
				return append([]uuid.UUID(nil), ids...)
			}
	}

	hZero, gotZero := collect()
	zeroOpts := fastOpts()
	zeroOpts.StartFrom = "0"
	cZero := streams.NewConsumer(rdb, stream, "from-zero", "c1", zeroOpts)
	cZero.Handle(testType, hZero)
	start(t, cZero)

	hDollar, gotDollar := collect()
	dollarOpts := fastOpts()
	dollarOpts.StartFrom = "$"
	cDollar := streams.NewConsumer(rdb, stream, "from-dollar", "c1", dollarOpts)
	cDollar.Handle(testType, hDollar)
	start(t, cDollar)

	require.Eventually(t, func() bool { return len(gotZero()) == 2 }, 5*time.Second, 20*time.Millisecond)
	require.Eventually(t, func() bool {
		groups, err := rdb.XInfoGroups(ctx, stream).Result()
		return err == nil && len(groups) == 2
	}, 5*time.Second, 20*time.Millisecond)

	fresh := envelope(testType, `{}`)
	require.NoError(t, streams.Produce(ctx, rdb, stream, fresh))
	require.Eventually(t, func() bool { return len(gotDollar()) == 1 }, 5*time.Second, 20*time.Millisecond)
	assert.Equal(t, []uuid.UUID{fresh.ID}, gotDollar())
	assert.Equal(t, []uuid.UUID{old1.ID, old2.ID, fresh.ID}, gotZero())
}

func TestMaxAge_SkipsOldEnvelopes(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	now := time.Date(2026, 11, 23, 12, 0, 0, 0, time.UTC)
	old := envelope(testType, `{}`)
	old.OccurredAt = now.Add(-2 * time.Hour)
	fresh := envelope(testType, `{}`)
	fresh.OccurredAt = now.Add(-10 * time.Minute)
	require.NoError(t, streams.Produce(context.Background(), rdb, stream, old))
	require.NoError(t, streams.Produce(context.Background(), rdb, stream, fresh))

	var mu sync.Mutex
	var ids []uuid.UUID
	opts := fastOpts()
	opts.MaxAge = time.Hour
	opts.Now = func() time.Time { return now }
	c := streams.NewConsumer(rdb, stream, "g", "c1", opts)
	c.Handle(testType, func(_ context.Context, e streams.Envelope) error {
		mu.Lock()
		ids = append(ids, e.ID)
		mu.Unlock()
		return nil
	})
	start(t, c)

	require.Eventually(t, func() bool { return pendingCount(t, rdb, stream, "g") == 0 && lenLocked(&mu, &ids) >= 1 }, 5*time.Second, 20*time.Millisecond)
	time.Sleep(200 * time.Millisecond)
	mu.Lock()
	assert.Equal(t, []uuid.UUID{fresh.ID}, ids)
	mu.Unlock()
	assert.Empty(t, dlq(t, rdb, stream))
}

// ERP's OutboxRelay.ToFields writes these three fields; the envelope is compact System.Text.Json.
func TestConsumer_ConsumesErpShapedEntry(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	raw, err := os.ReadFile("../../../../contracts/event-v1.example.json")
	require.NoError(t, err)
	var compact bytes.Buffer
	require.NoError(t, json.Compact(&compact, raw))
	_, err = rdb.XAdd(context.Background(), &redis.XAddArgs{
		Stream: stream, MaxLen: 100000, Approx: true,
		Values: map[string]any{
			"type":      "current-account.entry-added",
			"tenant_id": "0f8fad5b-d9cb-469f-a165-70867728950e",
			"event":     compact.String(),
		},
	}).Result()
	require.NoError(t, err)

	var mu sync.Mutex
	var got *streams.Envelope
	var gotTenant uuid.UUID
	c := streams.NewConsumer(rdb, stream, "g", "c1", fastOpts())
	c.Handle("current-account.entry-added", func(ctx context.Context, e streams.Envelope) error {
		mu.Lock()
		defer mu.Unlock()
		got = &e
		gotTenant, _ = tenant.FromContext(ctx)
		return nil
	})
	start(t, c)

	require.Eventually(t, func() bool {
		mu.Lock()
		defer mu.Unlock()
		return got != nil && pendingCount(t, rdb, stream, "g") == 0
	}, 5*time.Second, 20*time.Millisecond)
	mu.Lock()
	defer mu.Unlock()
	assert.Equal(t, "019a9d0c-3f5e-7a41-8c2b-6d1e9f4a7b20", got.ID.String())
	assert.Equal(t, "Ali Veli", got.Actor)
	assert.Equal(t, got.TenantID, gotTenant)
	assert.JSONEq(t, `"250.50"`, string(mustField(t, got.Data, "amount")))
}

func TestRun_ReturnsOnCancelAndBacksOffOnErrors(t *testing.T) {
	// Unreachable Valkey: Run must neither panic nor spin, and must return promptly on cancel.
	bad := redis.NewClient(&redis.Options{Addr: "127.0.0.1:1", DB: 4, MaxRetries: -1, DialTimeout: 100 * time.Millisecond})
	t.Cleanup(func() { _ = bad.Close() })
	var logs syncBuffer
	opts := fastOpts()
	opts.Logger = slog.New(slog.NewTextHandler(&logs, nil))
	c := streams.NewConsumer(bad, "test.stream.unreachable", "g", "c1", opts)

	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { done <- c.Run(ctx) }()
	time.Sleep(1500 * time.Millisecond)
	cancel()
	select {
	case err := <-done:
		assert.ErrorIs(t, err, context.Canceled)
	case <-time.After(3 * time.Second):
		t.Fatal("Run did not return after cancel")
	}
	// backoff 1s then 2s: at most two failed attempts within 1.5s.
	assert.LessOrEqual(t, strings.Count(logs.String(), "backing off"), 2)
	assert.Contains(t, logs.String(), "backing off")
}

func mustField(t *testing.T, raw json.RawMessage, key string) json.RawMessage {
	t.Helper()
	var m map[string]json.RawMessage
	require.NoError(t, json.Unmarshal(raw, &m))
	return m[key]
}

func countOf(s []string, v string) int {
	n := 0
	for _, x := range s {
		if x == v {
			n++
		}
	}
	return n
}

func lenLocked(mu *sync.Mutex, ids *[]uuid.UUID) int {
	mu.Lock()
	defer mu.Unlock()
	return len(*ids)
}

// syncBuffer is a bytes.Buffer safe for the consumer goroutine to write while the test reads.
type syncBuffer struct {
	mu  sync.Mutex
	buf bytes.Buffer
}

func (b *syncBuffer) Write(p []byte) (int, error) {
	b.mu.Lock()
	defer b.mu.Unlock()
	return b.buf.Write(p)
}

func (b *syncBuffer) String() string {
	b.mu.Lock()
	defer b.mu.Unlock()
	return b.buf.String()
}

func TestClaimJustID_DoesNotBumpDeliveryCount(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	ctx := context.Background()
	require.NoError(t, rdb.XGroupCreateMkStream(ctx, stream, "g", "0").Err())
	require.NoError(t, streams.Produce(ctx, rdb, stream, envelope(testType, `{}`)))
	res, err := rdb.XReadGroup(ctx, &redis.XReadGroupArgs{Group: "g", Consumer: "c1", Streams: []string{stream, ">"}, Count: 1}).Result()
	require.NoError(t, err)
	id := res[0].Messages[0].ID
	for i := 0; i < 3; i++ {
		require.NoError(t, rdb.XClaimJustID(ctx, &redis.XClaimArgs{Stream: stream, Group: "g", Consumer: "c1", MinIdle: 0, Messages: []string{id}}).Err())
	}
	ext, err := rdb.XPendingExt(ctx, &redis.XPendingExtArgs{Stream: stream, Group: "g", Start: id, End: id, Count: 1}).Result()
	require.NoError(t, err)
	require.Len(t, ext, 1)
	assert.EqualValues(t, 1, ext[0].RetryCount)
}

func TestLongHandlers_TwoConsumers_NoStealNoDlq(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	const total = 4
	for i := 0; i < total; i++ {
		require.NoError(t, streams.Produce(context.Background(), rdb, stream, envelope(testType, `{}`)))
	}
	var mu sync.Mutex
	seen := map[uuid.UUID]int{}
	h := func(_ context.Context, e streams.Envelope) error {
		time.Sleep(800 * time.Millisecond) // longer than ClaimIdle
		mu.Lock()
		seen[e.ID]++
		mu.Unlock()
		return nil
	}
	opts := fastOpts()
	opts.ClaimIdle = 300 * time.Millisecond
	opts.Count = 4
	c1 := streams.NewConsumer(rdb, stream, "g", "c1", opts)
	c2 := streams.NewConsumer(rdb, stream, "g", "c2", opts)
	c1.Handle(testType, h)
	c2.Handle(testType, h)
	start(t, c1)
	start(t, c2)

	require.Eventually(t, func() bool {
		mu.Lock()
		defer mu.Unlock()
		return len(seen) == total && pendingCount(t, rdb, stream, "g") == 0
	}, 15*time.Second, 50*time.Millisecond)
	mu.Lock()
	for id, n := range seen {
		assert.Equal(t, 1, n, "event %s handled %d times", id, n)
	}
	mu.Unlock()
	assert.Empty(t, dlq(t, rdb, stream))
}

func TestOverMaxDeliveries_GoesToDlqWithoutHandler(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	ctx := context.Background()
	require.NoError(t, rdb.XGroupCreateMkStream(ctx, stream, "g", "0").Err())
	require.NoError(t, streams.Produce(ctx, rdb, stream, envelope(testType, `{}`)))
	res, err := rdb.XReadGroup(ctx, &redis.XReadGroupArgs{Group: "g", Consumer: "ghost", Streams: []string{stream, ">"}, Count: 1}).Result()
	require.NoError(t, err)
	id := res[0].Messages[0].ID
	for i := 0; i < 5; i++ { // a crashed consumer burned the deliveries
		require.NoError(t, rdb.XClaim(ctx, &redis.XClaimArgs{Stream: stream, Group: "g", Consumer: "ghost", MinIdle: 0, Messages: []string{id}}).Err())
	}

	var mu sync.Mutex
	called := false
	c := streams.NewConsumer(rdb, stream, "g", "c1", fastOpts())
	c.Handle(testType, func(context.Context, streams.Envelope) error {
		mu.Lock()
		called = true
		mu.Unlock()
		return nil
	})
	start(t, c)

	require.Eventually(t, func() bool {
		return len(dlq(t, rdb, stream)) == 1 && pendingCount(t, rdb, stream, "g") == 0
	}, 5*time.Second, 20*time.Millisecond)
	mu.Lock()
	assert.False(t, called)
	mu.Unlock()
	assert.Equal(t, "max deliveries exceeded", dlq(t, rdb, stream)[0].Values["error"])
}

func TestRun_RecreatesGroupAfterStreamDeleted(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	ctx := context.Background()
	var mu sync.Mutex
	n := 0
	c := streams.NewConsumer(rdb, stream, "g", "c1", fastOpts())
	c.Handle(testType, func(context.Context, streams.Envelope) error {
		mu.Lock()
		n++
		mu.Unlock()
		return nil
	})
	start(t, c)
	require.Eventually(t, func() bool {
		g, err := rdb.XInfoGroups(ctx, stream).Result()
		return err == nil && len(g) == 1
	}, 5*time.Second, 20*time.Millisecond)

	require.NoError(t, rdb.Del(ctx, stream).Err())
	time.Sleep(200 * time.Millisecond)
	require.NoError(t, streams.Produce(ctx, rdb, stream, envelope(testType, `{}`)))
	// Produce re-created the stream without the group; the consumer must create the group again.
	require.Eventually(t, func() bool {
		mu.Lock()
		defer mu.Unlock()
		return n == 1
	}, 8*time.Second, 50*time.Millisecond)
}

func TestShutdown_FailedHandlerIsNotDeadLettered(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	require.NoError(t, streams.Produce(context.Background(), rdb, stream, envelope(testType, `{}`)))
	entered := make(chan struct{})
	opts := fastOpts()
	opts.MaxDeliveries = 1 // the first failure would normally be the last attempt
	c := streams.NewConsumer(rdb, stream, "g", "c1", opts)
	c.Handle(testType, func(ctx context.Context, _ streams.Envelope) error {
		close(entered)
		<-ctx.Done()
		return ctx.Err()
	})
	stop := start(t, c)
	select {
	case <-entered:
	case <-time.After(5 * time.Second):
		t.Fatal("handler not called")
	}
	assert.ErrorIs(t, stop(), context.Canceled)
	assert.Empty(t, dlq(t, rdb, stream))
	assert.EqualValues(t, 1, pendingCount(t, rdb, stream, "g"))
}

// While one handler of a batch runs long, the heartbeat must keep the idle time of the batch's
// later entries fresh too, otherwise another consumer's XAUTOCLAIM could take them meanwhile.
func TestHeartbeat_RefreshesRemainingEntriesOfBatch(t *testing.T) {
	rdb := testRedis(t)
	stream := newStream(t, rdb)
	const total = 3
	for i := 0; i < total; i++ {
		require.NoError(t, streams.Produce(context.Background(), rdb, stream, envelope(testType, `{}`)))
	}
	opts := fastOpts()
	opts.ClaimIdle = 300 * time.Millisecond
	opts.Count = total

	var mu sync.Mutex
	var maxIdle time.Duration
	var pendingSeen int
	first := true
	c := streams.NewConsumer(rdb, stream, "g", "c1", opts)
	c.Handle(testType, func(ctx context.Context, e streams.Envelope) error {
		mu.Lock()
		isFirst := first
		first = false
		mu.Unlock()
		if !isFirst {
			return nil
		}
		time.Sleep(4 * opts.ClaimIdle) // well past ClaimIdle
		ext, err := rdb.XPendingExt(context.Background(), &redis.XPendingExtArgs{
			Stream: stream, Group: "g", Consumer: "c1", Start: "-", End: "+", Count: 100,
		}).Result()
		if err != nil {
			return err
		}
		mu.Lock()
		pendingSeen = len(ext)
		for _, p := range ext {
			maxIdle = max(maxIdle, p.Idle)
		}
		mu.Unlock()
		return nil
	})
	start(t, c)

	require.Eventually(t, func() bool {
		mu.Lock()
		started := !first
		mu.Unlock()
		return started && pendingCount(t, rdb, stream, "g") == 0
	}, 10*time.Second, 50*time.Millisecond)
	mu.Lock()
	defer mu.Unlock()
	assert.Equal(t, total, pendingSeen, "the whole batch is pending while the first handler runs")
	assert.Less(t, maxIdle, opts.ClaimIdle, "idle time of every remaining entry stays below ClaimIdle")
}
