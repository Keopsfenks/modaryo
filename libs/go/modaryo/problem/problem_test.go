package problem_test

import (
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/stretchr/testify/assert"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/problem"
)

func TestWrite_ProblemJSONShape(t *testing.T) {
	rr := httptest.NewRecorder()
	problem.Write(rr, problem.App(http.StatusForbidden, "module_disabled", "Bu modül firmanızda kapalı."))

	assert.Equal(t, http.StatusForbidden, rr.Code)
	assert.Equal(t, "application/problem+json", rr.Header().Get("Content-Type"))
	assert.JSONEq(t, `{"title":"Bu modül firmanızda kapalı.","status":403,"code":"module_disabled"}`, rr.Body.String())
}
