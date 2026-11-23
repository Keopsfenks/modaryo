package tenant_test

import (
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/session"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/tenant"
)

func run(rec *session.Record) (*httptest.ResponseRecorder, uuid.UUID, bool) {
	var got uuid.UUID
	var ok bool
	next := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		got, ok = tenant.FromContext(r.Context())
		w.WriteHeader(http.StatusNoContent)
	})
	req := httptest.NewRequest(http.MethodGet, "/x", nil)
	if rec != nil {
		req = req.WithContext(session.WithRecord(req.Context(), rec))
	}
	rr := httptest.NewRecorder()
	tenant.FromSession(next).ServeHTTP(rr, req)
	return rr, got, ok
}

func TestTenant_NoTenantInSession_403(t *testing.T) {
	rr, _, ok := run(&session.Record{})

	assert.Equal(t, http.StatusForbidden, rr.Code)
	assert.False(t, ok)
	assert.JSONEq(t, `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"Önce bir firma seçin.","status":403,"code":"tenant_required"}`, rr.Body.String())
}

func TestTenant_NoSession_401(t *testing.T) {
	rr, _, _ := run(nil)

	assert.Equal(t, http.StatusUnauthorized, rr.Code)
}

func TestTenant_FromSession_SetsTenant(t *testing.T) {
	id := uuid.New()
	rr, got, ok := run(&session.Record{TenantID: &id})

	assert.Equal(t, http.StatusNoContent, rr.Code)
	require.True(t, ok)
	assert.Equal(t, id, got)
}

func TestTenant_WithTenantRoundTrip(t *testing.T) {
	id := uuid.New()
	got, ok := tenant.FromContext(tenant.WithTenant(t.Context(), id))

	assert.True(t, ok)
	assert.Equal(t, id, got)
	_, ok = tenant.FromContext(t.Context())
	assert.False(t, ok)
}
