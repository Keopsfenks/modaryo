package session

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/redis/go-redis/v9"
)

// Reader reads a session and extends its idle lifetime; it never writes the value.
type Reader struct {
	rdb redis.UniversalClient
	now func() time.Time
}

// NewReader; a nil now means time.Now.
func NewReader(rdb redis.UniversalClient, now func() time.Time) *Reader {
	if now == nil {
		now = time.Now
	}
	return &Reader{rdb: rdb, now: now}
}

// ttl = min(idle_timeout_s, absolute_expires_at - now) seconds; ok is false when remaining <= 0.
func ttl(rec *Record, now time.Time) (time.Duration, bool) {
	remaining := rec.AbsoluteExpiresAt - now.Unix()
	if remaining <= 0 {
		return 0, false
	}
	secs := min(int64(rec.IdleTimeoutS), remaining)
	return time.Duration(secs) * time.Second, true
}

// Read returns ErrNotFound, ErrExpired, ErrUnsupportedVersion or a wrapped Valkey error.
func (r *Reader) Read(ctx context.Context, sid string) (*Record, error) {
	if strings.TrimSpace(sid) == "" {
		return nil, ErrNotFound
	}
	key := Key(sid)

	raw, err := r.rdb.Get(ctx, key).Result()
	if errors.Is(err, redis.Nil) {
		return nil, ErrNotFound
	}
	if err != nil {
		return nil, fmt.Errorf("session: valkey get: %w", err)
	}

	rec := Record{UserType: UserTypeEmployee, Modules: []string{}}
	if err := json.Unmarshal([]byte(raw), &rec); err != nil {
		return nil, ErrNotFound
	}
	if rec.V != CurrentVersion {
		return nil, ErrUnsupportedVersion
	}
	if rec.Modules == nil {
		rec.Modules = []string{}
	}
	if rec.UserType == "" {
		rec.UserType = UserTypeEmployee
	}

	d, ok := ttl(&rec, r.now())
	if !ok {
		return nil, ErrExpired
	}
	if err := r.rdb.Expire(ctx, key, d).Err(); err != nil {
		return nil, fmt.Errorf("session: valkey expire: %w", err)
	}
	return &rec, nil
}
