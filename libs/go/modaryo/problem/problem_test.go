package problem_test

import (
	"bytes"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/problem"
)

func TestWrite_ProblemJSONShape(t *testing.T) {
	rr := httptest.NewRecorder()
	problem.Write(rr, problem.App(http.StatusForbidden, "module_disabled", "Bu modül firmanızda kapalı."))

	assert.Equal(t, http.StatusForbidden, rr.Code)
	assert.Equal(t, "application/problem+json", rr.Header().Get("Content-Type"))
	assert.JSONEq(t, `{"title":"Bu modül firmanızda kapalı.","status":403,"code":"module_disabled"}`, rr.Body.String())
}

func fixture(t *testing.T, name string) map[string]any {
	t.Helper()
	raw, err := os.ReadFile(filepath.Join("..", "..", "..", "..", "contracts", "fixtures", name))
	require.NoError(t, err)
	require.False(t, bytes.HasPrefix(raw, []byte{0xEF, 0xBB, 0xBF}), "fixture must not have a BOM")
	var m map[string]any
	require.NoError(t, json.Unmarshal(raw, &m))
	return m
}

func body(t *testing.T, d problem.Details) map[string]any {
	t.Helper()
	rr := httptest.NewRecorder()
	problem.Write(rr, d)
	assert.Equal(t, d.Status, rr.Code)
	assert.Equal(t, "application/problem+json", rr.Header().Get("Content-Type"))
	var m map[string]any
	require.NoError(t, json.Unmarshal(rr.Body.Bytes(), &m))
	return m
}

func TestProblem_JsonShapeMatchesDotnetFixture(t *testing.T) {
	validation := problem.Validation(map[string][]string{
		"Name":       {"Ünvan / ad soyad zorunludur.", "Ünvan 2 ile 200 karakter arasında olmalıdır."},
		"Code":       {"Cari kodu zorunludur.", "Cari kodu 1 ile 20 karakter arasında olmalıdır."},
		"IsCustomer": {"Cari en az müşteri, tedarikçi veya personel olarak işaretlenmelidir."},
	})
	cases := map[string]problem.Details{
		"problem-validation.json":   validation,
		"problem-unauthorized.json": problem.Unauthorized(),
		"problem-forbidden.json":    problem.Forbidden(),
		"problem-not-found.json": problem.NotFound(
			"Queried object CurrentAccount was not found, Key: 0197a3c2-5b1e-7c3a-9d2f-4e6b8a1c0d99"),
		"problem-app.json": problem.App(http.StatusForbidden, "module_disabled", "Bu modül firmanızda kapalı."),
	}
	for name, d := range cases {
		t.Run(name, func(t *testing.T) {
			assert.Equal(t, fixture(t, name), body(t, d))
		})
	}
}

func TestError_ImplementsError(t *testing.T) {
	var err error = &problem.Error{Details: problem.App(409, "idempotency_conflict", "Çakışma.")}
	var pe *problem.Error
	require.True(t, errors.As(err, &pe))
	assert.Equal(t, "idempotency_conflict", pe.Code)
	assert.Equal(t, "409 idempotency_conflict: Çakışma.", err.Error())
}

func TestTypeFor_MatchesDotnetDefaults(t *testing.T) {
	const base = "https://tools.ietf.org/html/rfc9110#section-"
	cases := map[int]string{
		400: base + "15.5.1", 401: base + "15.5.2", 403: base + "15.5.4", 404: base + "15.5.5",
		405: base + "15.5.6", 409: base + "15.5.10", 413: base + "15.5.14",
		500: base + "15.6.1", 503: base + "15.6.4",
		422: "https://tools.ietf.org/html/rfc4918#section-11.2",
		200: "", 302: "", 402: "", 999: "",
	}
	for status, want := range cases {
		assert.Equal(t, want, problem.TypeFor(status), status)
	}
	// The named constructors use the same URIs.
	assert.Equal(t, problem.TypeFor(400), problem.Validation(nil).Type)
	assert.Equal(t, problem.TypeFor(401), problem.Unauthorized().Type)
	assert.Equal(t, problem.TypeFor(403), problem.Forbidden().Type)
	assert.Equal(t, problem.TypeFor(404), problem.NotFound("").Type)
}
