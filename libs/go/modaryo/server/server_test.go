package server_test

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net"
	"net/http"
	"sync"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/server"
)

type running struct {
	base   string
	cancel context.CancelFunc
	done   chan error
}

func start(t *testing.T, h http.Handler, opts ...server.Option) *running {
	t.Helper()
	return startWith(t, h, slog.New(slog.DiscardHandler), opts...)
}

type syncBuf struct {
	mu sync.Mutex
	b  bytes.Buffer
}

func (s *syncBuf) Write(p []byte) (int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.b.Write(p)
}

func (s *syncBuf) String() string {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.b.String()
}

func startWith(t *testing.T, h http.Handler, l *slog.Logger, opts ...server.Option) *running {
	t.Helper()
	ctx, cancel := context.WithCancel(context.Background())
	addrCh := make(chan net.Addr, 1)
	done := make(chan error, 1)
	opts = append(opts, server.WithOnListen(func(a net.Addr) { addrCh <- a }))
	go func() { done <- server.Run(ctx, "127.0.0.1:0", h, l, opts...) }()
	select {
	case a := <-addrCh:
		return &running{base: "http://" + a.String(), cancel: cancel, done: done}
	case err := <-done:
		cancel()
		t.Fatalf("server exited early: %v", err)
	case <-time.After(5 * time.Second):
		cancel()
		t.Fatal("server did not listen")
	}
	return nil
}

func get(t *testing.T, url string) (int, map[string]any, string) {
	t.Helper()
	resp, err := http.Get(url)
	require.NoError(t, err)
	defer func() { _ = resp.Body.Close() }()
	b, _ := io.ReadAll(resp.Body)
	var m map[string]any
	_ = json.Unmarshal(b, &m)
	return resp.StatusCode, m, string(b)
}

func TestServer_HealthLive(t *testing.T) {
	r := start(t, http.NotFoundHandler())
	code, m, _ := get(t, r.base+"/health/live")
	assert.Equal(t, 200, code)
	assert.Equal(t, "ok", m["status"])
	r.cancel()
	require.NoError(t, <-r.done)
}

func TestServer_HealthReadyRunsChecks(t *testing.T) {
	called := make(chan struct{}, 1)
	r := start(t, http.NotFoundHandler(),
		server.WithReadyCheck("db", func(context.Context) error { called <- struct{}{}; return nil }),
	)
	code, m, _ := get(t, r.base+"/health/ready")
	assert.Equal(t, 200, code)
	assert.Equal(t, "ok", m["status"])
	assert.Len(t, called, 1)
	r.cancel()
	require.NoError(t, <-r.done)
}

func TestServer_HealthReady503ListsFailingNamesOnly(t *testing.T) {
	r := start(t, http.NotFoundHandler(),
		server.WithReadyCheck("db", func(context.Context) error { return errors.New("password=hunter2 refused") }),
		server.WithReadyCheck("valkey", func(context.Context) error { return nil }),
		server.WithReadyCheck("smtp", func(ctx context.Context) error { <-ctx.Done(); return ctx.Err() }),
	)
	began := time.Now()
	code, m, raw := get(t, r.base+"/health/ready")
	assert.Equal(t, 503, code)
	assert.Equal(t, "fail", m["status"])
	assert.ElementsMatch(t, []any{"db", "smtp"}, m["failed"])
	assert.NotContains(t, raw, "hunter2")
	assert.Less(t, time.Since(began), 4*time.Second)
	r.cancel()
	require.NoError(t, <-r.done)
}

func TestServer_DelegatesToHandler(t *testing.T) {
	r := start(t, http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) { w.WriteHeader(http.StatusTeapot) }))
	resp, err := http.Get(r.base + "/anything")
	require.NoError(t, err)
	_ = resp.Body.Close()
	assert.Equal(t, http.StatusTeapot, resp.StatusCode)
	r.cancel()
	require.NoError(t, <-r.done)
}

func TestServer_GracefulShutdownReturnsNil(t *testing.T) {
	r := start(t, http.NotFoundHandler())
	r.cancel()
	select {
	case err := <-r.done:
		require.NoError(t, err)
	case <-time.After(5 * time.Second):
		t.Fatal("shutdown timed out")
	}
}

func TestServer_HealthReady_LogsErrorTypeNotMessage(t *testing.T) {
	buf := &syncBuf{}
	r := startWith(t, http.NotFoundHandler(), slog.New(slog.NewJSONHandler(buf, nil)),
		server.WithReadyCheck("db", func(context.Context) error { return errors.New("password=hunter2 refused") }),
	)
	code, _, _ := get(t, r.base+"/health/ready")
	assert.Equal(t, 503, code)
	out := buf.String()
	assert.NotContains(t, out, "hunter2")
	assert.Contains(t, out, `"check":"db"`)
	assert.Contains(t, out, "errors.errorString")
	r.cancel()
	require.NoError(t, <-r.done)
}

func TestServer_HealthReady_PanickingCheckCountsAsFailure(t *testing.T) {
	r := start(t, http.NotFoundHandler(),
		server.WithReadyCheck("boom", func(context.Context) error { panic("secret-dsn") }),
		server.WithReadyCheck("ok", func(context.Context) error { return nil }),
	)
	code, m, raw := get(t, r.base+"/health/ready")
	assert.Equal(t, 503, code)
	assert.Equal(t, []any{"boom"}, m["failed"])
	assert.NotContains(t, raw, "secret-dsn")
	r.cancel()
	require.NoError(t, <-r.done)
}

func TestServer_HealthReady_CheckIgnoringContextIsBounded(t *testing.T) {
	release := make(chan struct{})
	defer close(release)
	r := start(t, http.NotFoundHandler(),
		server.WithReadyCheck("stuck", func(context.Context) error { <-release; return nil }),
	)
	began := time.Now()
	code, m, _ := get(t, r.base+"/health/ready")
	assert.Equal(t, 503, code)
	assert.Equal(t, []any{"stuck"}, m["failed"])
	assert.Less(t, time.Since(began), 4*time.Second)
	r.cancel()
	require.NoError(t, <-r.done)
}

func TestServer_WithReadyCheck_NilPanics(t *testing.T) {
	assert.Panics(t, func() { server.WithReadyCheck("x", nil) })
}

func TestServer_HeadHealthLive_200(t *testing.T) {
	r := start(t, http.NotFoundHandler())
	resp, err := http.Head(r.base + "/health/live")
	require.NoError(t, err)
	_ = resp.Body.Close()
	assert.Equal(t, 200, resp.StatusCode)
	r.cancel()
	require.NoError(t, <-r.done)
}
