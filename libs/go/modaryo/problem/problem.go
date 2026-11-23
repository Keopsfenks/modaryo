// Package problem produces RFC 9457 "application/problem+json" responses.
package problem

import (
	"encoding/json"
	"net/http"
	"strconv"
)

// Details uses the same field names as the ProblemDetails body of the .NET services.
// Code and Errors are top-level extension members.
type Details struct {
	Type   string              `json:"type,omitempty"`
	Title  string              `json:"title"`
	Status int                 `json:"status"`
	Detail string              `json:"detail,omitempty"`
	Code   string              `json:"code,omitempty"`
	Errors map[string][]string `json:"errors,omitempty"`
}

const typeBase = "https://tools.ietf.org/html/rfc9110#section-"

// types are the ASP.NET Core ProblemDetails defaults (TypedResults.Problem / ProblemDetailsDefaults):
// the RFC 9110 section of each status. Statuses without a section there have no type.
var types = map[int]string{
	400: "15.5.1", 401: "15.5.2", 403: "15.5.4", 404: "15.5.5", 405: "15.5.6", 406: "15.5.7",
	407: "15.5.8", 408: "15.5.9", 409: "15.5.10", 410: "15.5.11", 411: "15.5.12", 412: "15.5.13",
	413: "15.5.14", 414: "15.5.15", 415: "15.5.16", 416: "15.5.17", 417: "15.5.18", 421: "15.5.20",
	426: "15.5.22",
	500: "15.6.1", 501: "15.6.2", 502: "15.6.3", 503: "15.6.4", 504: "15.6.5", 505: "15.6.6",
}

// TypeFor returns the "type" URI the .NET services emit for status
// ("https://tools.ietf.org/html/rfc9110#section-15.5.2" for 401), or "" when .NET has none.
func TypeFor(status int) string {
	if status == http.StatusUnprocessableEntity {
		return "https://tools.ietf.org/html/rfc4918#section-11.2" // the one default outside RFC 9110
	}
	if s, ok := types[status]; ok {
		return typeBase + s
	}
	return ""
}

// Validation is the 400 "validation_failed" body with the field errors (same as .NET).
func Validation(errors map[string][]string) Details {
	return Details{Type: TypeFor(http.StatusBadRequest), Title: "Formdaki hataları düzeltin.", Status: http.StatusBadRequest,
		Code: "validation_failed", Errors: errors}
}

// Unauthorized is the 401 "unauthorized" body.
func Unauthorized() Details {
	return Details{Type: TypeFor(http.StatusUnauthorized), Title: "Oturum açmanız gerekiyor.", Status: http.StatusUnauthorized,
		Code: "unauthorized"}
}

// Forbidden is the 403 "forbidden" body.
func Forbidden() Details {
	return Details{Type: TypeFor(http.StatusForbidden), Title: "Bu işlem için yetkiniz yok.", Status: http.StatusForbidden,
		Code: "forbidden"}
}

// NotFound is the 404 "not_found" body; detail is optional.
func NotFound(detail string) Details {
	return Details{Type: TypeFor(http.StatusNotFound), Title: "Kayıt bulunamadı.", Status: http.StatusNotFound,
		Detail: detail, Code: "not_found"}
}

// App builds the { status, code, title } response equivalent to ERP AppException; like
// AppException it has no type. Set Type (TypeFor) for bodies .NET emits through TypedResults.Problem.
func App(status int, code, title string) Details {
	return Details{Title: title, Status: status, Code: code}
}

// Write writes d as application/problem+json.
func Write(w http.ResponseWriter, d Details) {
	w.Header().Set("Content-Type", "application/problem+json")
	w.WriteHeader(d.Status)
	enc := json.NewEncoder(w)
	enc.SetEscapeHTML(false)
	_ = enc.Encode(d)
}

// Error is a Details body received from another service, usable as an error value.
type Error struct {
	Details
}

// Error implements the error interface: "{status} {code}: {title}".
func (e *Error) Error() string {
	msg := strconv.Itoa(e.Status)
	if e.Code != "" {
		msg += " " + e.Code
	}
	if e.Title != "" {
		msg += ": " + e.Title
	}
	return msg
}
