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

func TestLogx_PanickingHandler_StillLogsAndRepanics(t *testing.T) {
	var buf bytes.Buffer
	h := logx.RequestMiddleware(jsonLogger(&buf))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		panic("boom")
	}))
	assert.PanicsWithValue(t, "boom", func() {
		h.ServeHTTP(httptest.NewRecorder(), httptest.NewRequest(http.MethodGet, "/x", nil))
	})
	var rec map[string]any
	require.NoError(t, json.Unmarshal(buf.Bytes(), &rec))
	assert.EqualValues(t, 500, rec["status"])
	assert.Equal(t, "/x", rec["path"])
}

func TestLogx_PanicAfterHeader_KeepsWrittenStatus(t *testing.T) {
	var buf bytes.Buffer
	h := logx.RequestMiddleware(jsonLogger(&buf))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusAccepted)
		panic(http.ErrAbortHandler)
	}))
	assert.PanicsWithValue(t, http.ErrAbortHandler, func() {
		h.ServeHTTP(httptest.NewRecorder(), httptest.NewRequest(http.MethodGet, "/x", nil))
	})
	assert.Contains(t, buf.String(), `"status":202`)
}

func TestLogx_FlusherWorksThroughMiddleware(t *testing.T) {
	flushed := false
	h := logx.RequestMiddleware(jsonLogger(&bytes.Buffer{}))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		f, ok := w.(http.Flusher)
		require.True(t, ok)
		f.Flush()
		flushed = true
	}))
	rr := httptest.NewRecorder()
	h.ServeHTTP(rr, httptest.NewRequest(http.MethodGet, "/x", nil))
	assert.True(t, flushed)
	assert.True(t, rr.Flushed)
}

func TestLogx_HijackUnsupported_ReturnsError(t *testing.T) {
	h := logx.RequestMiddleware(jsonLogger(&bytes.Buffer{}))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		hj, ok := w.(http.Hijacker)
		require.True(t, ok)
		_, _, err := hj.Hijack()
		assert.ErrorIs(t, err, http.ErrNotSupported)
	}))
	h.ServeHTTP(httptest.NewRecorder(), httptest.NewRequest(http.MethodGet, "/x", nil))
}

func TestLogx_InformationalStatusNotRecordedAsFinal(t *testing.T) {
	var buf bytes.Buffer
	h := logx.RequestMiddleware(jsonLogger(&buf))(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusEarlyHints)
		w.WriteHeader(http.StatusCreated)
	}))
	h.ServeHTTP(httptest.NewRecorder(), httptest.NewRequest(http.MethodGet, "/x", nil))
	assert.Contains(t, buf.String(), `"status":201`)
}

func TestLogx_NewWithWriter_DevTextProdJSON(t *testing.T) {
	var dev, prod bytes.Buffer
	logx.NewWithWriter("einvoice", true, &dev).Info("hello")
	logx.NewWithWriter("einvoice", false, &prod).Info("hello")
	assert.Contains(t, dev.String(), "service=einvoice")
	assert.False(t, json.Valid(dev.Bytes()))
	var rec map[string]any
	require.NoError(t, json.Unmarshal(prod.Bytes(), &rec))
	assert.Equal(t, "einvoice", rec["service"])
}

func logLines(t *testing.T, buf *bytes.Buffer) []map[string]any {
	t.Helper()
	var out []map[string]any
	for _, line := range strings.Split(strings.TrimSpace(buf.String()), "\n") {
		if line == "" {
			continue
		}
		var m map[string]any
		require.NoError(t, json.Unmarshal([]byte(line), &m), line)
		out = append(out, m)
	}
	return out
}

func TestRecover_PanicBecomes500Problem(t *testing.T) {
	h := logx.Recover(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { panic("boom") }))
	rr := httptest.NewRecorder()
	assert.NotPanics(t, func() { h.ServeHTTP(rr, httptest.NewRequest(http.MethodPost, "/x", nil)) })

	assert.Equal(t, http.StatusInternalServerError, rr.Code)
	assert.Equal(t, "application/problem+json", rr.Header().Get("Content-Type"))
	assert.JSONEq(t, `{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.1","title":"Beklenmeyen bir hata oluştu.","status":500,"code":"internal_error"}`, rr.Body.String())
}

func TestRecover_WithRequestMiddleware_Access500AndPanicLoggedOnce(t *testing.T) {
	var buf bytes.Buffer
	h := logx.RequestMiddleware(jsonLogger(&buf))(logx.Recover(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		panic("boom")
	})))
	rr := httptest.NewRecorder()
	assert.NotPanics(t, func() { h.ServeHTTP(rr, httptest.NewRequest(http.MethodGet, "/x", nil)) })
	assert.Equal(t, http.StatusInternalServerError, rr.Code)

	lines := logLines(t, &buf)
	require.Len(t, lines, 2, buf.String())
	panics := 0
	for _, l := range lines {
		if l["msg"] == "panic recovered" {
			panics++
			assert.Equal(t, "ERROR", l["level"])
			assert.Equal(t, "boom", l["panic"])
			assert.NotEmpty(t, l["stack"])
			assert.Equal(t, rr.Header().Get("X-Request-Id"), l["request_id"])
		}
	}
	assert.Equal(t, 1, panics)
	access := lines[len(lines)-1]
	assert.Equal(t, "request", access["msg"])
	assert.EqualValues(t, 500, access["status"])
}

func TestRecover_AbortHandlerIsRepanickedWithoutLogging(t *testing.T) {
	var buf bytes.Buffer
	h := logx.RequestMiddleware(jsonLogger(&buf))(logx.Recover(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		panic(http.ErrAbortHandler)
	})))
	assert.PanicsWithValue(t, http.ErrAbortHandler, func() {
		h.ServeHTTP(httptest.NewRecorder(), httptest.NewRequest(http.MethodGet, "/x", nil))
	})
	lines := logLines(t, &buf)
	require.Len(t, lines, 1, buf.String())
	assert.Equal(t, "request", lines[0]["msg"])
}

func TestRecover_PanicAfterResponseStarted_LogsAndAborts(t *testing.T) {
	var buf bytes.Buffer
	h := logx.RequestMiddleware(jsonLogger(&buf))(logx.Recover(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte("partial"))
		panic("late boom")
	})))
	rr := httptest.NewRecorder()
	assert.PanicsWithValue(t, http.ErrAbortHandler, func() {
		h.ServeHTTP(rr, httptest.NewRequest(http.MethodGet, "/x", nil))
	})
	assert.Equal(t, "partial", rr.Body.String(), "no problem body appended to a started response")
	lines := logLines(t, &buf)
	require.Len(t, lines, 2, buf.String())
	assert.Equal(t, "panic recovered", lines[0]["msg"])
	assert.Equal(t, "late boom", lines[0]["panic"])
	assert.EqualValues(t, 200, lines[1]["status"])
}
