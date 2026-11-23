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
		"IsCustomer": {"Cari en az müşteri veya tedarikçi olarak işaretlenmelidir."},
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
