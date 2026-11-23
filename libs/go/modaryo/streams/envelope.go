// Package streams consumes and produces the ERP event stream (contract "event v1") on Valkey
// Streams: consumer groups, retry through XAUTOCLAIM and a dead-letter stream.
package streams

import (
	"encoding/json"
	"time"

	"github.com/google/uuid"
)

// Version is the envelope version this package understands.
const Version = 1

// TimeFormat is the wire format of occurred_at: ISO-8601 UTC with milliseconds (as ERP writes it).
const TimeFormat = "2006-01-02T15:04:05.000Z"

// Envelope is the event v1 envelope carried in the "event" field of a stream entry.
type Envelope struct {
	V          int             `json:"v"`
	ID         uuid.UUID       `json:"id"`
	Type       string          `json:"type"`
	TenantID   uuid.UUID       `json:"tenant_id"`
	OccurredAt time.Time       `json:"occurred_at"`
	Actor      string          `json:"actor"`
	Data       json.RawMessage `json:"data"`
}

// MarshalJSON writes occurred_at as yyyy-MM-ddTHH:mm:ss.fffZ, like ERP does.
func (e Envelope) MarshalJSON() ([]byte, error) {
	type wire struct {
		V          int             `json:"v"`
		ID         uuid.UUID       `json:"id"`
		Type       string          `json:"type"`
		TenantID   uuid.UUID       `json:"tenant_id"`
		OccurredAt string          `json:"occurred_at"`
		Actor      string          `json:"actor"`
		Data       json.RawMessage `json:"data"`
	}
	data := e.Data
	if len(data) == 0 {
		data = json.RawMessage("null")
	}
	return json.Marshal(wire{
		V: e.V, ID: e.ID, Type: e.Type, TenantID: e.TenantID,
		OccurredAt: e.OccurredAt.UTC().Format(TimeFormat), Actor: e.Actor, Data: data,
	})
}
