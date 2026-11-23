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
	raw, err := json.Marshal(rec)
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
