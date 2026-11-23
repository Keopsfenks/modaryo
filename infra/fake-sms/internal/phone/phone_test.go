package phone

import "testing"

func TestNormalize(t *testing.T) {
	cases := map[string]string{
		"905321234567":      "+905321234567",
		"+905321234567":     "+905321234567",
		"5321234567":        "+905321234567",
		"+90 532 123 45 67": "+905321234567",
	}
	for in, want := range cases {
		got, err := Normalize(in)
		if err != nil {
			t.Fatalf("Normalize(%q) hata verdi: %v", in, err)
		}
		if got != want {
			t.Fatalf("Normalize(%q) = %q, beklenen %q", in, got, want)
		}
	}
}

func TestNormalize_Invalid(t *testing.T) {
	for _, in := range []string{"", "12345", "+1 555 0100", "0532 123 45 67", "abc"} {
		if _, err := Normalize(in); err == nil {
			t.Fatalf("Normalize(%q) hata bekleniyordu", in)
		}
	}
}
