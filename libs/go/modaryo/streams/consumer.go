package streams

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"strings"
	"time"

	"github.com/redis/go-redis/v9"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/tenant"
)

// Handler processes one event. A nil return acknowledges the entry; an error (or a panic) leaves it
// pending so it is retried through XAUTOCLAIM, up to Options.MaxDeliveries attempts.
type Handler func(ctx context.Context, e Envelope) error

// Options tunes a Consumer. Zero values take the defaults noted below.
type Options struct {
	// MaxDeliveries is the number of handler attempts before an entry is dead-lettered (default 5).
	MaxDeliveries int
	// ClaimIdle is how long an entry must sit pending before it is reclaimed (default 60s). It should
	// comfortably exceed the worst-case handler time. While a batch is worked through and while a
	// handler runs, the consumer refreshes the idle time of the entries it owns (XCLAIM JUSTID,
	// every ClaimIdle/2), so entries are not stolen by other consumers; this narrows but cannot
	// close the window, so delivery stays at-least-once and handlers must dedupe by envelope id.
	ClaimIdle time.Duration
	// Block is the XREADGROUP block time (default 5s).
	Block time.Duration
	// Count is the XREADGROUP/XAUTOCLAIM batch size (default 50).
	Count int
	// StartFrom is the group start id, "$" (default) or "0". It only applies when the group is created.
	StartFrom string
	// MaxAge, when > 0, makes the consumer ack and skip envelopes whose occurred_at is older than now-MaxAge.
	MaxAge time.Duration
	// Logger defaults to slog.Default(). Message bodies are never logged.
	Logger *slog.Logger
	// Now is the clock (default time.Now); tests inject their own.
	Now func() time.Time
}

const (
	minBackoff = time.Second
	maxBackoff = 30 * time.Second
	maxErrLen  = 1000
	ackTimeout = 5 * time.Second
)

// Consumer reads one stream as a member of a consumer group. Register handlers with Handle before Run.
type Consumer struct {
	rdb       redis.UniversalClient
	stream    string
	group     string
	name      string
	handlers  map[string]Handler
	opts      Options
	log       *slog.Logger
	lastClaim time.Time
	stolen    map[string]bool // ids of the current batch found to be owned by another consumer
}

// NewConsumer creates a consumer. The group is created (XGROUP CREATE ... MKSTREAM) by Run.
func NewConsumer(rdb redis.UniversalClient, stream, group, consumerName string, opts Options) *Consumer {
	if opts.MaxDeliveries <= 0 {
		opts.MaxDeliveries = 5
	}
	if opts.ClaimIdle <= 0 {
		opts.ClaimIdle = 60 * time.Second
	}
	if opts.Block <= 0 {
		opts.Block = 5 * time.Second
	}
	if opts.Count <= 0 {
		opts.Count = 50
	}
	if opts.StartFrom == "" {
		opts.StartFrom = "$"
	}
	if opts.Now == nil {
		opts.Now = time.Now
	}
	log := opts.Logger
	if log == nil {
		log = slog.Default()
	}
	return &Consumer{
		rdb: rdb, stream: stream, group: group, name: consumerName,
		handlers: map[string]Handler{}, opts: opts,
		log: log.With("stream", stream, "group", group, "consumer", consumerName),
	}
}

// Handle registers h for eventType. Entries of unregistered types are acked and ignored.
// Not safe to call concurrently with Run.
func (c *Consumer) Handle(eventType string, h Handler) { c.handlers[eventType] = h }

// DLQStream is the dead-letter stream of stream.
func DLQStream(stream string) string { return stream + ".dlq" }

func backoff(failures int) time.Duration {
	d := minBackoff
	for i := 1; i < failures && d < maxBackoff; i++ {
		d *= 2
	}
	if d > maxBackoff {
		d = maxBackoff
	}
	return d
}

// Run consumes until ctx is cancelled and then returns ctx.Err(). Valkey errors are logged and
// retried with exponential backoff (1s up to 30s). Entries of a batch are processed sequentially.
func (c *Consumer) Run(ctx context.Context) error {
	failures := 0
	fail := func(what string, err error) {
		if ctx.Err() != nil {
			return
		}
		failures++
		d := backoff(failures)
		c.log.Warn("streams: "+what+" failed; backing off", "error", err.Error(), "retry_in", d.String())
		t := time.NewTimer(d)
		defer t.Stop()
		select {
		case <-ctx.Done():
		case <-t.C:
		}
	}

	grouped := false
	for ctx.Err() == nil {
		if !grouped {
			if err := c.ensureGroup(ctx); err != nil {
				fail("create group", err)
				continue
			}
			grouped = true
		}

		if c.lastClaim.IsZero() || time.Since(c.lastClaim) >= c.opts.ClaimIdle {
			c.lastClaim = time.Now()
			if err := c.claimPending(ctx); err != nil {
				if isNoGroup(err) {
					grouped = false
				}
				fail("autoclaim", err)
				continue
			}
		}

		res, err := c.rdb.XReadGroup(ctx, &redis.XReadGroupArgs{
			Group: c.group, Consumer: c.name, Streams: []string{c.stream, ">"},
			Count: int64(c.opts.Count), Block: c.opts.Block,
		}).Result()
		if err != nil {
			if errors.Is(err, redis.Nil) {
				failures = 0
				continue
			}
			if isNoGroup(err) {
				grouped = false
			}
			fail("read", err)
			continue
		}
		failures = 0
		var items []item
		for _, s := range res {
			for _, m := range s.Messages {
				items = append(items, item{m, 1})
			}
		}
		c.processBatch(ctx, items)
	}
	return ctx.Err()
}

func isNoGroup(err error) bool { return err != nil && strings.HasPrefix(err.Error(), "NOGROUP") }

func (c *Consumer) ensureGroup(ctx context.Context) error {
	err := c.rdb.XGroupCreateMkStream(ctx, c.stream, c.group, c.opts.StartFrom).Err()
	if err != nil && !strings.HasPrefix(err.Error(), "BUSYGROUP") {
		return err
	}
	return nil
}

// claimPending reclaims entries idle for at least ClaimIdle and processes them.
func (c *Consumer) claimPending(ctx context.Context) error {
	start := "0-0"
	for ctx.Err() == nil {
		msgs, next, err := c.rdb.XAutoClaim(ctx, &redis.XAutoClaimArgs{
			Stream: c.stream, Group: c.group, Consumer: c.name,
			MinIdle: c.opts.ClaimIdle, Start: start, Count: int64(c.opts.Count),
		}).Result()
		if err != nil {
			return err
		}
		var items []item
		for _, m := range msgs {
			n, err := c.deliveries(ctx, m.ID)
			if err != nil {
				return err
			}
			if n == 0 { // acked or deleted meanwhile
				continue
			}
			items = append(items, item{m, n})
		}
		c.processBatch(ctx, items)
		if next == "0-0" || next == "" {
			return nil
		}
		start = next
	}
	return nil
}

// item is a stream entry with its delivery count (including the current delivery).
type item struct {
	m          redis.XMessage
	deliveries int64
}

// processBatch handles the items in order. Idle time runs from delivery, so while working through
// a batch the consumer periodically checks which remaining entries it still owns, skips the ones
// another consumer took, and resets idle on the others (XCLAIM JUSTID does not bump the delivery
// count).
func (c *Consumer) processBatch(ctx context.Context, items []item) {
	last := time.Now()
	for i, it := range items {
		if ctx.Err() != nil {
			return
		}
		if time.Since(last) > c.opts.ClaimIdle/2 {
			owned, err := c.refresh(ctx, items[i:])
			last = time.Now()
			if err != nil {
				if ctx.Err() != nil {
					return
				}
				c.log.Warn("streams: ownership refresh failed", "error", err.Error())
			} else {
				for _, r := range items[i:] {
					if !owned[r.m.ID] {
						c.markStolen(r.m.ID)
					}
				}
			}
		}
		if c.isStolen(it.m.ID) {
			c.skipStolen(it.m)
			continue
		}
		c.process(ctx, it.m, it.deliveries)
	}
	c.stolen = nil
}

func (c *Consumer) markStolen(id string) {
	if c.stolen == nil {
		c.stolen = map[string]bool{}
	}
	c.stolen[id] = true
}

func (c *Consumer) isStolen(id string) bool { return c.stolen[id] }

func (c *Consumer) skipStolen(m redis.XMessage) {
	typ, _ := m.Values["type"].(string)
	c.log.Warn("streams: entry was claimed by another consumer; skipped", "entry_id", m.ID, "event_type", typ)
}

// refresh returns which of items this consumer still owns and resets idle on those (XCLAIM JUSTID).
func (c *Consumer) refresh(ctx context.Context, items []item) (map[string]bool, error) {
	if len(items) == 0 {
		return nil, nil
	}
	ext, err := c.rdb.XPendingExt(ctx, &redis.XPendingExtArgs{
		Stream: c.stream, Group: c.group, Consumer: c.name,
		Start: items[0].m.ID, End: items[len(items)-1].m.ID, Count: 10000,
	}).Result()
	if err != nil {
		return nil, err
	}
	mine := make(map[string]bool, len(ext))
	for _, e := range ext {
		mine[e.ID] = true
	}
	owned := make(map[string]bool, len(items))
	var ids []string
	for _, it := range items {
		if mine[it.m.ID] {
			owned[it.m.ID] = true
			ids = append(ids, it.m.ID)
		}
	}
	if len(ids) == 0 {
		return owned, nil
	}
	err = c.rdb.XClaimJustID(ctx, &redis.XClaimArgs{
		Stream: c.stream, Group: c.group, Consumer: c.name, MinIdle: 0, Messages: ids,
	}).Err()
	return owned, err
}

// heartbeat keeps the idle time of one entry fresh while its handler runs; call the result to stop it.
func (c *Consumer) heartbeat(ctx context.Context, m redis.XMessage) (stop func()) {
	interval := c.opts.ClaimIdle / 2
	if interval <= 0 {
		interval = time.Millisecond
	}
	hctx, cancel := context.WithCancel(ctx)
	done := make(chan struct{})
	go func() {
		defer close(done)
		t := time.NewTicker(interval)
		defer t.Stop()
		for {
			select {
			case <-hctx.Done():
				return
			case <-t.C:
				if _, err := c.refresh(hctx, []item{{m: m}}); err != nil && hctx.Err() == nil {
					c.log.Warn("streams: heartbeat failed", "entry_id", m.ID, "error", err.Error())
				}
			}
		}
	}()
	return func() { cancel(); <-done }
}

// deliveries returns the delivery count of id from the PEL (0 when it is no longer pending).
func (c *Consumer) deliveries(ctx context.Context, id string) (int64, error) {
	ext, err := c.rdb.XPendingExt(ctx, &redis.XPendingExtArgs{
		Stream: c.stream, Group: c.group, Start: id, End: id, Count: 1,
	}).Result()
	if err != nil {
		return 0, err
	}
	if len(ext) == 0 {
		return 0, nil
	}
	return ext[0].RetryCount, nil
}

// process handles one entry. deliveries is its delivery count including this one.
//
// A handler gets at most MaxDeliveries attempts: when the attempt number MaxDeliveries fails the
// entry is dead-lettered right away; an entry already past that count (for example a consumer
// crashed in the last attempt) is dead-lettered without calling the handler.
func (c *Consumer) process(ctx context.Context, m redis.XMessage, deliveries int64) {
	log := c.log.With("entry_id", m.ID, "deliveries", deliveries)

	raw, _ := m.Values["event"].(string)
	if raw == "" {
		c.deadLetter(ctx, m, deliveries, "missing event field")
		return
	}
	var e Envelope
	if err := json.Unmarshal([]byte(raw), &e); err != nil {
		c.deadLetter(ctx, m, deliveries, "malformed envelope")
		return
	}
	if e.V != Version {
		c.deadLetter(ctx, m, deliveries, fmt.Sprintf("unsupported envelope version %d", e.V))
		return
	}
	log = log.With("event_id", e.ID.String(), "event_type", e.Type)

	if deliveries > int64(c.opts.MaxDeliveries) {
		c.deadLetter(ctx, m, deliveries-1, "max deliveries exceeded")
		return
	}
	if c.opts.MaxAge > 0 && e.OccurredAt.Before(c.opts.Now().Add(-c.opts.MaxAge)) {
		log.Info("streams: event older than max age; skipped")
		c.ack(ctx, m.ID, log)
		return
	}
	h, ok := c.handlers[e.Type]
	if !ok {
		log.Debug("streams: no handler for event type; acked")
		c.ack(ctx, m.ID, log)
		return
	}

	stopHeartbeat := c.heartbeat(ctx, m)
	err := c.call(ctx, h, e)
	stopHeartbeat()
	if err != nil {
		if ctx.Err() != nil {
			// Shutting down: the failure may be the cancellation itself. Leave it pending.
			log.Warn("streams: handler failed during shutdown; left pending", "error", err.Error())
			return
		}
		if deliveries >= int64(c.opts.MaxDeliveries) {
			log.Error("streams: handler failed on last attempt; dead-lettering", "error", err.Error())
			c.deadLetter(ctx, m, deliveries, err.Error())
			return
		}
		log.Warn("streams: handler failed; will be retried", "error", err.Error())
		return
	}
	c.ack(ctx, m.ID, log)
}

// call runs the handler with the tenant in its context, turning a panic into an error.
func (c *Consumer) call(ctx context.Context, h Handler, e Envelope) (err error) {
	defer func() {
		if r := recover(); r != nil {
			err = fmt.Errorf("handler panic: %v", r)
		}
	}()
	return h(tenant.WithTenant(ctx, e.TenantID), e)
}

func (c *Consumer) ack(ctx context.Context, id string, log *slog.Logger) {
	// Not tied to ctx: a handled entry must be acked even while shutting down.
	actx, cancel := context.WithTimeout(context.WithoutCancel(ctx), ackTimeout)
	defer cancel()
	if err := c.rdb.XAck(actx, c.stream, c.group, id).Err(); err != nil {
		log.Warn("streams: ack failed", "error", err.Error())
	}
}

// deadLetter copies the entry to {stream}.dlq with error metadata, then acks the original.
func (c *Consumer) deadLetter(ctx context.Context, m redis.XMessage, deliveries int64, reason string) {
	if r := []rune(reason); len(r) > maxErrLen {
		reason = string(r[:maxErrLen])
	}
	values := make(map[string]any, len(m.Values)+5)
	for k, v := range m.Values {
		values[k] = v
	}
	values["error"] = reason
	values["deliveries"] = deliveries
	values["original_id"] = m.ID
	values["failed_at"] = time.Now().UTC().Format(TimeFormat)
	values["group"] = c.group

	actx, cancel := context.WithTimeout(context.WithoutCancel(ctx), ackTimeout)
	defer cancel()
	err := c.rdb.XAdd(actx, &redis.XAddArgs{
		Stream: DLQStream(c.stream), MaxLen: MaxLen, Approx: true, Values: values,
	}).Err()
	if err != nil {
		c.log.Error("streams: dead-letter write failed; entry stays pending", "entry_id", m.ID, "error", err.Error())
		return
	}
	c.log.Warn("streams: entry dead-lettered", "entry_id", m.ID, "deliveries", deliveries)
	c.ack(ctx, m.ID, c.log.With("entry_id", m.ID))
}
