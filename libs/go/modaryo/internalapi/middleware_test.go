package internalapi

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/tenant"
)

var (
	testNow    = time.Date(2026, 3, 1, 10, 0, 0, 0, time.UTC)
	testSecret = []byte("dev-secret")
	testTenant = uuid.MustParse("0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b21")
)

func testCallers() Callers {
	return Callers{"einvoice": {Secret: testSecret, Routes: []string{"/internal/einvoice/", "/internal/ping"}}}
}

type sreq struct {
	method, target, tenant, key string
	body                        string
	ts                          int64
	service                     string
	secret                      []byte
}

func (s sreq) build() *http.Request {
	if s.ts == 0 {
		s.ts = testNow.Unix()
	}
	if s.service == "" {
		s.service = "einvoice"
	}
	if s.secret == nil {
		s.secret = testSecret
	}
	r := httptest.NewRequest(s.method, s.target, strings.NewReader(s.body))
	r.Header.Set(HeaderService, s.service)
	r.Header.Set(HeaderTimestamp, strconv.FormatInt(s.ts, 10))
	r.Header.Set(HeaderSignature, Sign(s.secret, s.method, s.target, s.ts, s.tenant, s.key, []byte(s.body)))
	if s.tenant != "" {
		r.Header.Set(HeaderTenant, s.tenant)
	}
	if s.key != "" {
		r.Header.Set(HeaderIdempotencyKey, s.key)
	}
	return r
}

type harness struct {
	store   *memStore
	calls   atomic.Int32
	handler http.Handler
	now     time.Time
	mu      sync.Mutex
}

// newHarness builds the middleware around a handler that echoes the body as {"echo": ...}.
func newHarness(opts func(*Options), inner func(h *harness) http.HandlerFunc) *harness {
	h := &harness{store: newMemStore(), now: testNow}
	o := Options{
		Callers: testCallers(),
		Store:   h.store,
		Now:     func() time.Time { h.mu.Lock(); defer h.mu.Unlock(); return h.now },
	}
	if opts != nil {
		opts(&o)
	}
	var in http.HandlerFunc
	if inner != nil {
		in = inner(h)
	} else {
		in = func(w http.ResponseWriter, r *http.Request) {
			h.calls.Add(1)
			b, _ := io.ReadAll(r.Body)
			w.Header().Set("Content-Type", "application/json")
			if r.Method == http.MethodGet {
				w.WriteHeader(http.StatusOK)
			} else {
				w.WriteHeader(http.StatusCreated)
			}
			_, _ = w.Write([]byte(`{"echo":` + strconv.Quote(string(b)) + `}`))
		}
	}
	h.handler = Middleware(o)(in)
	return h
}

func (h *harness) do(r *http.Request) *httptest.ResponseRecorder {
	w := httptest.NewRecorder()
	h.handler.ServeHTTP(w, r)
	return w
}

func code(t *testing.T, w *httptest.ResponseRecorder) string {
	t.Helper()
	var d struct {
		Code string `json:"code"`
	}
	require.NoError(t, json.Unmarshal(w.Body.Bytes(), &d), w.Body.String())
	return d.Code
}

func TestMiddleware_StaleTimestamp_401(t *testing.T) {
	h := newHarness(nil, nil)
	w := h.do(sreq{method: "GET", target: "/internal/einvoice/x", tenant: testTenant.String(), ts: testNow.Unix() - 301}.build())
	assert.Equal(t, 401, w.Code)
	assert.Equal(t, "internal_unauthorized", code(t, w))
	assert.Equal(t, "application/problem+json", w.Header().Get("Content-Type"))

	w = h.do(sreq{method: "GET", target: "/internal/einvoice/x", tenant: testTenant.String(), ts: testNow.Unix() + 301}.build())
	assert.Equal(t, 401, w.Code)

	w = h.do(sreq{method: "GET", target: "/internal/einvoice/x", tenant: testTenant.String(), ts: testNow.Unix() - 300}.build())
	assert.Equal(t, 200, w.Code)
}

func TestMiddleware_TamperedTenant_401(t *testing.T) {
	h := newHarness(nil, nil)
	r := sreq{method: "GET", target: "/internal/einvoice/x", tenant: testTenant.String()}.build()
	r.Header.Set(HeaderTenant, uuid.NewString())
	w := h.do(r)
	assert.Equal(t, 401, w.Code)
	assert.Equal(t, "internal_unauthorized", code(t, w))
}

func TestMiddleware_TamperedIdempotencyKey_401(t *testing.T) {
	h := newHarness(nil, nil)
	r := sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k1", body: "{}"}.build()
	r.Header.Set(HeaderIdempotencyKey, "k2")
	w := h.do(r)
	assert.Equal(t, 401, w.Code)
	assert.EqualValues(t, 0, h.calls.Load())
}

func TestMiddleware_RouteNotAllowed_403(t *testing.T) {
	h := newHarness(nil, nil)
	for _, target := range []string{"/internal/other/x", "/internal/einvoicex/y", "/internal/branches/1"} {
		w := h.do(sreq{method: "GET", target: target, tenant: testTenant.String()}.build())
		assert.Equal(t, 403, w.Code, target)
		assert.Equal(t, "internal_forbidden", code(t, w))
	}
	// exact prefix (without trailing slash) and case-insensitive match are allowed, like .NET
	assert.Equal(t, 200, h.do(sreq{method: "GET", target: "/internal/einvoice", tenant: testTenant.String()}.build()).Code)
	assert.Equal(t, 200, h.do(sreq{method: "GET", target: "/Internal/EInvoice/a", tenant: testTenant.String()}.build()).Code)
}

func TestMiddleware_PreChecks_GenericUnauthorized(t *testing.T) {
	h := newHarness(func(o *Options) {
		o.Callers["nosecret"] = Caller{Routes: []string{"/internal/"}}
	}, nil)
	tid := testTenant.String()

	missingSig := sreq{method: "GET", target: "/internal/ping"}.build()
	missingSig.Header.Del(HeaderSignature)
	badTs := sreq{method: "GET", target: "/internal/ping"}.build()
	badTs.Header.Set(HeaderTimestamp, "-5")
	signTs := sreq{method: "GET", target: "/internal/ping"}.build()
	signTs.Header.Set(HeaderTimestamp, "+"+strconv.FormatInt(testNow.Unix(), 10))
	missingSvc := sreq{method: "GET", target: "/internal/ping"}.build()
	missingSvc.Header.Del(HeaderService)

	for name, r := range map[string]*http.Request{
		"missing signature": missingSig,
		"negative ts":       badTs,
		"plus-sign ts":      signTs,
		"missing service":   missingSvc,
		"unknown caller":    sreq{method: "GET", target: "/internal/ping", service: "ghost"}.build(),
		"empty secret":      sreq{method: "GET", target: "/internal/ping", service: "nosecret", secret: []byte("x")}.build(),
		"wrong secret":      sreq{method: "GET", target: "/internal/ping", secret: []byte("nope"), tenant: tid}.build(),
	} {
		w := h.do(r)
		assert.Equal(t, 401, w.Code, name)
		assert.Equal(t, "internal_unauthorized", code(t, w), name)
	}
}

type failReader struct{ read *atomic.Bool }

func (f failReader) Read([]byte) (int, error) {
	f.read.Store(true)
	return 0, errors.New("must not be read")
}

func TestMiddleware_UnknownCallerBigBody_401WithoutReading(t *testing.T) {
	h := newHarness(nil, nil)
	var read atomic.Bool
	r := httptest.NewRequest("POST", "/internal/einvoice/x", failReader{&read})
	r.ContentLength = 50 << 20
	r.Header.Set(HeaderService, "ghost")
	r.Header.Set(HeaderTimestamp, strconv.FormatInt(testNow.Unix(), 10))
	r.Header.Set(HeaderSignature, "00")
	w := h.do(r)
	assert.Equal(t, 401, w.Code)
	assert.False(t, read.Load(), "body must not be read before the pre-checks pass")
}

func TestMiddleware_BodyTooLarge_413(t *testing.T) {
	h := newHarness(func(o *Options) { o.MaxBodyBytes = 16 }, nil)
	big := strings.Repeat("a", 17)

	// declared Content-Length over the cap: rejected without reading
	var read atomic.Bool
	r := sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: big}.build()
	r.Body = io.NopCloser(failReader{&read})
	w := h.do(r)
	assert.Equal(t, 413, w.Code)
	assert.Equal(t, "internal_body_too_large", code(t, w))
	assert.False(t, read.Load())

	// unknown length (chunked): capped while reading
	r = sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: big}.build()
	r.ContentLength = -1
	w = h.do(r)
	assert.Equal(t, 413, w.Code)
	assert.EqualValues(t, 0, h.calls.Load())

	// exactly at the cap passes
	ok := sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k2", body: strings.Repeat("a", 16)}.build()
	assert.Equal(t, 201, h.do(ok).Code)
}

func TestMiddleware_PingWithoutTenant_200(t *testing.T) {
	var gotCaller string
	var hasTenant bool
	h := newHarness(nil, func(h *harness) http.HandlerFunc {
		return func(w http.ResponseWriter, r *http.Request) {
			gotCaller, _ = CallerFromContext(r.Context())
			_, hasTenant = tenant.FromContext(r.Context())
			w.WriteHeader(200)
		}
	})
	w := h.do(sreq{method: "GET", target: "/internal/ping"}.build())
	assert.Equal(t, 200, w.Code)
	assert.Equal(t, "einvoice", gotCaller)
	assert.False(t, hasTenant)

	// POST ping without tenant passes straight through (no idempotency), like .NET
	w = h.do(sreq{method: "POST", target: "/internal/ping/", body: "{}"}.build())
	assert.Equal(t, 200, w.Code)
}

func TestMiddleware_TenantRules(t *testing.T) {
	var got uuid.UUID
	h := newHarness(nil, func(h *harness) http.HandlerFunc {
		return func(w http.ResponseWriter, r *http.Request) {
			got, _ = tenant.FromContext(r.Context())
			w.WriteHeader(200)
		}
	})
	w := h.do(sreq{method: "GET", target: "/internal/einvoice/x"}.build())
	assert.Equal(t, 400, w.Code)
	assert.Equal(t, "internal_tenant_invalid", code(t, w))

	w = h.do(sreq{method: "GET", target: "/internal/einvoice/x", tenant: "not-a-uuid"}.build())
	assert.Equal(t, 400, w.Code)
	assert.Equal(t, "internal_tenant_invalid", code(t, w))

	w = h.do(sreq{method: "GET", target: "/internal/einvoice/x", tenant: uuid.Nil.String()}.build())
	assert.Equal(t, 400, w.Code)

	w = h.do(sreq{method: "GET", target: "/internal/einvoice/x", tenant: testTenant.String()}.build())
	assert.Equal(t, 200, w.Code)
	assert.Equal(t, testTenant, got)
}

func TestMiddleware_RawQueryOrderAndEncodingAreSigned(t *testing.T) {
	h := newHarness(nil, nil)
	target := "/internal/einvoice/x?b=2&a=1&c=%2F%20x"
	w := h.do(sreq{method: "GET", target: target, tenant: testTenant.String()}.build())
	assert.Equal(t, 200, w.Code, w.Body.String())

	// reordering the query after signing breaks the signature (no normalisation)
	r := sreq{method: "GET", target: target, tenant: testTenant.String()}.build()
	r.RequestURI = "/internal/einvoice/x?a=1&b=2&c=%2F%20x"
	r.URL.RawQuery = "a=1&b=2&c=%2F%20x"
	assert.Equal(t, 401, h.do(r).Code)
}

func TestMiddleware_NonInternalPathPassesThrough(t *testing.T) {
	h := newHarness(nil, nil)
	w := h.do(httptest.NewRequest("GET", "/api/anything", nil))
	assert.Equal(t, 200, w.Code)
}

func TestMiddleware_Writes(t *testing.T) {
	t.Run("key_required", func(t *testing.T) {
		h := newHarness(nil, nil)
		w := h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), body: "{}"}.build())
		assert.Equal(t, 400, w.Code)
		assert.Equal(t, "idempotency_key_required", code(t, w))

		tooLong := strings.Repeat("k", 201)
		w = h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: tooLong, body: "{}"}.build())
		assert.Equal(t, 400, w.Code)
		assert.EqualValues(t, 0, h.calls.Load())
	})

	t.Run("no_store_405", func(t *testing.T) {
		h := newHarness(func(o *Options) { o.Store = nil }, nil)
		w := h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: "{}"}.build())
		assert.Equal(t, 405, w.Code)
		assert.Equal(t, "internal_method_not_allowed", code(t, w))
		assert.Equal(t, "GET, HEAD", w.Header().Get("Allow"))
		// reads still work
		assert.Equal(t, 200, h.do(sreq{method: "GET", target: "/internal/einvoice/x", tenant: testTenant.String()}.build()).Code)
	})

	t.Run("same_key_same_body_returns_stored", func(t *testing.T) {
		h := newHarness(nil, nil)
		mk := func() *http.Request {
			return sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: `{"a":1}`}.build()
		}
		first := h.do(mk())
		require.Equal(t, 201, first.Code)
		second := h.do(mk())
		assert.Equal(t, 201, second.Code)
		assert.Equal(t, first.Body.String(), second.Body.String())
		assert.Equal(t, "application/json; charset=utf-8", second.Header().Get("Content-Type"))
		assert.EqualValues(t, 1, h.calls.Load(), "handler must not run again")
		assert.JSONEq(t, `{"echo":"{\"a\":1}"}`, first.Body.String(), "handler must be able to read the body")
	})

	t.Run("same_key_different_body_conflicts", func(t *testing.T) {
		h := newHarness(nil, nil)
		h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: `{"a":1}`}.build())
		w := h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: `{"a":2}`}.build())
		assert.Equal(t, 409, w.Code)
		assert.Equal(t, "idempotency_conflict", code(t, w))
	})

	t.Run("different_path_conflicts", func(t *testing.T) {
		h := newHarness(nil, nil)
		h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: `{}`}.build())
		w := h.do(sreq{method: "POST", target: "/internal/einvoice/y", tenant: testTenant.String(), key: "k", body: `{}`}.build())
		assert.Equal(t, 409, w.Code)
	})

	t.Run("in_progress_409_retry_after", func(t *testing.T) {
		h := newHarness(nil, nil)
		// Reserve the key directly so the request finds a fresh pending record.
		hash := RequestHash("POST", "/internal/einvoice/x", testTenant.String(), []byte("{}"))
		_, err := h.store.Reserve(context.Background(), "einvoice", "k", testTenant, hash, testNow, 5*time.Minute)
		require.NoError(t, err)
		w := h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: "{}"}.build())
		assert.Equal(t, 409, w.Code)
		assert.Equal(t, "idempotency_in_progress", code(t, w))
		assert.Equal(t, "1", w.Header().Get("Retry-After"))
	})

	t.Run("stale_pending_is_taken_over", func(t *testing.T) {
		h := newHarness(nil, nil)
		hash := RequestHash("POST", "/internal/einvoice/x", testTenant.String(), []byte("{}"))
		_, _ = h.store.Reserve(context.Background(), "einvoice", "k", testTenant, hash, testNow.Add(-6*time.Minute), 5*time.Minute)
		w := h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: "{}"}.build())
		assert.Equal(t, 201, w.Code)
		assert.EqualValues(t, 1, h.calls.Load())
	})

	t.Run("5xx_is_released_and_retry_executes_again", func(t *testing.T) {
		var fail atomic.Bool
		fail.Store(true)
		h := newHarness(nil, func(h *harness) http.HandlerFunc {
			return func(w http.ResponseWriter, r *http.Request) {
				h.calls.Add(1)
				if fail.Load() {
					w.WriteHeader(http.StatusBadGateway)
					return
				}
				w.WriteHeader(http.StatusOK)
			}
		})
		mk := func() *http.Request {
			return sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: "{}"}.build()
		}
		assert.Equal(t, 502, h.do(mk()).Code)
		fail.Store(false)
		assert.Equal(t, 200, h.do(mk()).Code)
		assert.EqualValues(t, 2, h.calls.Load())
	})

	t.Run("panic_releases_the_key", func(t *testing.T) {
		var boom atomic.Bool
		boom.Store(true)
		h := newHarness(nil, func(h *harness) http.HandlerFunc {
			return func(w http.ResponseWriter, r *http.Request) {
				h.calls.Add(1)
				if boom.Load() {
					panic("boom")
				}
				w.WriteHeader(200)
			}
		})
		mk := func() *http.Request {
			return sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: "{}"}.build()
		}
		assert.Panics(t, func() { h.do(mk()) })
		boom.Store(false)
		assert.Equal(t, 200, h.do(mk()).Code)
		assert.EqualValues(t, 2, h.calls.Load())
	})

	t.Run("4xx_is_stored", func(t *testing.T) {
		h := newHarness(nil, func(h *harness) http.HandlerFunc {
			return func(w http.ResponseWriter, r *http.Request) {
				h.calls.Add(1)
				w.WriteHeader(http.StatusUnprocessableEntity)
				_, _ = w.Write([]byte(`{"x":1}`))
			}
		})
		mk := func() *http.Request {
			return sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: "{}"}.build()
		}
		assert.Equal(t, 422, h.do(mk()).Code)
		w := h.do(mk())
		assert.Equal(t, 422, w.Code)
		assert.JSONEq(t, `{"x":1}`, w.Body.String())
		assert.EqualValues(t, 1, h.calls.Load())
	})

	t.Run("parallel_identical_writes_execute_once", func(t *testing.T) {
		h := newHarness(nil, func(h *harness) http.HandlerFunc {
			return func(w http.ResponseWriter, r *http.Request) {
				h.calls.Add(1)
				time.Sleep(50 * time.Millisecond)
				w.WriteHeader(http.StatusCreated)
				_, _ = w.Write([]byte(`{"ok":true}`))
			}
		})
		const n = 12
		var wg sync.WaitGroup
		codes := make(chan int, n)
		for i := 0; i < n; i++ {
			wg.Add(1)
			go func() {
				defer wg.Done()
				w := h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: "{}"}.build())
				codes <- w.Code
			}()
		}
		wg.Wait()
		close(codes)
		created := 0
		for c := range codes {
			if c == 201 {
				created++
			} else {
				assert.Equal(t, 409, c)
			}
		}
		assert.GreaterOrEqual(t, created, 1)
		assert.EqualValues(t, 1, h.calls.Load())
	})
}

// failingCompleteStore fails Complete; the key must then stay pending (never released).
type failingCompleteStore struct {
	*memStore
	released atomic.Bool
}

func (f *failingCompleteStore) Complete(context.Context, string, string, uuid.UUID, int, []byte) error {
	return errors.New("db down")
}
func (f *failingCompleteStore) Release(ctx context.Context, c, k string, o uuid.UUID) error {
	f.released.Store(true)
	return f.memStore.Release(ctx, c, k, o)
}

func TestMiddleware_CompleteFailureKeepsPendingAndStillResponds(t *testing.T) {
	fs := &failingCompleteStore{memStore: newMemStore()}
	h := newHarness(func(o *Options) { o.Store = fs }, nil)
	w := h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: "{}"}.build())
	assert.Equal(t, 201, w.Code)
	assert.False(t, fs.released.Load())
	w = h.do(sreq{method: "POST", target: "/internal/einvoice/x", tenant: testTenant.String(), key: "k", body: "{}"}.build())
	assert.Equal(t, 409, w.Code)
	assert.Equal(t, "idempotency_in_progress", code(t, w))
}
