// Package problem produces RFC 9457 "application/problem+json" responses.
package problem

import (
	"encoding/json"
	"net/http"
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

// Type URIs of the .NET services (RFC 9110 sections), kept identical per status.
const (
	typeBadRequest   = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
	typeUnauthorized = "https://tools.ietf.org/html/rfc9110#section-15.5.2"
	typeForbidden    = "https://tools.ietf.org/html/rfc9110#section-15.5.4"
	typeNotFound     = "https://tools.ietf.org/html/rfc9110#section-15.5.5"
)

// Validation is the 400 "validation_failed" body with the field errors (same as .NET).
func Validation(errors map[string][]string) Details {
	return Details{Type: typeBadRequest, Title: "Formdaki hataları düzeltin.", Status: http.StatusBadRequest,
		Code: "validation_failed", Errors: errors}
}

// Unauthorized is the 401 "unauthorized" body.
func Unauthorized() Details {
	return Details{Type: typeUnauthorized, Title: "Oturum açmanız gerekiyor.", Status: http.StatusUnauthorized,
		Code: "unauthorized"}
}

// Forbidden is the 403 "forbidden" body.
func Forbidden() Details {
	return Details{Type: typeForbidden, Title: "Bu işlem için yetkiniz yok.", Status: http.StatusForbidden,
		Code: "forbidden"}
}

// NotFound is the 404 "not_found" body; detail is optional.
func NotFound(detail string) Details {
	return Details{Type: typeNotFound, Title: "Kayıt bulunamadı.", Status: http.StatusNotFound,
		Detail: detail, Code: "not_found"}
}

// App builds the { status, code, title } response equivalent to ERP AppException.
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
