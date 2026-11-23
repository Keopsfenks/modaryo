package internalapi

import (
	"context"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/testinfra"
)

// newTestPool creates a temporary database, applies the idempotency schema and drops the database at the end.
func newTestPool(t *testing.T) *pgxpool.Pool {
	t.Helper()
	pool, _ := testinfra.TempDatabase(t)
	_, err := pool.Exec(context.Background(), IdempotencySchemaSQL)
	require.NoError(t, err)
	return pool
}

func TestPostgresStore_Contract(t *testing.T) {
	storeContract(t, NewPostgresStore(newTestPool(t)))
}

func TestPostgresStore_SchemaIsIdempotent(t *testing.T) {
	pool := newTestPool(t)
	_, err := pool.Exec(context.Background(), IdempotencySchemaSQL)
	require.NoError(t, err)
}

func TestPostgresStore_PurgeDeletesOldRecords(t *testing.T) {
	ctx := context.Background()
	store := NewPostgresStore(newTestPool(t))
	now := time.Date(2026, 1, 10, 0, 0, 0, 0, time.UTC)
	for i, age := range []time.Duration{8 * 24 * time.Hour, 6 * 24 * time.Hour} {
		r, err := store.Reserve(ctx, "c", string(rune('a'+i)), uuid.New(), "h", now.Add(-age), time.Minute)
		require.NoError(t, err)
		require.Equal(t, Reserved, r.Outcome)
	}
	n, err := store.Purge(ctx, now.Add(-IdempotencyRetention))
	require.NoError(t, err)
	assert.EqualValues(t, 1, n)
}

func TestPostgresStore_StoresLargeUnicodeBody(t *testing.T) {
	ctx := context.Background()
	store := NewPostgresStore(newTestPool(t))
	now := time.Now().UTC()
	r, err := store.Reserve(ctx, "c", "k", uuid.New(), "h", now, time.Minute)
	require.NoError(t, err)
	body := []byte(`{"ad":"şğüöçİ"}`)
	require.NoError(t, store.Complete(ctx, "c", "k", r.Owner, 200, body))
	again, err := store.Reserve(ctx, "c", "k", uuid.New(), "h", now, time.Minute)
	require.NoError(t, err)
	assert.Equal(t, Replay, again.Outcome)
	assert.Equal(t, body, again.StoredBody)
}

func TestPostgresStore_BinaryBodyIsStoredAsValidText(t *testing.T) {
	ctx := context.Background()
	store := NewPostgresStore(newTestPool(t))
	now := time.Now().UTC()
	r, err := store.Reserve(ctx, "c", "k", uuid.New(), "h", now, time.Minute)
	require.NoError(t, err)
	require.NoError(t, store.Complete(ctx, "c", "k", r.Owner, 200, []byte("a\x00b\xff\xfec")))
	again, err := store.Reserve(ctx, "c", "k", uuid.New(), "h", now, time.Minute)
	require.NoError(t, err)
	require.Equal(t, Replay, again.Outcome)
	assert.Equal(t, "ab\uFFFDc", string(again.StoredBody))
}
