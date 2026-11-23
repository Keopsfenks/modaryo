// Package secrets, kiracıya ve amaca bağlı AES-256-GCM ile gizli bilgi
// şifreleme (sealing) ve anahtar rotasyonu sağlar.
//
// Sealed biçim: v1:{keyId}:{base64(nonce|ciphertext|tag)}. Rastgele 12 baytlık
// nonce kullanılır. İlişkili veri (AD) "{tenantId}|{purpose}" olduğundan sealed
// bir değer başka bir kiracıya ya da amaca taşınamaz. İlk anahtar şifreler,
// tüm anahtarlar çözer.
package secrets

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"os"
	"regexp"
	"strings"

	"github.com/google/uuid"
)

// EnvKeys, anahtar listesini taşıyan ortam değişkenidir:
// "keyId:base64key[,keyId:base64key]".
const EnvKeys = "MODARYO_SECRETS_KEYS"

const (
	version  = "v1"
	keyBytes = 32
	nonceLen = 12
	tagLen   = 16
)

var (
	// ErrMalformed, sealed değerin biçiminin bozuk olduğunu belirtir.
	ErrMalformed = errors.New("secrets: sealed değer biçimi geçersiz")
	// ErrUnknownKey, sealed değerdeki anahtar kimliğinin tanınmadığını belirtir.
	ErrUnknownKey = errors.New("secrets: bilinmeyen anahtar kimliği")
	// ErrAuth, doğrulamanın başarısız olduğunu belirtir (yanlış anahtar,
	// kiracı, amaç veya değiştirilmiş veri).
	ErrAuth = errors.New("secrets: doğrulama başarısız")
	// ErrInvalidInput, kiracı veya amaç değerinin geçersiz olduğunu belirtir.
	ErrInvalidInput = errors.New("secrets: geçersiz kiracı veya amaç")
	// ErrConfig, anahtar yapılandırmasının geçersiz olduğunu belirtir.
	ErrConfig = errors.New("secrets: anahtar yapılandırması geçersiz")
)

var keyIDPattern = regexp.MustCompile(`^[A-Za-z0-9_-]{1,32}$`)

const redacted = "[REDACTED]"

// Key bir şifreleme anahtarıdır. Gizli bayt değerleri hiçbir biçimlendirme
// fiilinde yazdırılmaz.
type Key struct {
	ID     string
	Secret []byte
}

// String anahtar baytlarını göstermez.
func (k Key) String() string { return "secrets.Key{ID:" + k.ID + ", Secret:" + redacted + "}" }

// GoString anahtar baytlarını göstermez.
func (k Key) GoString() string { return k.String() }

// Format tüm fiiller için gizli değeri maskeler.
func (k Key) Format(f fmt.State, _ rune) { _, _ = f.Write([]byte(k.String())) }

// MarshalJSON anahtar baytları yerine maskelenmiş biçimi yazar: {"ID":"...","Secret":"[REDACTED]"}.
func (k Key) MarshalJSON() ([]byte, error) {
	return json.Marshal(struct {
		ID     string
		Secret string
	}{k.ID, redacted})
}

// LogValue slog çıktısında anahtar baytlarını maskeler.
func (k Key) LogValue() slog.Value {
	return slog.GroupValue(slog.String("id", k.ID), slog.String("secret", redacted))
}

type entry struct {
	id   string
	aead cipher.AEAD
}

// Box, sealed değerleri üretir ve açar. Eşzamanlı kullanım güvenlidir.
type Box struct {
	keys []entry
	byID map[string]cipher.AEAD
}

// String anahtar bilgisi göstermez.
func (b Box) String() string { return "secrets.Box{" + redacted + "}" }

// GoString anahtar bilgisi göstermez.
func (b Box) GoString() string { return b.String() }

// Format tüm fiiller için içeriği maskeler.
func (b Box) Format(f fmt.State, _ rune) { _, _ = f.Write([]byte(b.String())) }

// NewBox verilen anahtarlardan bir Box kurar. İlk anahtar şifreler; hepsi çözer.
func NewBox(keys []Key) (*Box, error) {
	if len(keys) == 0 {
		return nil, fmt.Errorf("%w: en az bir anahtar gerekli", ErrConfig)
	}
	b := &Box{byID: make(map[string]cipher.AEAD, len(keys))}
	for _, k := range keys {
		if !keyIDPattern.MatchString(k.ID) {
			return nil, fmt.Errorf("%w: anahtar kimliği geçersiz", ErrConfig)
		}
		if _, dup := b.byID[k.ID]; dup {
			return nil, fmt.Errorf("%w: anahtar kimliği tekrarlanıyor: %s", ErrConfig, k.ID)
		}
		if len(k.Secret) != keyBytes {
			return nil, fmt.Errorf("%w: anahtar %s için uzunluk %d bayt olmalı", ErrConfig, k.ID, keyBytes)
		}
		block, err := aes.NewCipher(k.Secret)
		if err != nil {
			return nil, fmt.Errorf("%w: anahtar %s kurulamadı", ErrConfig, k.ID)
		}
		aead, err := cipher.NewGCM(block)
		if err != nil {
			return nil, fmt.Errorf("%w: anahtar %s kurulamadı", ErrConfig, k.ID)
		}
		b.keys = append(b.keys, entry{id: k.ID, aead: aead})
		b.byID[k.ID] = aead
	}
	return b, nil
}

// NewBoxFromString "keyId:base64key[,keyId:base64key]" biçimini ayrıştırır.
func NewBoxFromString(spec string) (*Box, error) {
	if strings.TrimSpace(spec) == "" {
		return nil, fmt.Errorf("%w: anahtar listesi boş", ErrConfig)
	}
	var keys []Key
	for _, part := range strings.Split(spec, ",") {
		part = strings.TrimSpace(part)
		if part == "" {
			return nil, fmt.Errorf("%w: boş anahtar girdisi", ErrConfig)
		}
		id, enc, ok := strings.Cut(part, ":")
		if !ok || !keyIDPattern.MatchString(id) {
			return nil, fmt.Errorf("%w: anahtar girdisi 'kimlik:base64' biçiminde olmalı", ErrConfig)
		}
		secret, err := base64.StdEncoding.DecodeString(enc)
		if err != nil {
			return nil, fmt.Errorf("%w: anahtar %s base64 değil", ErrConfig, id)
		}
		keys = append(keys, Key{ID: id, Secret: secret})
	}
	return NewBox(keys)
}

// NewBoxFromEnv MODARYO_SECRETS_KEYS ortam değişkeninden Box kurar.
func NewBoxFromEnv() (*Box, error) {
	return NewBoxFromString(os.Getenv(EnvKeys))
}

func associatedData(tenant uuid.UUID, purpose string) ([]byte, error) {
	if tenant == uuid.Nil || purpose == "" || strings.Contains(purpose, "|") {
		return nil, ErrInvalidInput
	}
	return []byte(tenant.String() + "|" + purpose), nil
}

// Seal düz metni ilk anahtarla şifreler.
func (b *Box) Seal(tenant uuid.UUID, purpose string, plaintext []byte) (string, error) {
	ad, err := associatedData(tenant, purpose)
	if err != nil {
		return "", err
	}
	k := b.keys[0]
	nonce := make([]byte, nonceLen, nonceLen+len(plaintext)+tagLen)
	if _, err := rand.Read(nonce); err != nil {
		return "", fmt.Errorf("secrets: nonce üretilemedi: %w", err)
	}
	out := k.aead.Seal(nonce, nonce, plaintext, ad)
	return version + ":" + k.id + ":" + base64.StdEncoding.EncodeToString(out), nil
}

// Open sealed değeri çözer. Hata değerleri düz metin veya anahtar içermez.
func (b *Box) Open(tenant uuid.UUID, purpose string, sealed string) ([]byte, error) {
	ad, err := associatedData(tenant, purpose)
	if err != nil {
		return nil, err
	}
	parts := strings.Split(sealed, ":")
	if len(parts) != 3 || parts[0] != version {
		return nil, ErrMalformed
	}
	aead, ok := b.byID[parts[1]]
	if !ok {
		return nil, ErrUnknownKey
	}
	raw, err := base64.StdEncoding.DecodeString(parts[2])
	if err != nil || len(raw) < nonceLen+tagLen {
		return nil, ErrMalformed
	}
	plain, err := aead.Open(nil, raw[:nonceLen], raw[nonceLen:], ad)
	if err != nil {
		return nil, ErrAuth
	}
	return plain, nil
}
