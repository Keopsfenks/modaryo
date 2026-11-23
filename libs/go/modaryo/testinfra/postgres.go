// Package testinfra holds helpers for tests that run against the real dev infrastructure
// (Postgres and Valkey in WSL Docker, started with infra/up.sh).
//
// Valkey DB index allocation (go test runs packages in parallel, so indexes must not collide):
//
//	1-3  .NET suites (libs 1, identity 2, erp 3)
//	4    Go shared-library tests (session, streams) - unique keys, never flushed
//	5    testinfra's own tests
//	6+   future services pick their own and own it exclusively
//
// Connection settings come from MODARYO_TEST_PG_DSN (admin DSN to the "postgres" database) and
// MODARYO_TEST_VALKEY_ADDR; both default to the local dev infrastructure.
package testinfra

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"fmt"
	"net/url"
	"os"
	"testing"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"
)

const defaultAdminDSN = "postgres://modaryo:modaryo_dev@localhost:5432/postgres?sslmode=disable"

// AdminDSN returns the admin DSN (database "postgres") from MODARYO_TEST_PG_DSN or the dev default.
func AdminDSN() string {
	if v := os.Getenv("MODARYO_TEST_PG_DSN"); v != "" {
		return v
	}
	return defaultAdminDSN
}

func dsnForDatabase(adminDSN, name string) (string, error) {
	u, err := url.Parse(adminDSN)
	if err != nil {
		return "", fmt.Errorf("testinfra: bad admin dsn: %w", err)
	}
	u.Path = "/" + name
	return u.String(), nil
}

// NewTempDatabase creates a temporary database "modaryo_go_test_<random>" and returns a pool,
// its DSN and a cleanup func (closes the pool, drops the database WITH FORCE). It does not need
// testing.TB, so it can be used from TestMain. Cleanup is safe to call once.
func NewTempDatabase(ctx context.Context) (*pgxpool.Pool, string, func(), error) {
	admin, err := pgxpool.New(ctx, AdminDSN())
	if err != nil {
		return nil, "", nil, fmt.Errorf("testinfra: admin pool: %w", err)
	}
	if err := admin.Ping(ctx); err != nil {
		admin.Close()
		return nil, "", nil, fmt.Errorf("testinfra: postgres must be running (infra/up.sh): %w", err)
	}

	suffix := make([]byte, 6)
	_, _ = rand.Read(suffix)
	name := "modaryo_go_test_" + hex.EncodeToString(suffix)
	if _, err := admin.Exec(ctx, "CREATE DATABASE "+name); err != nil {
		admin.Close()
		return nil, "", nil, fmt.Errorf("testinfra: create database: %w", err)
	}

	drop := func() {
		dctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
		defer cancel()
		_, _ = admin.Exec(dctx, "DROP DATABASE IF EXISTS "+name+" WITH (FORCE)")
		admin.Close()
	}

	dsn, err := dsnForDatabase(AdminDSN(), name)
	if err != nil {
		drop()
		return nil, "", nil, err
	}
	pool, err := pgxpool.New(ctx, dsn)
	if err != nil {
		drop()
		return nil, "", nil, fmt.Errorf("testinfra: pool: %w", err)
	}
	cleanup := func() {
		pool.Close()
		drop()
	}
	return pool, dsn, cleanup, nil
}

// TempDatabase creates a temporary database for the test and drops it on cleanup.
func TempDatabase(tb testing.TB) (*pgxpool.Pool, string) {
	tb.Helper()
	pool, dsn, cleanup, err := NewTempDatabase(context.Background())
	if err != nil {
		tb.Fatal(err)
	}
	tb.Cleanup(cleanup)
	return pool, dsn
}
