package testinfra_test

import (
	"context"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/session"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/testinfra"
)

func dbExists(t *testing.T, admin *pgxpool.Pool, name string) bool {
	t.Helper()
	var exists bool
	require.NoError(t, admin.QueryRow(context.Background(),
		"SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = $1)", name).Scan(&exists))
	return exists
}

func TestTempDatabase_CreatedAndDropped(t *testing.T) {
	admin, err := pgxpool.New(context.Background(), testinfra.AdminDSN())
	require.NoError(t, err)
	defer admin.Close()

	var name string
	t.Run("inner", func(t *testing.T) {
		pool, dsn := testinfra.TempDatabase(t)
		require.NoError(t, pool.Ping(context.Background()))
		require.NoError(t, pool.QueryRow(context.Background(), "SELECT current_database()").Scan(&name))
		assert.Contains(t, name, "modaryo_go_test_")
		assert.Contains(t, dsn, name)
		assert.True(t, dbExists(t, admin, name), "database must exist during the test")
	})
	require.NotEmpty(t, name)
	assert.False(t, dbExists(t, admin, name), "database must be dropped after cleanup")
}

func TestNewTempDatabase_ReturnsCleanup(t *testing.T) {
	admin, err := pgxpool.New(context.Background(), testinfra.AdminDSN())
	require.NoError(t, err)
	defer admin.Close()

	pool, _, cleanup, err := testinfra.NewTempDatabase(context.Background())
	require.NoError(t, err)
	var name string
	require.NoError(t, pool.QueryRow(context.Background(), "SELECT current_database()").Scan(&name))
	assert.True(t, dbExists(t, admin, name))
	cleanup()
	assert.False(t, dbExists(t, admin, name))
}

func TestWriteSession_ReadableBySessionReader(t *testing.T) {
	rdb := testinfra.Valkey(t, 5)
	tenant := uuid.New()
	branch := uuid.New()
	cari := uuid.New()
	ip, ua := "10.0.0.1", "go-test"
	branches := []uuid.UUID{branch}
	now := time.Now()
	rec := session.Record{
		V: 1, ID: uuid.NewString(), UserID: uuid.New(), Email: "a@b.com", FullName: "Ayşe Yılmaz",
		TenantID: &tenant, Permissions: []string{"*"}, RememberMe: true,
		CreatedAt: now.Unix(), AbsoluteExpiresAt: now.Add(time.Hour).Unix(), IdleTimeoutS: 1800,
		IP: &ip, UserAgent: &ua,
		UserType: session.UserTypeDealer, CariID: &cari, Modules: []string{"sales"},
		BranchIDs: &branches, MaxDiscountPct: decimal.RequireFromString("12.5"),
	}

	sid := testinfra.WriteSession(t, rdb, rec, time.Minute)
	require.NotEmpty(t, sid)

	got, err := session.NewReader(rdb, nil).Read(context.Background(), sid)
	require.NoError(t, err)
	assert.Equal(t, rec.UserID, got.UserID)
	assert.Equal(t, rec.Email, got.Email)
	assert.Equal(t, rec.FullName, got.FullName)
	assert.Equal(t, rec.TenantID, got.TenantID)
	assert.Equal(t, rec.Permissions, got.Permissions)
	assert.Equal(t, rec.IP, got.IP)
	assert.Equal(t, rec.UserType, got.UserType)
	assert.Equal(t, rec.CariID, got.CariID)
	assert.Equal(t, rec.Modules, got.Modules)
	assert.Equal(t, rec.BranchIDs, got.BranchIDs)
	assert.True(t, rec.MaxDiscountPct.Equal(got.MaxDiscountPct))
	assert.Equal(t, rec.AbsoluteExpiresAt, got.AbsoluteExpiresAt)
}

func TestWriteSession_DeletedOnCleanup(t *testing.T) {
	rdb := testinfra.Valkey(t, 5)
	var sid string
	t.Run("inner", func(t *testing.T) {
		sid = testinfra.WriteSession(t, rdb, session.Record{V: 1, AbsoluteExpiresAt: time.Now().Add(time.Hour).Unix(), IdleTimeoutS: 60}, time.Minute)
		n, err := rdb.Exists(context.Background(), session.Key(sid)).Result()
		require.NoError(t, err)
		assert.EqualValues(t, 1, n)
	})
	n, err := rdb.Exists(context.Background(), session.Key(sid)).Result()
	require.NoError(t, err)
	assert.EqualValues(t, 0, n)
}

func TestFlushDB_ClearsOwnedDatabase(t *testing.T) {
	rdb := testinfra.Valkey(t, 5)
	require.NoError(t, rdb.Set(context.Background(), "k", "v", 0).Err())
	testinfra.FlushDB(t, rdb)
	n, err := rdb.DBSize(context.Background()).Result()
	require.NoError(t, err)
	assert.EqualValues(t, 0, n)
}
