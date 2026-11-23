package internalapi

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

type vector struct {
	Secret         string `json:"secret"`
	Method         string `json:"method"`
	PathAndQuery   string `json:"pathAndQuery"`
	Timestamp      int64  `json:"timestamp"`
	Tenant         string `json:"tenant"`
	IdempotencyKey string `json:"idempotencyKey"`
	Body           string `json:"body"`
	BodySha256Hex  string `json:"bodySha256Hex"`
	CanonicalStr   string `json:"canonicalString"`
	SignatureHex   string `json:"signatureHex"`
}

// repoRoot walks up from the test directory to the folder that holds go.work.
func repoRoot(t testing.TB) string {
	t.Helper()
	dir, err := os.Getwd()
	require.NoError(t, err)
	for {
		if _, err := os.Stat(filepath.Join(dir, "go.work")); err == nil {
			return dir
		}
		parent := filepath.Dir(dir)
		require.NotEqual(t, dir, parent, "go.work not found")
		dir = parent
	}
}

func loadVector(t testing.TB) vector {
	t.Helper()
	raw, err := os.ReadFile(filepath.Join(repoRoot(t), "contracts", "fixtures", "internal-signature-v1.json"))
	require.NoError(t, err)
	var v vector
	require.NoError(t, json.Unmarshal(raw, &v))
	return v
}

func TestSignature_SharedVector(t *testing.T) {
	v := loadVector(t)
	body := []byte(v.Body)

	assert.Equal(t, v.CanonicalStr, CanonicalString(v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, v.IdempotencyKey, body))
	assert.Equal(t, v.SignatureHex, Sign([]byte(v.Secret), v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, v.IdempotencyKey, body))
	assert.True(t, Verify([]byte(v.Secret), v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, v.IdempotencyKey, body, v.SignatureHex))
}

func TestVerify_RejectsTamperingAndBadHex(t *testing.T) {
	v := loadVector(t)
	s, body := []byte(v.Secret), []byte(v.Body)
	ok := func(method, pq string, ts int64, tenant, key string, b []byte, sig string) bool {
		return Verify(s, method, pq, ts, tenant, key, b, sig)
	}
	assert.False(t, ok(v.Method, v.PathAndQuery+"&y=2", v.Timestamp, v.Tenant, v.IdempotencyKey, body, v.SignatureHex))
	assert.False(t, ok(v.Method, v.PathAndQuery, v.Timestamp+1, v.Tenant, v.IdempotencyKey, body, v.SignatureHex))
	assert.False(t, ok(v.Method, v.PathAndQuery, v.Timestamp, "", v.IdempotencyKey, body, v.SignatureHex))
	assert.False(t, ok(v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, "other", body, v.SignatureHex))
	assert.False(t, ok(v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, v.IdempotencyKey, []byte("x"), v.SignatureHex))
	assert.False(t, ok(v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, v.IdempotencyKey, body, ""))
	assert.False(t, ok(v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, v.IdempotencyKey, body, "zz"+v.SignatureHex[2:]))
	assert.False(t, ok(v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, v.IdempotencyKey, body, v.SignatureHex[:62]))
}
