package csrf_test

import (
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/stretchr/testify/assert"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/csrf"
)

func serve(method, header string) *httptest.ResponseRecorder {
	called := http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) { w.WriteHeader(http.StatusNoContent) })
	req := httptest.NewRequest(method, "/x", nil)
	if header != "" {
		req.Header.Set("X-Modaryo-Csrf", header)
	}
	rr := httptest.NewRecorder()
	csrf.Middleware(called).ServeHTTP(rr, req)
	return rr
}

func TestCsrf_PostWithoutHeader_403(t *testing.T) {
	rr := serve(http.MethodPost, "")

	assert.Equal(t, http.StatusForbidden, rr.Code)
	assert.Equal(t, "application/problem+json", rr.Header().Get("Content-Type"))
	assert.JSONEq(t, `{"title":"İstek reddedildi.","status":403,"detail":"Bu istek için 'X-Modaryo-Csrf: 1' başlığı gereklidir.","code":"csrf_header_missing"}`, rr.Body.String())
}

func TestCsrf_WrongHeaderValue_403(t *testing.T) {
	assert.Equal(t, http.StatusForbidden, serve(http.MethodDelete, "0").Code)
}

func TestCsrf_PostWithHeader_Passes(t *testing.T) {
	assert.Equal(t, http.StatusNoContent, serve(http.MethodPost, "1").Code)
}

func TestCsrf_SafeMethodsPass(t *testing.T) {
	for _, m := range []string{http.MethodGet, http.MethodHead, http.MethodOptions, http.MethodTrace} {
		assert.Equal(t, http.StatusNoContent, serve(m, "").Code, m)
	}
}

func TestCsrf_GetPasses(t *testing.T) {
	assert.Equal(t, http.StatusNoContent, serve(http.MethodGet, "").Code)
}
