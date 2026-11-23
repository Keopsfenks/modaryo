// Package internalapi implements the signed service-to-service API (internal-api v1): the HMAC
// signature, a signing HTTP client, a verifying middleware and idempotency storage. It is
// byte- and behaviour-compatible with libs/dotnet/Modaryo.Internal; see contracts/README.md.
package internalapi

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"strconv"
)

// Header names of the internal API contract.
const (
	HeaderService        = "X-Modaryo-Service"
	HeaderTimestamp      = "X-Modaryo-Timestamp"
	HeaderSignature      = "X-Modaryo-Signature"
	HeaderTenant         = "X-Modaryo-Tenant"
	HeaderIdempotencyKey = "Idempotency-Key"
)

// CanonicalString is "{method}\n{pathAndQuery}\n{timestamp}\n{tenant}\n{idempotencyKey}\n{sha256hex(body)}".
// tenant and idempotencyKey are empty when the header is absent.
func CanonicalString(method, pathAndQuery string, ts int64, tenant, idempotencyKey string, body []byte) string {
	sum := sha256.Sum256(body)
	return method + "\n" + pathAndQuery + "\n" + strconv.FormatInt(ts, 10) + "\n" + tenant + "\n" +
		idempotencyKey + "\n" + hex.EncodeToString(sum[:])
}

// Sign returns the lowercase hex HMAC-SHA256 of the canonical string. The secret is used as raw
// bytes. pathAndQuery must be exactly what is sent on the wire (no normalisation).
func Sign(secret []byte, method, pathAndQuery string, ts int64, tenant, idempotencyKey string, body []byte) string {
	return hex.EncodeToString(compute(secret, method, pathAndQuery, ts, tenant, idempotencyKey, body))
}

// Verify compares the signature in constant time; malformed hex yields false.
func Verify(secret []byte, method, pathAndQuery string, ts int64, tenant, idempotencyKey string, body []byte, sigHex string) bool {
	if len(sigHex) != 2*sha256.Size {
		return false
	}
	actual, err := hex.DecodeString(sigHex)
	if err != nil {
		return false
	}
	return hmac.Equal(compute(secret, method, pathAndQuery, ts, tenant, idempotencyKey, body), actual)
}

func compute(secret []byte, method, pathAndQuery string, ts int64, tenant, idempotencyKey string, body []byte) []byte {
	mac := hmac.New(sha256.New, secret)
	mac.Write([]byte(CanonicalString(method, pathAndQuery, ts, tenant, idempotencyKey, body)))
	return mac.Sum(nil)
}
