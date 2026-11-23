// Package phone, İleti Merkezi'nin kabul ettiği Türkiye cep telefonu biçimlerini
// tek bir +90 biçimine çevirir.
package phone

import (
	"errors"
	"strings"
)

// ErrInvalid geçersiz bir numara biçimi için döner.
var ErrInvalid = errors.New("geçersiz telefon numarası")

// Normalize 905XXXXXXXXX, +905XXXXXXXXX ve 5XXXXXXXXX biçimlerini +905XXXXXXXXX'e çevirir.
// Boşluk, tire ve parantezler yok sayılır.
func Normalize(raw string) (string, error) {
	s := strings.NewReplacer(" ", "", "-", "", "(", "", ")", "").Replace(raw)
	switch {
	case strings.HasPrefix(s, "+90"):
		s = s[3:]
	case strings.HasPrefix(s, "90") && len(s) == 12:
		s = s[2:]
	}
	if len(s) != 10 || s[0] != '5' {
		return "", ErrInvalid
	}
	for _, c := range s {
		if c < '0' || c > '9' {
			return "", ErrInvalid
		}
	}
	return "+90" + s, nil
}
