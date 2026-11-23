// Package csrf requires the "X-Modaryo-Csrf: 1" header on state-changing requests (mirrors
// libs/dotnet/Modaryo.Sessions CsrfHeaderMiddleware). Services allow no CORS, so another
// site cannot send the header.
package csrf

import (
	"net/http"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/problem"
)

const (
	// Header is the required request header.
	Header = "X-Modaryo-Csrf"
	// ErrorCode is the ProblemDetails code of the rejection.
	ErrorCode = "csrf_header_missing"
)

// Middleware lets GET/HEAD/OPTIONS/TRACE through and rejects other methods without "X-Modaryo-Csrf: 1" with 403.
func Middleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if isSafe(r.Method) || r.Header.Get(Header) == "1" {
			next.ServeHTTP(w, r)
			return
		}
		d := problem.App(http.StatusForbidden, ErrorCode, "İstek reddedildi.")
		d.Detail = "Bu istek için '" + Header + ": 1' başlığı gereklidir."
		problem.Write(w, d)
	})
}

func isSafe(method string) bool {
	switch method {
	case http.MethodGet, http.MethodHead, http.MethodOptions, http.MethodTrace:
		return true
	}
	return false
}
