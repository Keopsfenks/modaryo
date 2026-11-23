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

// App builds the { status, code, title } response equivalent to ERP AppException.
func App(status int, code, title string) Details {
	return Details{Title: title, Status: status, Code: code}
}

// Write writes d as application/problem+json.
func Write(w http.ResponseWriter, d Details) {
	w.Header().Set("Content-Type", "application/problem+json")
	w.WriteHeader(d.Status)
	_ = json.NewEncoder(w).Encode(d)
}
