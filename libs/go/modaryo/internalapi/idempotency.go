package internalapi

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"time"

	"github.com/google/uuid"
)

// Outcome is the result of IdempotencyStore.Reserve.
type Outcome int

const (
	// Reserved: the key now belongs to this request; run it.
	Reserved Outcome = iota
	// Replay: the same request was completed before; answer with the stored response.
	Replay
	// Conflict: the key was used with a different request.
	Conflict
	// InProgress: the same request is still being processed.
	InProgress
)

// Reservation is what Reserve returns. Owner is set for Reserved; StoredStatus/StoredBody for Replay.
type Reservation struct {
	Outcome      Outcome
	Owner        uuid.UUID
	StoredStatus int
	StoredBody   []byte
}

// IdempotencyStore keeps idempotency records keyed by (caller, key). Same semantics as
// the .NET IInternalIdempotencyStore.
type IdempotencyStore interface {
	// Reserve inserts a pending record (race safe) or reports the state of the existing one.
	// A pending record older than pendingTimeout with the same hash is taken over (at most once).
	Reserve(ctx context.Context, caller, key string, tenant uuid.UUID, requestHash string, now time.Time, pendingTimeout time.Duration) (Reservation, error)
	// Complete stores the response; it does nothing if owner no longer owns the record.
	Complete(ctx context.Context, caller, key string, owner uuid.UUID, status int, body []byte) error
	// Release deletes the pending record; it does nothing if owner no longer owns it.
	Release(ctx context.Context, caller, key string, owner uuid.UUID) error
}

// RequestHash is sha256hex(method \n pathAndQuery \n tenant \n body), exactly as .NET computes it.
func RequestHash(method, pathAndQuery, tenant string, body []byte) string {
	h := sha256.New()
	h.Write([]byte(method + "\n" + pathAndQuery + "\n" + tenant + "\n"))
	h.Write(body)
	return hex.EncodeToString(h.Sum(nil))
}
