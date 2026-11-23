package session

import (
	"errors"
	"net/http"
	"slices"

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

func unauthorized() problem.Details {
	return problem.App(http.StatusUnauthorized, "unauthorized", "Oturum açmanız gerekiyor.")
}

// Middleware reads the "modaryo_sid" cookie and stores the *Record in the context.
// An invalid session yields 401; a Valkey failure yields 503.
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
				problem.Write(w, unauthorized())
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
				problem.Write(w, problem.App(http.StatusServiceUnavailable, "session_store_unavailable",
					"Oturum servisine şu anda ulaşılamıyor."))
				return
			}
			if !isKnownUserType(rec.UserType) || !slices.Contains(allowed, rec.UserType) {
				reject()
				return
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
		d := problem.App(http.StatusForbidden, "forbidden", "Bu işlem için yetkiniz yok.")
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
				problem.Write(w, unauthorized())
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
