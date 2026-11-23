package secrets

import (
	"bytes"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"log/slog"
	"strings"
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

var (
	tenantA = uuid.MustParse("11111111-1111-1111-1111-111111111111")
	tenantB = uuid.MustParse("22222222-2222-2222-2222-222222222222")
)

func key(id string, fill byte) Key {
	return Key{ID: id, Secret: bytes.Repeat([]byte{fill}, 32)}
}

func newBox(t *testing.T, keys ...Key) *Box {
	t.Helper()
	b, err := NewBox(keys)
	require.NoError(t, err)
	return b
}

func TestSecrets_RoundTrip(t *testing.T) {
	b := newBox(t, key("k1", 1))
	sealed, err := b.Seal(tenantA, "sms", []byte("api-key-123"))
	require.NoError(t, err)
	assert.True(t, strings.HasPrefix(sealed, "v1:k1:"))
	got, err := b.Open(tenantA, "sms", sealed)
	require.NoError(t, err)
	assert.Equal(t, []byte("api-key-123"), got)

	empty, err := b.Seal(tenantA, "sms", nil)
	require.NoError(t, err)
	got, err = b.Open(tenantA, "sms", empty)
	require.NoError(t, err)
	assert.Empty(t, got)
}

func TestSecrets_AssociatedDataBindsTenant(t *testing.T) {
	b := newBox(t, key("k1", 1))
	sealed, err := b.Seal(tenantA, "sms", []byte("secret"))
	require.NoError(t, err)
	_, err = b.Open(tenantB, "sms", sealed)
	assert.ErrorIs(t, err, ErrAuth)
}

func TestSecrets_PurposeBinding(t *testing.T) {
	b := newBox(t, key("k1", 1))
	sealed, err := b.Seal(tenantA, "sms", []byte("secret"))
	require.NoError(t, err)
	_, err = b.Open(tenantA, "einvoice", sealed)
	assert.ErrorIs(t, err, ErrAuth)
}

func TestSecrets_RotationDecryptsOldKey(t *testing.T) {
	oldBox := newBox(t, key("old", 1))
	oldSealed, err := oldBox.Seal(tenantA, "p", []byte("v"))
	require.NoError(t, err)

	rotated := newBox(t, key("new", 2), key("old", 1))
	got, err := rotated.Open(tenantA, "p", oldSealed)
	require.NoError(t, err)
	assert.Equal(t, []byte("v"), got)

	newSealed, err := rotated.Seal(tenantA, "p", []byte("v2"))
	require.NoError(t, err)
	assert.True(t, strings.HasPrefix(newSealed, "v1:new:"))

	// Reverse direction: a box whose only key is the old one cannot open new.
	_, err = oldBox.Open(tenantA, "p", newSealed)
	assert.ErrorIs(t, err, ErrUnknownKey)

	// Old key in the second position still opens values sealed by it, and the
	// first key is always used for sealing.
	reversed := newBox(t, key("old", 1), key("new", 2))
	got, err = reversed.Open(tenantA, "p", newSealed)
	require.NoError(t, err)
	assert.Equal(t, []byte("v2"), got)
	s, err := reversed.Seal(tenantA, "p", []byte("x"))
	require.NoError(t, err)
	assert.True(t, strings.HasPrefix(s, "v1:old:"))
}

func TestSecrets_TamperedCiphertextFails(t *testing.T) {
	b := newBox(t, key("k1", 1))
	sealed, err := b.Seal(tenantA, "p", []byte("secret-value"))
	require.NoError(t, err)
	parts := strings.SplitN(sealed, ":", 3)
	raw, err := base64.StdEncoding.DecodeString(parts[2])
	require.NoError(t, err)
	raw[len(raw)-1] ^= 0x01
	tampered := parts[0] + ":" + parts[1] + ":" + base64.StdEncoding.EncodeToString(raw)
	_, err = b.Open(tenantA, "p", tampered)
	assert.ErrorIs(t, err, ErrAuth)
}

func TestSecrets_BadKeyLength_Error(t *testing.T) {
	_, err := NewBox([]Key{{ID: "short", Secret: []byte("tooshort")}})
	require.Error(t, err)
	assert.Contains(t, err.Error(), "short")

	spec := "k1:" + base64.StdEncoding.EncodeToString([]byte("0123456789"))
	_, err = NewBoxFromString(spec)
	require.Error(t, err)
	assert.Contains(t, err.Error(), "k1")
	assert.NotContains(t, err.Error(), spec[3:])
}

func TestSecrets_SealsDiffer(t *testing.T) {
	b := newBox(t, key("k1", 1))
	a, err := b.Seal(tenantA, "p", []byte("same"))
	require.NoError(t, err)
	c, err := b.Seal(tenantA, "p", []byte("same"))
	require.NoError(t, err)
	assert.NotEqual(t, a, c)
}

func TestSecrets_InvalidPurposeAndTenant(t *testing.T) {
	b := newBox(t, key("k1", 1))
	_, err := b.Seal(tenantA, "a|b", []byte("x"))
	assert.ErrorIs(t, err, ErrInvalidInput)
	_, err = b.Seal(tenantA, "", []byte("x"))
	assert.ErrorIs(t, err, ErrInvalidInput)
	_, err = b.Seal(uuid.Nil, "p", []byte("x"))
	assert.ErrorIs(t, err, ErrInvalidInput)
	sealed, err := b.Seal(tenantA, "p", []byte("x"))
	require.NoError(t, err)
	_, err = b.Open(uuid.Nil, "p", sealed)
	assert.ErrorIs(t, err, ErrInvalidInput)
	_, err = b.Open(tenantA, "a|b", sealed)
	assert.ErrorIs(t, err, ErrInvalidInput)
}

func TestSecrets_MalformedInputs(t *testing.T) {
	b := newBox(t, key("k1", 1))
	good, err := b.Seal(tenantA, "p", []byte("x"))
	require.NoError(t, err)
	body := strings.SplitN(good, ":", 3)[2]
	short := base64.StdEncoding.EncodeToString(make([]byte, 20))

	for name, in := range map[string]string{
		"empty":      "",
		"no parts":   "v1",
		"wrong ver":  "v2:k1:" + body,
		"bad base64": "v1:k1:!!!!",
		"too short":  "v1:k1:" + short,
	} {
		_, err := b.Open(tenantA, "p", in)
		assert.ErrorIs(t, err, ErrMalformed, name)
	}
	_, err = b.Open(tenantA, "p", "v1:nokey:"+body)
	assert.ErrorIs(t, err, ErrUnknownKey)
}

func TestSecrets_NewBoxFromString(t *testing.T) {
	k1 := base64.StdEncoding.EncodeToString(bytes.Repeat([]byte{1}, 32))
	k2 := base64.StdEncoding.EncodeToString(bytes.Repeat([]byte{2}, 32))
	b, err := NewBoxFromString("  k1:" + k1 + " , k-2_x:" + k2 + "  ")
	require.NoError(t, err)
	s, err := b.Seal(tenantA, "p", []byte("x"))
	require.NoError(t, err)
	assert.True(t, strings.HasPrefix(s, "v1:k1:"))

	for name, spec := range map[string]string{
		"empty":       "",
		"blank":       "   ",
		"empty entry": "k1:" + k1 + ",",
		"duplicate":   "k1:" + k1 + ",k1:" + k2,
		"bad id":      "bad id:" + k1,
		"long id":     strings.Repeat("a", 33) + ":" + k1,
		"no colon":    k1,
		"bad base64":  "k1:***",
		"unpadded":    "k1:" + strings.TrimRight(k1, "="),
	} {
		_, err := NewBoxFromString(spec)
		require.Error(t, err, name)
		assert.NotContains(t, err.Error(), k1, name)
	}
}

func TestSecrets_NewBoxFromEnv(t *testing.T) {
	k1 := base64.StdEncoding.EncodeToString(bytes.Repeat([]byte{1}, 32))
	t.Setenv(EnvKeys, "k1:"+k1)
	b, err := NewBoxFromEnv()
	require.NoError(t, err)
	s, err := b.Seal(tenantA, "p", []byte("x"))
	require.NoError(t, err)
	got, err := b.Open(tenantA, "p", s)
	require.NoError(t, err)
	assert.Equal(t, []byte("x"), got)

	t.Setenv(EnvKeys, "")
	_, err = NewBoxFromEnv()
	assert.Error(t, err)
}

func TestSecrets_Redaction(t *testing.T) {
	secret := bytes.Repeat([]byte{0xAB}, 32)
	k := Key{ID: "k1", Secret: secret}
	b := newBox(t, k)
	marker := fmt.Sprintf("%v", secret)
	for _, v := range []any{k, &k, b, *b} {
		for _, f := range []string{"%v", "%+v", "%#v", "%s", "%x", "%d"} {
			out := fmt.Sprintf(f, v)
			assert.NotContains(t, out, marker, f)
			assert.NotContains(t, out, "171", f)
			assert.NotContains(t, strings.ToLower(out), "abababab", f)
		}
	}
}

func TestSecrets_KeyJSONAndSlogAreRedacted(t *testing.T) {
	secret := bytes.Repeat([]byte{0xAB}, 32)
	k := Key{ID: "k1", Secret: secret}
	b64 := base64.StdEncoding.EncodeToString(secret)
	leaks := func(t *testing.T, out string) {
		t.Helper()
		assert.NotContains(t, out, b64)
		assert.NotContains(t, out, b64[:16])
		assert.NotContains(t, out, "171")
		assert.NotContains(t, strings.ToLower(out), "abababab")
		assert.Contains(t, out, redacted)
	}

	for name, v := range map[string]any{
		"value":   k,
		"pointer": &k,
		"nested":  struct{ K Key }{k},
		"slice":   []Key{k},
	} {
		t.Run("json_"+name, func(t *testing.T) {
			out, err := json.Marshal(v)
			require.NoError(t, err)
			leaks(t, string(out))
		})
	}

	t.Run("slog_json", func(t *testing.T) {
		var buf bytes.Buffer
		l := slog.New(slog.NewJSONHandler(&buf, nil))
		l.Info("key", "key", k, "ptr", &k)
		l.Info("any", slog.Any("key", k))
		leaks(t, buf.String())
	})
	t.Run("slog_text", func(t *testing.T) {
		var buf bytes.Buffer
		slog.New(slog.NewTextHandler(&buf, nil)).Info("key", "key", k)
		leaks(t, buf.String())
	})
}
