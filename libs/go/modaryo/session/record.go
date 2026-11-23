// Package session reads the session records identity writes to Valkey (contract v1).
// Behaviour mirrors libs/dotnet/Modaryo.Sessions; the reader never writes the value.
package session

import (
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"slices"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

const (
	// CookieName is the session cookie name.
	CookieName = "modaryo_sid"
	// CurrentVersion is the supported record version.
	CurrentVersion = 1
	// AllPermissions is the permission value that grants everything.
	AllPermissions = "*"

	UserTypeEmployee    = "employee"
	UserTypeDealer      = "dealer"
	UserTypeSelfService = "self_service"
)

var (
	// ErrNotFound: missing record, empty sid or malformed JSON.
	ErrNotFound = errors.New("session: not found")
	// ErrExpired: the absolute lifetime has passed.
	ErrExpired = errors.New("session: expired")
	// ErrUnsupportedVersion: the record version is not v1.
	ErrUnsupportedVersion = errors.New("session: unsupported version")
)

// Key returns the "sess:{sha256_hex(sid)}" Valkey key.
func Key(sid string) string {
	sum := sha256.Sum256([]byte(sid))
	return "sess:" + hex.EncodeToString(sum[:])
}

func isKnownUserType(t string) bool {
	return t == UserTypeEmployee || t == UserTypeDealer || t == UserTypeSelfService
}

// Record is the session record in Valkey. Fields missing in older records read with the P0a
// defaults: user_type "employee", modules [], branch_ids nil (all branches), max_discount_pct 0.
type Record struct {
	V                 int        `json:"v"`
	ID                string     `json:"id"`
	UserID            uuid.UUID  `json:"user_id"`
	Email             string     `json:"email"`
	FullName          string     `json:"full_name"`
	TenantID          *uuid.UUID `json:"tenant_id"`
	Permissions       []string   `json:"permissions"`
	RememberMe        bool       `json:"remember_me"`
	CreatedAt         int64      `json:"created_at"`
	AbsoluteExpiresAt int64      `json:"absolute_expires_at"`
	IdleTimeoutS      int        `json:"idle_timeout_s"`
	IP                *string    `json:"ip"`
	UserAgent         *string    `json:"user_agent"`

	UserType       string          `json:"user_type"`
	CariID         *uuid.UUID      `json:"cari_id"`
	Modules        []string        `json:"modules"`
	BranchIDs      *[]uuid.UUID    `json:"branch_ids"` // nil: all branches, empty: none
	MaxDiscountPct decimal.Decimal `json:"max_discount_pct"`
}

// HasPermission: "*" grants every permission, others must match exactly. Null permissions are empty.
func (r Record) HasPermission(p string) bool {
	return slices.Contains(r.Permissions, AllPermissions) || slices.Contains(r.Permissions, p)
}

// HasModule reports whether the module key is enabled.
func (r Record) HasModule(k string) bool {
	return slices.Contains(r.Modules, k)
}

// CanAccessBranch: nil BranchIDs means all branches, an empty list means none.
func (r Record) CanAccessBranch(id uuid.UUID) bool {
	return r.BranchIDs == nil || slices.Contains(*r.BranchIDs, id)
}
