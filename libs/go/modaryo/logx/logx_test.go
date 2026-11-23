package logx_test

import (
	"bytes"
	"encoding/json"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/logx"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/tenant"
)

func jsonLogger(buf *bytes.Buffer) *slog.Logger {
	return slog.New(slog.NewJSONHandler(buf, nil)).With("service", "test")
}

func TestLogx_New_CarriesService(t *testing.T) {
	assert.NotNil(t, logx.New("einvoice", true))
	assert.NotNil(t, logx.New("einvoice", false))
}

func TestLogx_RedactsCookieAndAuthorization(t *testing.T) {
	var buf bytes.Buffer
	h := logx.RequestMiddleware(jsonLogger(&buf))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write([]byte("body-secret-response"))
	}))
	req := httptest.NewRequest(http.MethodPost, "/invoices?token=secret", strings.NewReader("body-secret-request"))
	req.Header.Set("Cookie", "modaryo_session=sess-secret")
	req.Header.Set("Authorization", "Bearer auth-secret")
	req.Header.Set("X-Modaryo-Signature", "sig-secret")
	h.ServeHTTP(httptest.NewRecorder(), req)

	out := buf.String()
	require.NotEmpty(t, out)
	for _, s := range []string{"sess-secret", "auth-secret", "sig-secret", "token", "secret", "body-secret"} {
		assert.NotContains(t, out, s)
	}
	var rec map[string]any
	require.NoError(t, json.Unmarshal(buf.Bytes(), &rec))
	assert.Equal(t, "POST", rec["method"])
	assert.Equal(t, "/invoices", rec["path"])
	assert.EqualValues(t, 200, rec["status"])
	assert.EqualValues(t, len("body-secret-response"), rec["bytes"])
	assert.Contains(t, rec, "duration_ms")
	assert.Equal(t, "test", rec["service"])
	assert.NotEmpty(t, rec["request_id"])
}

func TestLogx_RequestID_EchoedWhenValid(t *testing.T) {
	var buf bytes.Buffer
	var inCtx string
	h := logx.RequestMiddleware(jsonLogger(&buf))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		inCtx = logx.RequestID(r.Context())
	}))
	req := httptest.NewRequest(http.MethodGet, "/x", nil)
	req.Header.Set("X-Request-Id", "abc-123_X.y")
	rr := httptest.NewRecorder()
	h.ServeHTTP(rr, req)
	assert.Equal(t, "abc-123_X.y", rr.Header().Get("X-Request-Id"))
	assert.Equal(t, "abc-123_X.y", inCtx)
	assert.Contains(t, buf.String(), `"request_id":"abc-123_X.y"`)
}

func TestLogx_RequestID_InvalidReplaced(t *testing.T) {
	for _, bad := range []string{"has space", "new\nline", "<script>", strings.Repeat("a", 129)} {
		h := logx.RequestMiddleware(jsonLogger(&bytes.Buffer{}))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {}))
		req := httptest.NewRequest(http.MethodGet, "/x", nil)
		req.Header["X-Request-Id"] = []string{bad}
		rr := httptest.NewRecorder()
		h.ServeHTTP(rr, req)
		got := rr.Header().Get("X-Request-Id")
		assert.NotEqual(t, bad, got)
		assert.NotEmpty(t, got)
	}
}

func TestLogx_RequestID_GeneratedWhenMissing(t *testing.T) {
	h := logx.RequestMiddleware(jsonLogger(&bytes.Buffer{}))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {}))
	rr := httptest.NewRecorder()
	h.ServeHTTP(rr, httptest.NewRequest(http.MethodGet, "/x", nil))
	assert.NotEmpty(t, rr.Header().Get("X-Request-Id"))
}

func TestLogx_TenantIDLoggedWhenSetDownstream(t *testing.T) {
	var buf bytes.Buffer
	id := uuid.New()
	h := logx.RequestMiddleware(jsonLogger(&buf))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		ctx := tenant.WithTenant(r.Context(), id)
		logx.FromContext(ctx).Info("ping")
	}))
	h.ServeHTTP(httptest.NewRecorder(), httptest.NewRequest(http.MethodGet, "/x", nil))
	lines := strings.Split(strings.TrimSpace(buf.String()), "\n")
	require.Len(t, lines, 2)
	assert.Contains(t, lines[0], `"msg":"ping"`)
	assert.Contains(t, lines[0], `"request_id"`)
	assert.Contains(t, lines[0], `"tenant_id":"`+id.String()+`"`)
	assert.Contains(t, lines[1], `"tenant_id":"`+id.String()+`"`)
}

func TestLogx_NoTenant_NoTenantField(t *testing.T) {
	var buf bytes.Buffer
	h := logx.RequestMiddleware(jsonLogger(&buf))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {}))
	h.ServeHTTP(httptest.NewRecorder(), httptest.NewRequest(http.MethodGet, "/x", nil))
	assert.NotContains(t, buf.String(), "tenant_id")
}

func TestLogx_FromContext_WithoutMiddleware_ReturnsDefault(t *testing.T) {
	assert.NotNil(t, logx.FromContext(httptest.NewRequest(http.MethodGet, "/", nil).Context()))
}
