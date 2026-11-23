package streams

import (
	"context"
	"encoding/json"
	"fmt"

	"github.com/redis/go-redis/v9"
)

// MaxLen is the approximate stream length kept by XADD MAXLEN ~ (same as ERP).
const MaxLen = 100000

// Produce appends the envelope to stream in the ERP entry format: fields type, tenant_id and
// event (the envelope JSON). A zero V is set to 1; any other version is rejected.
func Produce(ctx context.Context, rdb redis.UniversalClient, stream string, e Envelope) error {
	if e.V == 0 {
		e.V = Version
	}
	if e.V != Version {
		return fmt.Errorf("streams: unsupported envelope version %d", e.V)
	}
	raw, err := json.Marshal(e)
	if err != nil {
		return fmt.Errorf("streams: marshal envelope: %w", err)
	}
	return rdb.XAdd(ctx, &redis.XAddArgs{
		Stream: stream,
		MaxLen: MaxLen,
		Approx: true,
		Values: map[string]any{
			"type":      e.Type,
			"tenant_id": e.TenantID.String(),
			"event":     string(raw),
		},
	}).Err()
}
