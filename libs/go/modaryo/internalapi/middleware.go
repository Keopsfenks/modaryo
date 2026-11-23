package internalapi

import (
	"bytes"
	"context"
	"errors"
	"io"
	"log/slog"
	"mime"
	"net/http"
	"path"
	"strconv"
	"strings"
	"time"

	"github.com/google/uuid"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/problem"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/tenant"
)

// Error codes of the internal API contract.
const (
	CodeUnauthorized     = "internal_unauthorized"
	CodeForbidden        = "internal_forbidden"
	CodeTenantInvalid    = "internal_tenant_invalid"
	CodeBodyTooLarge     = "internal_body_too_large"
	CodeKeyRequired      = "idempotency_key_required"
	CodeConflict         = "idempotency_conflict"
	CodeInProgress       = "idempotency_in_progress"
	CodeMethodNotAllowed = "internal_method_not_allowed"
	// CodeUnavailable: the idempotency store failed (Go services; .NET answers 500). Retry with the same key.
	CodeUnavailable = "internal_unavailable"
	CodeBadPath     = "internal_bad_path"

	// PingPath is the one internal path that does not need a tenant header.
	PingPath = "/internal/ping"

	maxIdempotencyKeyLength = 200
)

// Caller is a service allowed to call this one. Routes are allowed path prefixes
// (e.g. "/internal/einvoice/"; "/internal/ping" is listed separately).
type Caller struct {
	Secret []byte
	Routes []string
}

// Callers maps the X-Modaryo-Service name to its configuration.
type Callers map[string]Caller

// Options configure Middleware. Zero values take the contract defaults.
type Options struct {
	Callers Callers
	// MaxSkew is the allowed timestamp deviation (default 300 s).
	MaxSkew time.Duration
	// MaxBodyBytes caps the request body (default 1 MiB).
	MaxBodyBytes int64
	// PendingTimeout after which a pending idempotency record can be taken over (default 5 min).
	PendingTimeout time.Duration
	// Store keeps idempotency records. nil means this service accepts no internal writes (405).
	Store IdempotencyStore
	// Now defaults to time.Now.
	Now func() time.Time
	// Logger defaults to slog.Default().
	Logger *slog.Logger
}

type callerKey struct{}

// CallerFromContext returns the verified calling service name set by Middleware.
func CallerFromContext(ctx context.Context) (string, bool) {
	s, ok := ctx.Value(callerKey{}).(string)
	return s, ok
}

// Middleware verifies "/internal/*" requests like the .NET InternalAuthMiddleware: body-free
// pre-checks, bounded body read, HMAC signature, route allow-list, tenant header and, for
// writes, idempotency. Requests for which IsInternalPath is false pass through untouched. On
// success the context carries the caller (CallerFromContext) and the tenant (tenant.FromContext).
func Middleware(opts Options) func(http.Handler) http.Handler {
	if opts.MaxSkew <= 0 {
		opts.MaxSkew = 300 * time.Second
	}
	if opts.MaxBodyBytes <= 0 {
		opts.MaxBodyBytes = 1 << 20
	}
	if opts.PendingTimeout <= 0 {
		opts.PendingTimeout = 5 * time.Minute
	}
	if opts.Now == nil {
		opts.Now = time.Now
	}
	if opts.Logger == nil {
		opts.Logger = slog.Default()
	}
	m := &verifier{opts}
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			p := r.URL.Path
			if !IsInternalPath(p) {
				next.ServeHTTP(w, r)
				return
			}
			clean := path.Clean(p)
			// "/internal/ping/" is fine; any other non-canonical spelling is rejected before anything else.
			canon := p
			if len(canon) > 1 && strings.HasSuffix(canon, "/") {
				canon = canon[:len(canon)-1]
			}
			if canon != clean {
				writeProblem(w, http.StatusBadRequest, CodeBadPath, "Geçersiz istek.", "İç API yolu normalleştirilmiş biçimde olmalıdır.")
				return
			}
			m.serve(w, r, next, clean)
		})
	}
}

// settleTimeout bounds each idempotency Complete/Release call (a variable so tests can shorten it).
var settleTimeout = 5 * time.Second

type verifier struct{ opts Options }

func (v *verifier) serve(w http.ResponseWriter, r *http.Request, next http.Handler, cleanPath string) {
	service := r.Header.Get(HeaderService)
	signature := r.Header.Get(HeaderSignature)
	ts, tsOK := parseTimestamp(r.Header.Get(HeaderTimestamp))
	if service == "" || signature == "" || !tsOK {
		unauthorized(w)
		return
	}
	caller, known := v.opts.Callers[service]
	if !known || len(caller.Secret) == 0 {
		unauthorized(w)
		return
	}
	// ts is >= 0 (digits only) and now is >= 0, so neither subtraction can overflow.
	now := v.opts.Now().Unix()
	skew := now - ts
	if ts > now {
		skew = ts - now
	}
	if skew > int64(v.opts.MaxSkew/time.Second) {
		unauthorized(w)
		return
	}

	max := v.opts.MaxBodyBytes
	if r.ContentLength > max {
		bodyTooLarge(w)
		return
	}
	body, err := io.ReadAll(io.LimitReader(r.Body, max+1))
	if err != nil {
		unauthorized(w)
		return
	}
	if int64(len(body)) > max {
		bodyTooLarge(w)
		return
	}
	r.Body = io.NopCloser(bytes.NewReader(body))

	pathAndQuery := rawPathAndQuery(r)
	tenantHeader := r.Header.Get(HeaderTenant)
	key := r.Header.Get(HeaderIdempotencyKey)

	if !Verify(caller.Secret, r.Method, pathAndQuery, ts, tenantHeader, key, body, signature) {
		unauthorized(w)
		return
	}
	if !routeAllowed(caller, cleanPath) {
		writeProblem(w, http.StatusForbidden, CodeForbidden, "İstek reddedildi.", "Bu servis bu yola erişemez.")
		return
	}

	var tenantID uuid.UUID
	hasTenant := false
	if tenantHeader != "" {
		parsed, err := uuid.Parse(tenantHeader)
		if err != nil || parsed == uuid.Nil {
			writeProblem(w, http.StatusBadRequest, CodeTenantInvalid, "Geçersiz istek.",
				"'"+HeaderTenant+"' başlığı geçerli bir firma kimliği olmalıdır.")
			return
		}
		tenantID, hasTenant = parsed, true
	} else if !isPing(cleanPath) {
		writeProblem(w, http.StatusBadRequest, CodeTenantInvalid, "Geçersiz istek.",
			"'"+HeaderTenant+"' başlığı zorunludur.")
		return
	}

	ctx := context.WithValue(r.Context(), callerKey{}, service)
	if hasTenant {
		ctx = tenant.WithTenant(ctx, tenantID)
	}
	r = r.WithContext(ctx)

	if isSafeMethod(r.Method) || !hasTenant {
		next.ServeHTTP(w, r)
		return
	}
	v.runIdempotent(w, r, next, service, tenantID, pathAndQuery, tenantHeader, key, body)
}

func (v *verifier) runIdempotent(w http.ResponseWriter, r *http.Request, next http.Handler,
	caller string, tenantID uuid.UUID, pathAndQuery, tenantHeader, key string, body []byte) {
	store := v.opts.Store
	if store == nil {
		w.Header().Set("Allow", "GET, HEAD")
		writeProblem(w, http.StatusMethodNotAllowed, CodeMethodNotAllowed, "Yöntem desteklenmiyor.",
			"Bu servis iç API yazma isteklerini kabul etmez.")
		return
	}
	if key == "" || len(key) > maxIdempotencyKeyLength {
		writeProblem(w, http.StatusBadRequest, CodeKeyRequired, "Geçersiz istek.",
			"Yazma istekleri için '"+HeaderIdempotencyKey+"' başlığı zorunludur.")
		return
	}

	ctx := r.Context()
	hash := RequestHash(r.Method, pathAndQuery, tenantHeader, body)
	res, err := store.Reserve(ctx, caller, key, tenantID, hash, v.opts.Now(), v.opts.PendingTimeout)
	if err != nil {
		if !errors.Is(err, context.Canceled) && !errors.Is(err, context.DeadlineExceeded) {
			v.opts.Logger.Error("internal api idempotency reserve failed", "caller", caller, "key", key, "error", err)
		}
		writeProblem(w, http.StatusServiceUnavailable, CodeUnavailable, "Hizmet geçici olarak kullanılamıyor.", "")
		return
	}
	switch res.Outcome {
	case Conflict:
		writeProblem(w, http.StatusConflict, CodeConflict, "Çakışma.",
			"Bu idempotency anahtarı farklı bir istek için kullanılmış.")
		return
	case InProgress:
		w.Header().Set("Retry-After", "1")
		writeProblem(w, http.StatusConflict, CodeInProgress, "Çakışma.",
			"Aynı idempotency anahtarlı istek hâlâ işleniyor.")
		return
	case Replay:
		if len(res.StoredBody) > 0 {
			w.Header().Set("Content-Type", "application/json; charset=utf-8")
		}
		w.WriteHeader(res.StoredStatus)
		if len(res.StoredBody) > 0 {
			_, _ = w.Write(res.StoredBody)
		}
		return
	}

	// Reserved. Once the handler returned a stored outcome (see keepRecord) the operation may
	// have been applied: the record is then never released. 5xx, problem+json 4xx and panics
	// release it so a retry with the same key runs again.
	handled := false
	defer func() {
		if !handled {
			sctx, cancel := settleContext(ctx)
			defer cancel()
			if err := store.Release(sctx, caller, key, res.Owner); err != nil {
				v.opts.Logger.Warn("internal api idempotency release failed", "caller", caller, "key", key, "error", err)
			}
		}
	}()

	rec := &bufferedWriter{ResponseWriter: w, status: http.StatusOK}
	next.ServeHTTP(rec, r)

	if keepRecord(rec.status, w.Header()) {
		handled = true
		sctx, cancel := settleContext(ctx)
		err := store.Complete(sctx, caller, key, res.Owner, rec.status, rec.buf.Bytes())
		cancel()
		if err != nil {
			// The record stays pending and can be taken over after the timeout; the response still goes out.
			v.opts.Logger.Warn("internal api idempotency complete failed", "caller", caller, "key", key, "error", err)
		}
	}
	rec.flush()
}

// settleContext is used for Complete/Release: it survives a cancelled request (the outcome must
// still be recorded) but is bounded so a hung store cannot hold the response.
func settleContext(ctx context.Context) (context.Context, context.CancelFunc) {
	return context.WithTimeout(context.WithoutCancel(ctx), settleTimeout)
}

// keepRecord reports whether a handler response is stored for replay. Like ERP, where business,
// validation, not-found and authorization errors are exceptions that release the key, a 4xx
// "application/problem+json" response is an error outcome and is released, so a corrected retry
// with the same key runs again. 5xx is released too. Every other status < 500 (2xx, 3xx and 4xx
// with another content type) is stored.
func keepRecord(status int, h http.Header) bool {
	if status >= 500 {
		return false
	}
	if status >= 400 {
		if mt, _, err := mime.ParseMediaType(h.Get("Content-Type")); err == nil && strings.EqualFold(mt, "application/problem+json") {
			return false
		}
	}
	return true
}

// bufferedWriter holds status and body until the idempotency record is settled; headers go
// straight to the real writer's header map.
type bufferedWriter struct {
	http.ResponseWriter
	status      int
	wroteHeader bool
	buf         bytes.Buffer
}

func (b *bufferedWriter) WriteHeader(status int) {
	if b.wroteHeader {
		return
	}
	b.wroteHeader = true
	b.status = status
}

func (b *bufferedWriter) Write(p []byte) (int, error) {
	b.wroteHeader = true
	return b.buf.Write(p)
}

func (b *bufferedWriter) flush() {
	b.ResponseWriter.WriteHeader(b.status)
	if b.buf.Len() > 0 {
		_, _ = b.ResponseWriter.Write(b.buf.Bytes())
	}
}

func routeAllowed(c Caller, path string) bool {
	for _, route := range c.Routes {
		if route == "" {
			continue
		}
		prefix := strings.TrimRight(route, "/")
		if strings.EqualFold(path, prefix) ||
			(len(path) > len(prefix) && path[len(prefix)] == '/' && strings.EqualFold(path[:len(prefix)], prefix)) {
			return true
		}
	}
	return false
}

// IsInternalPath reports whether Middleware treats p (r.URL.Path) as an internal API path:
// "/internal" or below it, case-insensitive, in its raw or its path.Clean form. Routers use it to
// send these requests to the internal branch (Middleware + internal router) and everything else to
// the public chain (csrf, session, tenant), like ERP's UseWhen(!InternalAuthMiddleware.IsInternalPath).
func IsInternalPath(p string) bool {
	return underInternal(p) || underInternal(path.Clean(p))
}

func underInternal(p string) bool {
	const prefix = "/internal"
	return strings.EqualFold(p, prefix) ||
		(len(p) > len(prefix) && p[len(prefix)] == '/' && strings.EqualFold(p[:len(prefix)], prefix))
}

func isPing(path string) bool { return strings.EqualFold(strings.TrimRight(path, "/"), PingPath) }

func isSafeMethod(m string) bool {
	return strings.EqualFold(m, http.MethodGet) || strings.EqualFold(m, http.MethodHead)
}

// parseTimestamp accepts digits only (no sign, no spaces), like the .NET NumberStyles.None.
func parseTimestamp(s string) (int64, bool) {
	if s == "" {
		return 0, false
	}
	for i := 0; i < len(s); i++ {
		if s[i] < '0' || s[i] > '9' {
			return 0, false
		}
	}
	n, err := strconv.ParseInt(s, 10, 64)
	return n, err == nil
}

// rawPathAndQuery is the request target exactly as the client sent it.
func rawPathAndQuery(r *http.Request) string {
	if strings.HasPrefix(r.RequestURI, "/") {
		return r.RequestURI
	}
	p := r.URL.EscapedPath()
	if r.URL.RawQuery != "" || r.URL.ForceQuery {
		p += "?" + r.URL.RawQuery
	}
	return p
}

func unauthorized(w http.ResponseWriter) {
	writeProblem(w, http.StatusUnauthorized, CodeUnauthorized, "Kimlik doğrulanamadı.", "İç API isteği doğrulanamadı.")
}

func bodyTooLarge(w http.ResponseWriter) {
	writeProblem(w, http.StatusRequestEntityTooLarge, CodeBodyTooLarge, "İstek çok büyük.",
		"İç API istek gövdesi izin verilen boyutu aşıyor.")
}

// writeProblem uses the same titles, details and types as the .NET middleware (TypedResults.Problem).
func writeProblem(w http.ResponseWriter, status int, code, title, detail string) {
	d := problem.App(status, code, title)
	d.Type = problem.TypeFor(status)
	d.Detail = detail
	problem.Write(w, d)
}
