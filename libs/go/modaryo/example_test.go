package modaryo_test

import (
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"time"

	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/csrf"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/internalapi"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/logx"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/session"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/tenant"
)

// exampleStore accepts every write once; a real service uses internalapi.NewPostgresStore.
type exampleStore struct{}

func (exampleStore) Reserve(context.Context, string, string, uuid.UUID, string, time.Time, time.Duration) (internalapi.Reservation, error) {
	return internalapi.Reservation{Outcome: internalapi.Reserved, Owner: uuid.New()}, nil
}
func (exampleStore) Complete(context.Context, string, string, uuid.UUID, int, []byte) error {
	return nil
}
func (exampleStore) Release(context.Context, string, string, uuid.UUID) error { return nil }

// serviceHandler builds the canonical chain described in the package doc.
func serviceHandler(l *slog.Logger, reader *session.Reader, internalOpts internalapi.Options) http.Handler {
	// Internal router: sees the full "/internal/..." path (no StripPrefix).
	internalRouter := http.NewServeMux()
	internalRouter.HandleFunc("POST /internal/einvoice/documents", func(w http.ResponseWriter, r *http.Request) {
		caller, _ := internalapi.CallerFromContext(r.Context())
		t, _ := tenant.FromContext(r.Context())
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusCreated)
		_ = json.NewEncoder(w).Encode(map[string]string{"caller": caller, "tenant": t.String()})
	})
	internal := internalapi.Middleware(internalOpts)(internalRouter)

	// Public application routes: CSRF, then the session, then the active company.
	app := http.NewServeMux()
	app.HandleFunc("GET /api/einvoice/documents", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
	})
	app.HandleFunc("POST /api/einvoice/documents", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusCreated)
	})
	public := csrf.Middleware(session.Middleware(reader, session.Options{})(tenant.FromSession(app)))

	router := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if internalapi.IsInternalPath(r.URL.Path) {
			internal.ServeHTTP(w, r)
			return
		}
		public.ServeHTTP(w, r)
	})
	return logx.RequestMiddleware(l)(logx.Recover(router))
}

func show(h http.Handler, r *http.Request) {
	w := httptest.NewRecorder()
	h.ServeHTTP(w, r)
	var body struct {
		Code   string `json:"code"`
		Caller string `json:"caller"`
		Tenant string `json:"tenant"`
	}
	_ = json.Unmarshal(w.Body.Bytes(), &body)
	switch {
	case body.Code != "":
		fmt.Println(w.Code, body.Code)
	case body.Caller != "":
		fmt.Println(w.Code, body.Caller, body.Tenant)
	default:
		fmt.Println(w.Code)
	}
}

// Example_service wires a Go service the way ERP does: signed "/internal/*" requests skip the
// CSRF and session rules, every other request goes through them.
func Example_service() {
	now := time.Unix(1795200000, 0)
	secret := []byte("example-secret")
	// The reader only talks to Valkey for requests that carry a session cookie; none do here.
	rdb := redis.NewClient(&redis.Options{Addr: "localhost:6379", DB: 4})
	defer func() { _ = rdb.Close() }()

	h := serviceHandler(slog.New(slog.DiscardHandler), session.NewReader(rdb, nil), internalapi.Options{
		Callers: internalapi.Callers{"erp": {Secret: secret, Routes: []string{"/internal/einvoice/"}}},
		Store:   exampleStore{},
		Now:     func() time.Time { return now },
	})

	// 1. Signed internal POST without the CSRF header: accepted.
	const target, tenantID, key, body = "/internal/einvoice/documents", "0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b21", "doc-1", `{"no":"F-1"}`
	r := httptest.NewRequest(http.MethodPost, target, strings.NewReader(body))
	r.Header.Set(internalapi.HeaderService, "erp")
	r.Header.Set(internalapi.HeaderTimestamp, strconv.FormatInt(now.Unix(), 10))
	r.Header.Set(internalapi.HeaderTenant, tenantID)
	r.Header.Set(internalapi.HeaderIdempotencyKey, key)
	r.Header.Set(internalapi.HeaderSignature, internalapi.Sign(secret, http.MethodPost, target, now.Unix(), tenantID, key, []byte(body)))
	show(h, r)

	// 2. Unsigned public POST without the CSRF header: rejected by csrf.
	show(h, httptest.NewRequest(http.MethodPost, "/api/einvoice/documents", strings.NewReader(`{}`)))

	// 3. Public GET without a session cookie: rejected by session.
	show(h, httptest.NewRequest(http.MethodGet, "/api/einvoice/documents", nil))

	// Output:
	// 201 erp 0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b21
	// 403 csrf_header_missing
	// 401 unauthorized
}
