// Package tenant carries the active company (tenant) id of a request.
package tenant

import (
	"context"
	"net/http"

	"github.com/google/uuid"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/logx"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/problem"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/session"
)

type ctxKey struct{}

// WithTenant puts the tenant id into the context.
func WithTenant(ctx context.Context, id uuid.UUID) context.Context {
	logx.SetTenant(ctx, id) // lets the outer request-log middleware report tenant_id
	return context.WithValue(ctx, ctxKey{}, id)
}

// FromContext returns the tenant id stored by WithTenant or FromSession.
func FromContext(ctx context.Context) (uuid.UUID, bool) {
	id, ok := ctx.Value(ctxKey{}).(uuid.UUID)
	return id, ok
}

// FromSession copies the tenant of the session record (put there by session.Middleware) into the
// context. No session record yields 401 "unauthorized"; a session without an active company
// yields 403 "tenant_required" ("Önce bir firma seçin."). ERP still answers 403 "forbidden" in
// that case and will adopt the code later.
func FromSession(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		rec, ok := session.FromContext(r.Context())
		if !ok {
			problem.Write(w, problem.Unauthorized())
			return
		}
		if rec.TenantID == nil {
			d := problem.Forbidden()
			d.Title = "Önce bir firma seçin."
			d.Code = "tenant_required"
			problem.Write(w, d)
			return
		}
		next.ServeHTTP(w, r.WithContext(WithTenant(r.Context(), *rec.TenantID)))
	})
}
