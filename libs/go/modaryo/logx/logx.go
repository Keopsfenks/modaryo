// Package logx provides structured logging and a request-logging middleware.
//
// RequestMiddleware stores a mutable per-request record in the context. Code that learns the
// tenant later in the chain (tenant.WithTenant does this) calls SetTenant, so the final access
// log line and FromContext loggers carry tenant_id even though the logging middleware runs
// outermost. Headers, cookies, bodies and query strings are never logged.
package logx

import (
	"bufio"
	"context"
	"crypto/rand"
	"encoding/hex"
	"io"
	"log/slog"
	"net"
	"net/http"
	"os"
	"sync"
	"time"

	"github.com/google/uuid"
)

const requestIDHeader = "X-Request-Id"

type ctxKey struct{}

type requestInfo struct {
	mu       sync.Mutex
	id       string
	tenantID string
	logger   *slog.Logger
}

// New returns a logger that writes text in development and JSON otherwise; every record carries
// the service name.
func New(service string, dev bool) *slog.Logger {
	return NewWithWriter(service, dev, os.Stderr)
}

// NewWithWriter is New writing to w instead of stderr.
func NewWithWriter(service string, dev bool, w io.Writer) *slog.Logger {
	var h slog.Handler
	if dev {
		h = slog.NewTextHandler(w, nil)
	} else {
		h = slog.NewJSONHandler(w, nil)
	}
	return slog.New(h).With("service", service)
}

// RequestID returns the id of the current request, or "".
func RequestID(ctx context.Context) string {
	if ri, ok := ctx.Value(ctxKey{}).(*requestInfo); ok {
		return ri.id
	}
	return ""
}

// SetTenant records the tenant of the current request for logging. It is a no-op outside
// RequestMiddleware.
func SetTenant(ctx context.Context, id uuid.UUID) {
	if ri, ok := ctx.Value(ctxKey{}).(*requestInfo); ok {
		ri.mu.Lock()
		ri.tenantID = id.String()
		ri.mu.Unlock()
	}
}

// FromContext returns the request logger enriched with request_id and tenant_id (when known).
// Outside RequestMiddleware it returns slog.Default().
func FromContext(ctx context.Context) *slog.Logger {
	ri, ok := ctx.Value(ctxKey{}).(*requestInfo)
	if !ok {
		return slog.Default()
	}
	ri.mu.Lock()
	defer ri.mu.Unlock()
	l := ri.logger.With("request_id", ri.id)
	if ri.tenantID != "" {
		l = l.With("tenant_id", ri.tenantID)
	}
	return l
}

// RequestMiddleware logs one line per finished request: method, path (no query string), status,
// duration_ms, bytes, request_id and tenant_id (if known). It accepts an incoming X-Request-Id
// only when it is 1-128 chars of [A-Za-z0-9._-], otherwise generates one, and echoes it in the
// response header.
func RequestMiddleware(l *slog.Logger) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			start := time.Now()
			id := r.Header.Get(requestIDHeader)
			if !validRequestID(id) {
				id = newRequestID()
			}
			w.Header().Set(requestIDHeader, id)

			ri := &requestInfo{id: id, logger: l}
			ctx := context.WithValue(r.Context(), ctxKey{}, ri)
			sw := &statusWriter{ResponseWriter: w, status: http.StatusOK}
			// Logged in a defer so that a panicking handler still produces an access line
			// (status 500 when no header was written); the panic is then re-raised.
			defer func() {
				rec := recover()
				status := sw.status
				if rec != nil && !sw.wroteHeader {
					status = http.StatusInternalServerError
				}
				FromContext(ctx).LogAttrs(ctx, slog.LevelInfo, "request",
					slog.String("method", r.Method),
					slog.String("path", r.URL.Path),
					slog.Int("status", status),
					slog.Int64("duration_ms", time.Since(start).Milliseconds()),
					slog.Int64("bytes", sw.bytes),
				)
				if rec != nil {
					panic(rec)
				}
			}()
			next.ServeHTTP(sw, r.WithContext(ctx))
		})
	}
}

func validRequestID(s string) bool {
	if len(s) == 0 || len(s) > 128 {
		return false
	}
	for i := 0; i < len(s); i++ {
		c := s[i]
		switch {
		case c >= 'a' && c <= 'z', c >= 'A' && c <= 'Z', c >= '0' && c <= '9', c == '.', c == '_', c == '-':
		default:
			return false
		}
	}
	return true
}

func newRequestID() string {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		return uuid.NewString()
	}
	return hex.EncodeToString(b[:])
}

type statusWriter struct {
	http.ResponseWriter
	status      int
	bytes       int64
	wroteHeader bool
}

func (w *statusWriter) WriteHeader(code int) {
	// 1xx informational responses (except 101) are not final; do not record them.
	if code >= 100 && code < 200 && code != http.StatusSwitchingProtocols {
		w.ResponseWriter.WriteHeader(code)
		return
	}
	if !w.wroteHeader {
		w.status = code
		w.wroteHeader = true
	}
	w.ResponseWriter.WriteHeader(code)
}

func (w *statusWriter) Write(b []byte) (int, error) {
	w.wroteHeader = true
	n, err := w.ResponseWriter.Write(b)
	w.bytes += int64(n)
	return n, err
}

// Unwrap lets http.ResponseController reach Flush and friends.
func (w *statusWriter) Unwrap() http.ResponseWriter { return w.ResponseWriter }

// Flush implements http.Flusher when the underlying writer supports it.
func (w *statusWriter) Flush() {
	if f, ok := w.ResponseWriter.(http.Flusher); ok {
		w.wroteHeader = true
		f.Flush()
	}
}

// Hijack implements http.Hijacker when the underlying writer supports it.
func (w *statusWriter) Hijack() (net.Conn, *bufio.ReadWriter, error) {
	if h, ok := w.ResponseWriter.(http.Hijacker); ok {
		return h.Hijack()
	}
	return nil, nil, http.ErrNotSupported
}
