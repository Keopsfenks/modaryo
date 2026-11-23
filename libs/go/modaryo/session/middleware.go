package session

import (
	"errors"
	"net/http"
	"slices"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/logx"
	"github.com/keopsfenks/modaryo/libs/go/modaryo/problem"
)

// Options are the middleware settings.
type Options struct {
	// AllowedUserTypes defaults to only "employee" when empty.
	AllowedUserTypes []string
	// Optional passes the request through without a record when the cookie is missing,
	// invalid or of a disallowed user type.
	Optional bool
}

// Middleware reads the "modaryo_sid" cookie and stores the *Record in the context.
// An invalid session yields 401; a Valkey failure yields 503. An accepted record with an active
// company also records the tenant for the access log (logx.SetTenant).
func Middleware(r *Reader, opts Options) func(http.Handler) http.Handler {
	allowed := opts.AllowedUserTypes
	if len(allowed) == 0 {
		allowed = []string{UserTypeEmployee}
	}
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, req *http.Request) {
			reject := func() {
				if opts.Optional {
					next.ServeHTTP(w, req)
					return
				}
				problem.Write(w, problem.Unauthorized())
			}

			c, err := req.Cookie(CookieName)
			if err != nil || c.Value == "" {
				reject()
				return
			}
			rec, err := r.Read(req.Context(), c.Value)
			switch {
			case errors.Is(err, ErrNotFound), errors.Is(err, ErrExpired), errors.Is(err, ErrUnsupportedVersion):
				reject()
				return
			case err != nil:
				d := problem.App(http.StatusServiceUnavailable, "session_store_unavailable",
					"Oturum servisine şu anda ulaşılamıyor.")
				d.Type = problem.TypeFor(http.StatusServiceUnavailable)
				problem.Write(w, d)
				return
			}
			if !isKnownUserType(rec.UserType) || !slices.Contains(allowed, rec.UserType) {
				reject()
				return
			}
			if rec.TenantID != nil {
				// Lets the outer logx.RequestMiddleware report tenant_id on the access line.
				logx.SetTenant(req.Context(), *rec.TenantID)
			}
			next.ServeHTTP(w, req.WithContext(WithRecord(req.Context(), rec)))
		})
	}
}

// RequirePermission yields 401 without a record and 403 "forbidden" without the permission.
func RequirePermission(p string) func(http.Handler) http.Handler {
	return guard(func(rec *Record) *problem.Details {
		if rec.HasPermission(p) {
			return nil
		}
		d := problem.Forbidden()
		return &d
	})
}

// RequireModule yields 403 "module_disabled" when the module is off (same title as ERP).
func RequireModule(k string) func(http.Handler) http.Handler {
	return guard(func(rec *Record) *problem.Details {
		if rec.HasModule(k) {
			return nil
		}
		d := problem.App(http.StatusForbidden, "module_disabled", "Bu modül firmanızda kapalı.")
		return &d
	})
}

func guard(check func(*Record) *problem.Details) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, req *http.Request) {
			rec, ok := FromContext(req.Context())
			if !ok {
				problem.Write(w, problem.Unauthorized())
				return
			}
			if d := check(rec); d != nil {
				problem.Write(w, *d)
				return
			}
			next.ServeHTTP(w, req)
		})
	}
}
