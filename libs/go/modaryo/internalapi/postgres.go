package internalapi

import (
	"context"
	_ "embed"
	"errors"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
)

// IdempotencySchemaSQL is the migration snippet that creates the idempotency_records table.
// Services run it in their own migrations.
//
//go:embed idempotency.sql
var IdempotencySchemaSQL string

// IdempotencyRetention is how long records are kept; PostgresStore.Purge(now - retention).
const IdempotencyRetention = 7 * 24 * time.Hour

// PostgresStore is the Postgres implementation of IdempotencyStore.
type PostgresStore struct {
	pool *pgxpool.Pool
}

// NewPostgresStore uses the idempotency_records table (see IdempotencySchemaSQL).
func NewPostgresStore(pool *pgxpool.Pool) *PostgresStore { return &PostgresStore{pool: pool} }

var _ IdempotencyStore = (*PostgresStore)(nil)

func (s *PostgresStore) Reserve(ctx context.Context, caller, key string, tenant uuid.UUID, requestHash string, now time.Time, pendingTimeout time.Duration) (Reservation, error) {
	for retried := false; ; retried = true {
		owner := uuid.New()
		tag, err := s.pool.Exec(ctx,
			`INSERT INTO idempotency_records (caller, key, tenant_id, request_hash, status, response_json, created_at, owner_token)
			 VALUES ($1, $2, $3, $4, 0, '', $5, $6) ON CONFLICT (caller, key) DO NOTHING`,
			caller, key, tenant, requestHash, now.UTC(), owner)
		if err != nil {
			return Reservation{}, err
		}
		if tag.RowsAffected() == 1 {
			return Reservation{Outcome: Reserved, Owner: owner}, nil
		}

		var (
			hash      string
			status    int
			response  string
			createdAt time.Time
			oldOwner  uuid.UUID
		)
		err = s.pool.QueryRow(ctx,
			`SELECT request_hash, status, response_json, created_at, owner_token FROM idempotency_records WHERE caller = $1 AND key = $2`,
			caller, key).Scan(&hash, &status, &response, &createdAt, &oldOwner)
		if errors.Is(err, pgx.ErrNoRows) {
			// Deleted in between (released after a 5xx): try once more.
			if retried {
				return Reservation{Outcome: InProgress}, nil
			}
			continue
		}
		if err != nil {
			return Reservation{}, err
		}

		if hash != requestHash {
			return Reservation{Outcome: Conflict}, nil
		}
		if status != 0 {
			return Reservation{Outcome: Replay, StoredStatus: status, StoredBody: []byte(response)}, nil
		}
		// Stale pending record (the processing instance may have crashed): take it over, once.
		if !retried && createdAt.Before(now.Add(-pendingTimeout)) {
			if _, err := s.pool.Exec(ctx,
				`DELETE FROM idempotency_records WHERE caller = $1 AND key = $2 AND status = 0 AND owner_token = $3`,
				caller, key, oldOwner); err != nil {
				return Reservation{}, err
			}
			continue
		}
		return Reservation{Outcome: InProgress}, nil
	}
}

func (s *PostgresStore) Complete(ctx context.Context, caller, key string, owner uuid.UUID, status int, body []byte) error {
	_, err := s.pool.Exec(ctx,
		`UPDATE idempotency_records SET status = $4, response_json = $5 WHERE caller = $1 AND key = $2 AND owner_token = $3`,
		caller, key, owner, status, string(body))
	return err
}

func (s *PostgresStore) Release(ctx context.Context, caller, key string, owner uuid.UUID) error {
	_, err := s.pool.Exec(ctx,
		`DELETE FROM idempotency_records WHERE caller = $1 AND key = $2 AND owner_token = $3`,
		caller, key, owner)
	return err
}

// Purge deletes records created before olderThan and returns how many were deleted.
func (s *PostgresStore) Purge(ctx context.Context, olderThan time.Time) (int64, error) {
	tag, err := s.pool.Exec(ctx, `DELETE FROM idempotency_records WHERE created_at < $1`, olderThan.UTC())
	if err != nil {
		return 0, err
	}
	return tag.RowsAffected(), nil
}
