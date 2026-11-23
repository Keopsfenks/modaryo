package internalapi

import (
	"context"
	"sync"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// memStore is an in-memory IdempotencyStore with the same semantics as the Postgres one.
type memStore struct {
	mu   sync.Mutex
	rows map[string]*memRow
}

type memRow struct {
	hash      string
	status    int
	body      []byte
	createdAt time.Time
	owner     uuid.UUID
}

func newMemStore() *memStore { return &memStore{rows: map[string]*memRow{}} }

func (m *memStore) Reserve(_ context.Context, caller, key string, _ uuid.UUID, hash string, now time.Time, timeout time.Duration) (Reservation, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	k := caller + "\x00" + key
	for retried := false; ; retried = true {
		row, ok := m.rows[k]
		if !ok {
			owner := uuid.New()
			m.rows[k] = &memRow{hash: hash, createdAt: now, owner: owner}
			return Reservation{Outcome: Reserved, Owner: owner}, nil
		}
		if row.hash != hash {
			return Reservation{Outcome: Conflict}, nil
		}
		if row.status != 0 {
			return Reservation{Outcome: Replay, StoredStatus: row.status, StoredBody: row.body}, nil
		}
		if !retried && row.createdAt.Before(now.Add(-timeout)) {
			delete(m.rows, k)
			continue
		}
		return Reservation{Outcome: InProgress}, nil
	}
}

func (m *memStore) Complete(_ context.Context, caller, key string, owner uuid.UUID, status int, body []byte) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	if r, ok := m.rows[caller+"\x00"+key]; ok && r.owner == owner {
		r.status, r.body = status, body
	}
	return nil
}

func (m *memStore) Release(_ context.Context, caller, key string, owner uuid.UUID) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	if r, ok := m.rows[caller+"\x00"+key]; ok && r.owner == owner {
		delete(m.rows, caller+"\x00"+key)
	}
	return nil
}

// storeContract runs the behaviour every IdempotencyStore must have.
func storeContract(t *testing.T, store IdempotencyStore) {
	ctx := context.Background()
	tenantID := uuid.New()
	now := time.Date(2026, 1, 1, 12, 0, 0, 0, time.UTC)
	timeout := 5 * time.Minute

	t.Run("reserve_complete_replay", func(t *testing.T) {
		r, err := store.Reserve(ctx, "einvoice", "k1", tenantID, "h1", now, timeout)
		require.NoError(t, err)
		require.Equal(t, Reserved, r.Outcome)
		require.NotEqual(t, uuid.Nil, r.Owner)

		// still pending: same hash -> in progress
		r2, err := store.Reserve(ctx, "einvoice", "k1", tenantID, "h1", now.Add(time.Second), timeout)
		require.NoError(t, err)
		assert.Equal(t, InProgress, r2.Outcome)

		require.NoError(t, store.Complete(ctx, "einvoice", "k1", r.Owner, 201, []byte(`{"id":1}`)))

		r3, err := store.Reserve(ctx, "einvoice", "k1", tenantID, "h1", now.Add(time.Second), timeout)
		require.NoError(t, err)
		assert.Equal(t, Replay, r3.Outcome)
		assert.Equal(t, 201, r3.StoredStatus)
		assert.JSONEq(t, `{"id":1}`, string(r3.StoredBody))
	})

	t.Run("different_hash_conflicts", func(t *testing.T) {
		r, err := store.Reserve(ctx, "einvoice", "k2", tenantID, "h1", now, timeout)
		require.NoError(t, err)
		require.Equal(t, Reserved, r.Outcome)
		r2, err := store.Reserve(ctx, "einvoice", "k2", tenantID, "h2", now, timeout)
		require.NoError(t, err)
		assert.Equal(t, Conflict, r2.Outcome)
	})

	t.Run("key_is_per_caller", func(t *testing.T) {
		a, _ := store.Reserve(ctx, "a", "same", tenantID, "h1", now, timeout)
		b, _ := store.Reserve(ctx, "b", "same", tenantID, "h2", now, timeout)
		assert.Equal(t, Reserved, a.Outcome)
		assert.Equal(t, Reserved, b.Outcome)
	})

	t.Run("release_frees_key_and_wrong_owner_cannot_release", func(t *testing.T) {
		r, _ := store.Reserve(ctx, "einvoice", "k3", tenantID, "h1", now, timeout)
		require.Equal(t, Reserved, r.Outcome)
		require.NoError(t, store.Release(ctx, "einvoice", "k3", uuid.New())) // wrong owner: no effect
		again, _ := store.Reserve(ctx, "einvoice", "k3", tenantID, "h1", now, timeout)
		assert.Equal(t, InProgress, again.Outcome)
		require.NoError(t, store.Release(ctx, "einvoice", "k3", r.Owner))
		fresh, _ := store.Reserve(ctx, "einvoice", "k3", tenantID, "h1", now, timeout)
		assert.Equal(t, Reserved, fresh.Outcome)
	})

	t.Run("stale_pending_is_taken_over_and_late_owner_is_ignored", func(t *testing.T) {
		old, _ := store.Reserve(ctx, "einvoice", "k4", tenantID, "h1", now, timeout)
		require.Equal(t, Reserved, old.Outcome)

		later := now.Add(timeout + time.Second)
		taken, err := store.Reserve(ctx, "einvoice", "k4", tenantID, "h1", later, timeout)
		require.NoError(t, err)
		require.Equal(t, Reserved, taken.Outcome)
		require.NotEqual(t, old.Owner, taken.Owner)

		// the old owner finishes late: neither Complete nor Release touches the new record
		require.NoError(t, store.Complete(ctx, "einvoice", "k4", old.Owner, 200, []byte(`{"old":true}`)))
		require.NoError(t, store.Release(ctx, "einvoice", "k4", old.Owner))
		still, _ := store.Reserve(ctx, "einvoice", "k4", tenantID, "h1", later, timeout)
		assert.Equal(t, InProgress, still.Outcome)

		require.NoError(t, store.Complete(ctx, "einvoice", "k4", taken.Owner, 200, []byte(`{"new":true}`)))
		done, _ := store.Reserve(ctx, "einvoice", "k4", tenantID, "h1", later, timeout)
		assert.Equal(t, Replay, done.Outcome)
		assert.JSONEq(t, `{"new":true}`, string(done.StoredBody))
	})

	t.Run("fresh_pending_is_not_taken_over", func(t *testing.T) {
		_, _ = store.Reserve(ctx, "einvoice", "k5", tenantID, "h1", now, timeout)
		r, _ := store.Reserve(ctx, "einvoice", "k5", tenantID, "h1", now.Add(timeout-time.Second), timeout)
		assert.Equal(t, InProgress, r.Outcome)
	})

	t.Run("stale_pending_with_different_hash_conflicts", func(t *testing.T) {
		_, _ = store.Reserve(ctx, "einvoice", "k6", tenantID, "h1", now, timeout)
		r, _ := store.Reserve(ctx, "einvoice", "k6", tenantID, "h2", now.Add(time.Hour), timeout)
		assert.Equal(t, Conflict, r.Outcome)
	})

	t.Run("parallel_reserve_has_single_winner", func(t *testing.T) {
		const n = 16
		results := make(chan Outcome, n)
		var wg sync.WaitGroup
		for i := 0; i < n; i++ {
			wg.Add(1)
			go func() {
				defer wg.Done()
				r, err := store.Reserve(ctx, "einvoice", "k7", tenantID, "h1", now, timeout)
				assert.NoError(t, err)
				results <- r.Outcome
			}()
		}
		wg.Wait()
		close(results)
		reserved := 0
		for o := range results {
			if o == Reserved {
				reserved++
			} else {
				assert.Equal(t, InProgress, o)
			}
		}
		assert.Equal(t, 1, reserved)
	})
}

func TestMemStore_Contract(t *testing.T) { storeContract(t, newMemStore()) }
