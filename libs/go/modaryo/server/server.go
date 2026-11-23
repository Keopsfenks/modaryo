// Package server runs an http.Server with health endpoints and graceful shutdown.
package server

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"net"
	"net/http"
	"os/signal"
	"sort"
	"sync"
	"syscall"
	"time"
)

const (
	readyTimeout    = 2 * time.Second
	shutdownTimeout = 15 * time.Second
)

type readyCheck struct {
	name string
	fn   func(context.Context) error
}

type config struct {
	checks   []readyCheck
	onListen func(net.Addr)
}

// Option customises Run.
type Option func(*config)

// WithReadyCheck adds a named check run by /health/ready (concurrently, 2 s timeout overall).
func WithReadyCheck(name string, fn func(context.Context) error) Option {
	return func(c *config) { c.checks = append(c.checks, readyCheck{name, fn}) }
}

// WithOnListen is called with the bound address once the listener is open (useful with ":0").
func WithOnListen(fn func(net.Addr)) Option {
	return func(c *config) { c.onListen = fn }
}

// Run serves h on addr, with /health/live and /health/ready mounted in front of it, until ctx
// is cancelled or SIGINT/SIGTERM arrives; it then drains for up to 15 s. A clean shutdown
// returns nil.
func Run(ctx context.Context, addr string, h http.Handler, l *slog.Logger, opts ...Option) error {
	var cfg config
	for _, o := range opts {
		o(&cfg)
	}

	mux := http.NewServeMux()
	mux.HandleFunc("GET /health/live", func(w http.ResponseWriter, _ *http.Request) {
		writeJSON(w, http.StatusOK, map[string]any{"status": "ok"})
	})
	mux.HandleFunc("GET /health/ready", func(w http.ResponseWriter, r *http.Request) {
		failed := runChecks(r.Context(), l, cfg.checks)
		if len(failed) > 0 {
			writeJSON(w, http.StatusServiceUnavailable, map[string]any{"status": "fail", "failed": failed})
			return
		}
		writeJSON(w, http.StatusOK, map[string]any{"status": "ok"})
	})
	mux.Handle("/", h)

	srv := &http.Server{
		Handler:           mux,
		ReadHeaderTimeout: 10 * time.Second,
		IdleTimeout:       120 * time.Second,
	}

	ln, err := net.Listen("tcp", addr)
	if err != nil {
		return fmt.Errorf("server: %s dinlenemiyor: %w", addr, err)
	}
	if cfg.onListen != nil {
		cfg.onListen(ln.Addr())
	}

	sigCtx, stop := signal.NotifyContext(ctx, syscall.SIGINT, syscall.SIGTERM)
	defer stop()

	errCh := make(chan error, 1)
	go func() { errCh <- srv.Serve(ln) }()
	l.Info("server started", "addr", ln.Addr().String())

	select {
	case err := <-errCh:
		if errors.Is(err, http.ErrServerClosed) {
			return nil
		}
		return err
	case <-sigCtx.Done():
	}

	l.Info("server shutting down")
	shutCtx, cancel := context.WithTimeout(context.Background(), shutdownTimeout)
	defer cancel()
	if err := srv.Shutdown(shutCtx); err != nil {
		_ = srv.Close()
		return fmt.Errorf("server: kapatma: %w", err)
	}
	if err := <-errCh; err != nil && !errors.Is(err, http.ErrServerClosed) {
		return err
	}
	return nil
}

func runChecks(ctx context.Context, l *slog.Logger, checks []readyCheck) []string {
	ctx, cancel := context.WithTimeout(ctx, readyTimeout)
	defer cancel()

	var (
		mu     sync.Mutex
		wg     sync.WaitGroup
		failed = []string{}
	)
	for _, c := range checks {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if err := c.fn(ctx); err != nil {
				l.Warn("readiness check failed", "check", c.name, "error", err.Error())
				mu.Lock()
				failed = append(failed, c.name)
				mu.Unlock()
			}
		}()
	}
	wg.Wait()
	sort.Strings(failed)
	return failed
}

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}
