package session_test

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"
	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/session"
)

// Fixture kayıtları 1795200000 anında oluşturulmuştur.
var fixtureNow = time.Unix(1795200000, 0)

const minimalRecord = `{"v":1,"id":"x","user_id":"0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90","email":"a@b.c","full_name":"A","permissions":[],"created_at":1795200000,"absolute_expires_at":1795286400,"idle_timeout_s":3600}`

func testRedis(t *testing.T) *redis.Client {
	t.Helper()
	rdb := redis.NewClient(&redis.Options{Addr: "localhost:6379", DB: 4})
	if err := rdb.Ping(context.Background()).Err(); err != nil {
		t.Fatalf("Valkey erişilemiyor (infra/up.sh çalıştırın): %v", err)
	}
	t.Cleanup(func() { _ = rdb.Close() })
	return rdb
}

func newSid(t *testing.T) string {
	t.Helper()
	b := make([]byte, 16)
	_, err := rand.Read(b)
	require.NoError(t, err)
	return "gotest-" + hex.EncodeToString(b)
}

func seed(t *testing.T, rdb *redis.Client, sid, raw string) string {
	t.Helper()
	key := session.Key(sid)
	require.NoError(t, rdb.Set(context.Background(), key, raw, time.Hour).Err())
	t.Cleanup(func() { rdb.Del(context.Background(), key) })
	return key
}

func fixture(t *testing.T, name string) string {
	t.Helper()
	b, err := os.ReadFile(filepath.Join("..", "..", "..", "..", "contracts", "fixtures", name))
	require.NoError(t, err)
	return string(b)
}

func fixedClock(at time.Time) func() time.Time { return func() time.Time { return at } }

func TestReader_FixtureWithoutNewFields_Defaults(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	seed(t, rdb, sid, fixture(t, "session-v1-legacy.json"))

	rec, err := session.NewReader(rdb, fixedClock(fixtureNow)).Read(context.Background(), sid)
	require.NoError(t, err)

	assert.Equal(t, 1, rec.V)
	assert.Equal(t, "employee", rec.UserType)
	assert.Nil(t, rec.CariID)
	assert.NotNil(t, rec.Modules)
	assert.Empty(t, rec.Modules)
	assert.Nil(t, rec.BranchIDs)
	assert.True(t, rec.MaxDiscountPct.IsZero())
	assert.True(t, rec.RememberMe)
	assert.Equal(t, []string{"*"}, rec.Permissions)
	assert.True(t, rec.HasPermission("anything.at.all"))
	assert.True(t, rec.CanAccessBranch(uuid.New()), "null branch_ids = all")
}

func TestReader_FixtureWithNewFields(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	seed(t, rdb, sid, fixture(t, "session-v1-full.json"))

	rec, err := session.NewReader(rdb, fixedClock(fixtureNow)).Read(context.Background(), sid)
	require.NoError(t, err)

	assert.Equal(t, "dealer", rec.UserType)
	require.NotNil(t, rec.CariID)
	assert.Equal(t, uuid.MustParse("0193a6f4-1b53-7d22-9f3c-7ea05b4d6c32"), *rec.CariID)
	assert.Equal(t, []string{"pos", "sube"}, rec.Modules)
	require.NotNil(t, rec.BranchIDs)
	assert.Len(t, *rec.BranchIDs, 2)
	assert.True(t, rec.MaxDiscountPct.Equal(decimal.RequireFromString("7.5")))
	assert.True(t, rec.HasModule("pos"))
	assert.False(t, rec.HasModule("muhasebe"))
	assert.True(t, rec.HasPermission("cari.read"))
	assert.False(t, rec.HasPermission("cari.delete"))
	assert.True(t, rec.CanAccessBranch(uuid.MustParse("0193a6f5-2c64-7e33-8a4d-8fb16c5e7d43")))
	assert.False(t, rec.CanAccessBranch(uuid.New()))
	require.NotNil(t, rec.TenantID)
	require.NotNil(t, rec.IP)
	assert.Equal(t, "203.0.113.5", *rec.IP)
}

func TestRecord_EmptyBranchIDsMeansNone(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	seed(t, rdb, sid, `{"v":1,"id":"x","user_id":"0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90","email":"a@b.c","full_name":"A","permissions":null,"created_at":1795200000,"absolute_expires_at":1795286400,"idle_timeout_s":60,"modules":null,"branch_ids":[]}`)

	rec, err := session.NewReader(rdb, fixedClock(fixtureNow)).Read(context.Background(), sid)
	require.NoError(t, err)
	require.NotNil(t, rec.BranchIDs)
	assert.False(t, rec.CanAccessBranch(uuid.New()))
	assert.False(t, rec.HasPermission("x"), "null permissions = none")
	assert.False(t, rec.HasModule("x"), "null modules = none")
}

func TestReader_IdleExtensionCappedByAbsolute(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	// 10 sn mutlak ömür kaldı, boşta süresi 3600 sn.
	now := time.Unix(1795286390, 0)
	key := seed(t, rdb, sid, minimalRecord)

	_, err := session.NewReader(rdb, fixedClock(now)).Read(context.Background(), sid)
	require.NoError(t, err)

	ttl, err := rdb.TTL(context.Background(), key).Result()
	require.NoError(t, err)
	assert.LessOrEqual(t, ttl, 10*time.Second)
	assert.Greater(t, ttl, 0*time.Second)
}

func TestReader_IdleExtensionUsesIdleWhenSmaller(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	key := seed(t, rdb, sid, fixture(t, "session-v1-full.json")) // idle 28800, kalan 86400

	_, err := session.NewReader(rdb, fixedClock(fixtureNow)).Read(context.Background(), sid)
	require.NoError(t, err)

	ttl, err := rdb.TTL(context.Background(), key).Result()
	require.NoError(t, err)
	assert.InDelta(t, 28800, ttl.Seconds(), 5)
}

func TestReader_NeverModifiesValue(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	key := seed(t, rdb, sid, fixture(t, "session-v1-full.json"))
	before, err := rdb.Get(context.Background(), key).Result()
	require.NoError(t, err)

	_, err = session.NewReader(rdb, fixedClock(fixtureNow)).Read(context.Background(), sid)
	require.NoError(t, err)

	after, err := rdb.Get(context.Background(), key).Result()
	require.NoError(t, err)
	assert.Equal(t, before, after)
}

func TestReader_ExpiredAbsolute_ErrExpired(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	key := seed(t, rdb, sid, fixture(t, "session-v1-full.json"))
	require.NoError(t, rdb.Expire(context.Background(), key, 1000*time.Second).Err())

	_, err := session.NewReader(rdb, fixedClock(time.Unix(1795286400, 0))).Read(context.Background(), sid)
	assert.ErrorIs(t, err, session.ErrExpired)

	ttl, _ := rdb.TTL(context.Background(), key).Result()
	assert.Greater(t, ttl, 900*time.Second, "süresi dolan kayıt için EXPIRE çağrılmaz")
}

func TestReader_UnknownVersion_Error(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	seed(t, rdb, sid, `{"v":2,"id":"x"}`)

	_, err := session.NewReader(rdb, fixedClock(fixtureNow)).Read(context.Background(), sid)
	assert.ErrorIs(t, err, session.ErrUnsupportedVersion)
}

func TestReader_MissingAndMalformed_NotFound(t *testing.T) {
	rdb := testRedis(t)
	r := session.NewReader(rdb, fixedClock(fixtureNow))

	_, err := r.Read(context.Background(), newSid(t))
	assert.ErrorIs(t, err, session.ErrNotFound)

	_, err = r.Read(context.Background(), "")
	assert.ErrorIs(t, err, session.ErrNotFound)

	sid := newSid(t)
	seed(t, rdb, sid, `{not json`)
	_, err = r.Read(context.Background(), sid)
	assert.ErrorIs(t, err, session.ErrNotFound)
}

func TestKey_MatchesDotnetHash(t *testing.T) {
	// sha256("abc")
	assert.Equal(t, "sess:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", session.Key("abc"))
}

// --- Middleware ---

func okHandler(saw *bool) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		*saw = true
		w.WriteHeader(http.StatusOK)
	})
}

func do(h http.Handler, sid string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(http.MethodGet, "/", nil)
	if sid != "" {
		req.AddCookie(&http.Cookie{Name: session.CookieName, Value: sid})
	}
	rr := httptest.NewRecorder()
	h.ServeHTTP(rr, req)
	return rr
}

func TestMiddleware_NoCookie_401(t *testing.T) {
	rdb := testRedis(t)
	var saw bool
	h := session.Middleware(session.NewReader(rdb, fixedClock(fixtureNow)), session.Options{})(okHandler(&saw))

	rr := do(h, "")
	assert.Equal(t, http.StatusUnauthorized, rr.Code)
	assert.Contains(t, rr.Header().Get("Content-Type"), "application/problem+json")
	assert.JSONEq(t, `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Oturum açmanız gerekiyor.","status":401,"code":"unauthorized"}`, rr.Body.String())
	assert.False(t, saw)
}

func TestMiddleware_UnknownSession_401(t *testing.T) {
	rdb := testRedis(t)
	var saw bool
	h := session.Middleware(session.NewReader(rdb, fixedClock(fixtureNow)), session.Options{})(okHandler(&saw))
	assert.Equal(t, http.StatusUnauthorized, do(h, newSid(t)).Code)
	assert.False(t, saw)
}

func TestMiddleware_Optional_PassesThroughWithoutRecord(t *testing.T) {
	rdb := testRedis(t)
	var has bool
	h := session.Middleware(session.NewReader(rdb, fixedClock(fixtureNow)), session.Options{Optional: true})(
		http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			_, has = session.FromContext(r.Context())
		}))
	rr := do(h, "")
	assert.Equal(t, http.StatusOK, rr.Code)
	assert.False(t, has)
}

func TestMiddleware_Optional_InvalidCookiePassesAsAnonymous(t *testing.T) {
	rdb := testRedis(t)
	var has bool
	h := session.Middleware(session.NewReader(rdb, fixedClock(fixtureNow)), session.Options{Optional: true})(
		http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			_, has = session.FromContext(r.Context())
		}))
	assert.Equal(t, http.StatusOK, do(h, newSid(t)).Code, "unknown sid")
	assert.False(t, has)

	sid := newSid(t)
	key := seed(t, rdb, sid, fixture(t, "session-v1-full.json"))
	require.NoError(t, rdb.Expire(context.Background(), key, 1000*time.Second).Err())
	h = session.Middleware(session.NewReader(rdb, fixedClock(time.Unix(1795286400, 0))), session.Options{Optional: true})(
		http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			_, has = session.FromContext(r.Context())
		}))
	assert.Equal(t, http.StatusOK, do(h, sid).Code, "expired record")
	assert.False(t, has)
}

func TestMiddleware_Optional_ValkeyDown_Still503(t *testing.T) {
	rdb := redis.NewClient(&redis.Options{Addr: "127.0.0.1:1", DialTimeout: 200 * time.Millisecond, MaxRetries: -1})
	t.Cleanup(func() { _ = rdb.Close() })
	h := session.Middleware(session.NewReader(rdb, fixedClock(fixtureNow)), session.Options{Optional: true})(okHandler(new(bool)))
	assert.Equal(t, http.StatusServiceUnavailable, do(h, "abc").Code)
}

func TestMiddleware_DealerRejectedByDefault(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	seed(t, rdb, sid, fixture(t, "session-v1-full.json")) // dealer
	var saw bool
	h := session.Middleware(session.NewReader(rdb, fixedClock(fixtureNow)), session.Options{})(okHandler(&saw))

	assert.Equal(t, http.StatusUnauthorized, do(h, sid).Code)
	assert.False(t, saw)
}

func TestMiddleware_UnknownUserType_401(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	seed(t, rdb, sid, `{"v":1,"id":"x","user_id":"0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90","email":"a@b.c","full_name":"A","permissions":[],"created_at":1795200000,"absolute_expires_at":1795286400,"idle_timeout_s":60,"user_type":"alien"}`)
	h := session.Middleware(session.NewReader(rdb, fixedClock(fixtureNow)),
		session.Options{AllowedUserTypes: []string{"alien"}})(okHandler(new(bool)))
	assert.Equal(t, http.StatusUnauthorized, do(h, sid).Code, "bilinmeyen tür izinli listede olsa da reddedilir")
}

func TestMiddleware_AllowedDealer_200(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	seed(t, rdb, sid, fixture(t, "session-v1-full.json"))
	var got *session.Record
	h := session.Middleware(session.NewReader(rdb, fixedClock(fixtureNow)),
		session.Options{AllowedUserTypes: []string{"dealer"}})(
		http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			got, _ = session.FromContext(r.Context())
		}))

	assert.Equal(t, http.StatusOK, do(h, sid).Code)
	require.NotNil(t, got)
	assert.Equal(t, "dealer", got.UserType)
}

func TestMiddleware_EmployeeAllowedByDefault(t *testing.T) {
	rdb := testRedis(t)
	sid := newSid(t)
	seed(t, rdb, sid, fixture(t, "session-v1-legacy.json"))
	var saw bool
	h := session.Middleware(session.NewReader(rdb, fixedClock(fixtureNow)), session.Options{})(okHandler(&saw))
	assert.Equal(t, http.StatusOK, do(h, sid).Code)
	assert.True(t, saw)
}

func TestMiddleware_ValkeyDown_503(t *testing.T) {
	rdb := redis.NewClient(&redis.Options{Addr: "127.0.0.1:1", DialTimeout: 200 * time.Millisecond, MaxRetries: -1})
	t.Cleanup(func() { _ = rdb.Close() })
	h := session.Middleware(session.NewReader(rdb, fixedClock(fixtureNow)), session.Options{})(okHandler(new(bool)))
	assert.Equal(t, http.StatusServiceUnavailable, do(h, "abc").Code)
}

// --- RequirePermission / RequireModule ---

func withRecord(rec *session.Record, mw func(http.Handler) http.Handler) *httptest.ResponseRecorder {
	h := mw(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.WriteHeader(http.StatusOK) }))
	req := httptest.NewRequest(http.MethodGet, "/", nil)
	if rec != nil {
		req = req.WithContext(session.WithRecord(req.Context(), rec))
	}
	rr := httptest.NewRecorder()
	h.ServeHTTP(rr, req)
	return rr
}

func TestRequirePermission_Star(t *testing.T) {
	assert.Equal(t, 200, withRecord(&session.Record{Permissions: []string{"*"}}, session.RequirePermission("x.y")).Code)
	assert.Equal(t, 200, withRecord(&session.Record{Permissions: []string{"x.y"}}, session.RequirePermission("x.y")).Code)

	rr := withRecord(&session.Record{Permissions: []string{"a.b"}}, session.RequirePermission("x.y"))
	assert.Equal(t, 403, rr.Code)
	assert.Contains(t, rr.Body.String(), `"code":"forbidden"`)

	assert.Equal(t, 401, withRecord(nil, session.RequirePermission("x.y")).Code)
}

func TestRequireModule_Disabled403Code(t *testing.T) {
	rr := withRecord(&session.Record{Modules: []string{"pos"}}, session.RequireModule("muhasebe"))
	assert.Equal(t, 403, rr.Code)
	assert.Contains(t, rr.Body.String(), `"code":"module_disabled"`)
	assert.Contains(t, rr.Body.String(), "Bu modül firmanızda kapalı.")

	assert.Equal(t, 200, withRecord(&session.Record{Modules: []string{"pos"}}, session.RequireModule("pos")).Code)
	assert.Equal(t, 401, withRecord(nil, session.RequireModule("pos")).Code)
}
