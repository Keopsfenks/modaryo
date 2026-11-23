package testinfra

import (
	"context"
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"testing"
	"time"

	"github.com/redis/go-redis/v9"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/session"
)

// WriteSession writes rec to Valkey exactly as identity does (record v1, all fields, key
// "sess:{sha256_hex(sid)}") with the given TTL, and deletes the key on cleanup. It returns the
// random sid to use as the session cookie value.
//
// Defaulting applied to rec before writing: V is forced to 1, an empty UserType becomes
// "employee", and nil Permissions / Modules become empty arrays. max_discount_pct is written as a
// bare JSON number (like identity), not as the quoted string decimal.Decimal emits by default.
func WriteSession(tb testing.TB, rdb redis.UniversalClient, rec session.Record, ttl time.Duration) string {
	tb.Helper()
	buf := make([]byte, 32)
	if _, err := rand.Read(buf); err != nil {
		tb.Fatalf("testinfra: random sid: %v", err)
	}
	sid := base64.RawURLEncoding.EncodeToString(buf)

	rec.V = session.CurrentVersion
	if rec.Permissions == nil {
		rec.Permissions = []string{}
	}
	if rec.Modules == nil {
		rec.Modules = []string{}
	}
	if rec.UserType == "" {
		rec.UserType = session.UserTypeEmployee
	}
	raw, err := marshalRecord(rec)
	if err != nil {
		tb.Fatalf("testinfra: marshal session: %v", err)
	}
	key := session.Key(sid)
	if err := rdb.Set(context.Background(), key, raw, ttl).Err(); err != nil {
		tb.Fatalf("testinfra: write session: %v", err)
	}
	tb.Cleanup(func() { _ = rdb.Del(context.Background(), key).Err() })
	return sid
}

// marshalRecord marshals rec with max_discount_pct as a bare number. It avoids the global
// decimal.MarshalJSONWithoutQuotes switch, which is not safe under parallel tests.
func marshalRecord(rec session.Record) ([]byte, error) {
	b, err := json.Marshal(rec)
	if err != nil {
		return nil, err
	}
	var m map[string]json.RawMessage
	if err := json.Unmarshal(b, &m); err != nil {
		return nil, err
	}
	m["max_discount_pct"] = json.RawMessage(rec.MaxDiscountPct.String())
	return json.Marshal(m)
}
