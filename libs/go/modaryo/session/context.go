package session

import "context"

type ctxKey struct{}

// WithRecord puts the record into the context (used by the middleware and tests).
func WithRecord(ctx context.Context, rec *Record) context.Context {
	return context.WithValue(ctx, ctxKey{}, rec)
}

// FromContext returns the session record the middleware stored.
func FromContext(ctx context.Context) (*Record, bool) {
	rec, ok := ctx.Value(ctxKey{}).(*Record)
	return rec, ok && rec != nil
}
