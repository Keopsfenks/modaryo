package internalapi

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/problem"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/tenant"
)

func newTestServer(t *testing.T) (*httptest.Server, *memStore) {
	t.Helper()
	store := newMemStore()
	mw := Middleware(Options{Callers: testCallers(), Store: store})
	srv := httptest.NewServer(mw(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		tid, _ := tenant.FromContext(r.Context())
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusCreated)
		_ = json.NewEncoder(w).Encode(map[string]any{"body": string(b), "tenant": tid.String(), "query": r.URL.RawQuery})
	})))
	t.Cleanup(srv.Close)
	return srv, store
}

func TestClient_RoundTripAgainstMiddleware(t *testing.T) {
	srv, _ := newTestServer(t)
	c := NewClient(srv.URL+"/", "einvoice", testSecret, nil)

	var out struct {
		Body   string `json:"body"`
		Tenant string `json:"tenant"`
		Query  string `json:"query"`
	}
	err := c.Do(context.Background(), "POST", "/internal/einvoice/send?z=1&a=%2F", testTenant, "idem-1", map[string]string{"x": "y"}, &out)
	require.NoError(t, err)
	assert.JSONEq(t, `{"x":"y"}`, out.Body)
	assert.Equal(t, testTenant.String(), out.Tenant)
	assert.Equal(t, "z=1&a=%2F", out.Query)

	// GET without tenant (ping) and without body
	require.NoError(t, c.Do(context.Background(), "GET", "/internal/ping", uuid.Nil, "", nil, nil))
}

func TestClient_DecodesProblemDetails(t *testing.T) {
	srv, _ := newTestServer(t)
	c := NewClient(srv.URL, "einvoice", testSecret, nil)

	err := c.Do(context.Background(), "GET", "/internal/other/x", testTenant, "", nil, nil)
	var pe *problem.Error
	require.True(t, errors.As(err, &pe), "got %v", err)
	assert.Equal(t, 403, pe.Status)
	assert.Equal(t, "internal_forbidden", pe.Code)

	// wrong secret -> 401
	bad := NewClient(srv.URL, "einvoice", []byte("wrong"), nil)
	err = bad.Do(context.Background(), "GET", "/internal/ping", uuid.Nil, "", nil, nil)
	require.True(t, errors.As(err, &pe))
	assert.Equal(t, "internal_unauthorized", pe.Code)

	// idempotency conflict
	require.NoError(t, c.Do(context.Background(), "POST", "/internal/einvoice/s", testTenant, "k", map[string]int{"a": 1}, nil))
	err = c.Do(context.Background(), "POST", "/internal/einvoice/s", testTenant, "k", map[string]int{"a": 2}, nil)
	require.True(t, errors.As(err, &pe))
	assert.Equal(t, 409, pe.Status)
	assert.Equal(t, "idempotency_conflict", pe.Code)
}

func TestClient_NonJsonErrorBody(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		http.Error(w, "gateway down", http.StatusBadGateway)
	}))
	defer srv.Close()
	err := NewClient(srv.URL, "einvoice", testSecret, nil).Do(context.Background(), "GET", "/internal/ping", uuid.Nil, "", nil, nil)
	var pe *problem.Error
	require.True(t, errors.As(err, &pe))
	assert.Equal(t, 502, pe.Status)
}

func TestClient_CallsErpPing(t *testing.T) {
	url := os.Getenv("MODARYO_IT_ERP_URL")
	if url == "" {
		t.Skip("MODARYO_IT_ERP_URL not set")
	}
	service, secret := os.Getenv("MODARYO_IT_ERP_SERVICE"), os.Getenv("MODARYO_IT_ERP_SECRET")
	if service == "" || secret == "" {
		t.Skip("MODARYO_IT_ERP_SERVICE / MODARYO_IT_ERP_SECRET not set")
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	require.NoError(t, NewClient(url, service, []byte(secret), nil).Do(ctx, "GET", "/internal/ping", uuid.Nil, "", nil, nil))
}

func TestClient_DoesNotFollowRedirects(t *testing.T) {
	var hits int
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		hits++
		if r.URL.Path == "/internal/ping" {
			http.Redirect(w, r, "/elsewhere", http.StatusTemporaryRedirect)
		}
	}))
	defer srv.Close()
	own := &http.Client{}
	err := NewClient(srv.URL, "einvoice", testSecret, own).Do(context.Background(), "GET", "/internal/ping", uuid.Nil, "", nil, nil)
	var pe *problem.Error
	require.True(t, errors.As(err, &pe))
	assert.Equal(t, 307, pe.Status)
	assert.Equal(t, 1, hits)
	assert.Nil(t, own.CheckRedirect, "caller's client must not be mutated")
}
