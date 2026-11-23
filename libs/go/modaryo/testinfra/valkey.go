package testinfra

import (
	"context"
	"os"
	"testing"

	"github.com/redis/go-redis/v9"
)

const defaultValkeyAddr = "localhost:6379"

// ValkeyAddr returns MODARYO_TEST_VALKEY_ADDR or the dev default.
func ValkeyAddr() string {
	if v := os.Getenv("MODARYO_TEST_VALKEY_ADDR"); v != "" {
		return v
	}
	return defaultValkeyAddr
}

// Valkey returns a client for the given DB index (see the package doc for the allocation); it is
// closed on cleanup.
func Valkey(tb testing.TB, db int) *redis.Client {
	tb.Helper()
	rdb := redis.NewClient(&redis.Options{Addr: ValkeyAddr(), DB: db})
	tb.Cleanup(func() { _ = rdb.Close() })
	if err := rdb.Ping(context.Background()).Err(); err != nil {
		tb.Fatalf("testinfra: valkey must be running at %s (infra/up.sh): %v", ValkeyAddr(), err)
	}
	return rdb
}

// FlushDB empties the client's database. Only use it for a DB index your package owns
// exclusively: packages run in parallel and DB 4 is shared by several of them.
func FlushDB(tb testing.TB, rdb *redis.Client) {
	tb.Helper()
	if err := rdb.FlushDB(context.Background()).Err(); err != nil {
		tb.Fatalf("testinfra: flushdb: %v", err)
	}
}
