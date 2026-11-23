// Package provider, İleti Merkezi JSON API'sinin (send-sms, get-report, get-balance)
// sahte bir uygulamasıdır. Hata koşulları alıcı numarasının son dört hanesine göre
// deterministik olarak enjekte edilir.
package provider

import (
	"crypto/subtle"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"strconv"
	"strings"
	"sync/atomic"
	"time"

	"github.com/go-chi/chi/v5"

	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/phone"
	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/store"
)

// Enjeksiyon kuralları (alıcı numarasının soneki).
const (
	suffixFailed       = "0000" // rapor: başarısız
	suffixInsufficient = "9999" // gönderim: bakiye yetersiz
	suffixStuck        = "5555" // rapor: süresiz beklemede
)

// Dokümandaki durum kodları.
const (
	codeOK                     = 200
	codeBadRequest             = 400
	codeAuthFailed             = 401
	codeInsufficient           = 402
	codeValidation             = 422
	codeRecipientInvalid       = 452
	codeTextEmpty              = 454
	codeOrderNotFound          = 455
	codeDateFormat             = 457
	codeSenderEmpty            = 459
	codeSenderInvalid          = 460
	codeInvalidNumber          = 466
	codeIYSRequired            = 468
	codeIYSListRequired        = 469
	msgOK                      = "İşlem başarılı"
	msgAuthFailed              = "Üyelik bilgileri hatalı"
	msgInsufficient            = "Bakiye yetersiz"
	msgBadRequest              = "İstek çözümlenemedi"
	msgValidation              = "İstek içerisindeki değerler doğrulanamadı"
	msgRecipientInvalid        = "Mesaj alıcıları hatalı"
	msgTextEmpty               = "Mesaj metni boş"
	msgOrderNotFound           = "Sipariş bulunamadı"
	msgDateFormat              = "Mesaj gönderim tarihinin formatı hatalı"
	msgSenderEmpty             = "Başlık kısmı boş olamaz"
	msgSenderInvalid           = "Başlık 11 karakterden fazla olmamalı ve Türkçe karakter içermemelidir"
	msgInvalidNumber           = "Hatalı numara"
	msgIYSRequired             = "IYS üzerinden sorgulama yapılıp yapılmayacağını ayarlamalısınız"
	msgIYSListRequired         = "IYS listenizi seçmelisiniz, BIREYSEL veya TACIR"
	firstOrderID         int64 = 312891245
)

// Rapor satırı durum kodları.
const (
	lineSending   = 110
	lineDelivered = 111
	lineFailed    = 112
)

// Sipariş durum kodları.
const (
	orderInProgress = 113
	orderCompleted  = 114
	orderFailed     = 115
)

// Config sağlayıcı davranışını belirler.
type Config struct {
	Store       *store.Store
	Credentials map[string]string // anahtar → hash
	Now         func() time.Time
}

type provider struct {
	cfg      Config
	orderSeq atomic.Int64
}

// ParseCredentials "anahtar:hash,anahtar2:hash2" biçimini ayrıştırır.
func ParseCredentials(s string) (map[string]string, error) {
	out := map[string]string{}
	for _, pair := range strings.Split(s, ",") {
		pair = strings.TrimSpace(pair)
		if pair == "" {
			continue
		}
		key, hash, ok := strings.Cut(pair, ":")
		if !ok || key == "" || hash == "" {
			return nil, fmt.Errorf("geçersiz kimlik bilgisi %q (beklenen anahtar:hash)", pair)
		}
		out[key] = hash
	}
	if len(out) == 0 {
		return nil, errors.New("en az bir kimlik bilgisi gerekli")
	}
	return out, nil
}

// New sağlayıcı API'sinin HTTP işleyicisini döner.
func New(cfg Config) http.Handler {
	p := &provider{cfg: cfg}
	p.orderSeq.Store(firstOrderID)
	r := chi.NewRouter()
	r.Post("/v1/send-sms/json", p.send)
	r.Post("/v1/get-report/json", p.report)
	r.Post("/v1/get-balance/json", p.balance)
	return r
}

func (p *provider) authenticated(a authentication) bool {
	hash, ok := p.cfg.Credentials[a.Key]
	if !ok {
		return false
	}
	return subtle.ConstantTimeCompare([]byte(hash), []byte(a.Hash)) == 1
}

func (p *provider) send(w http.ResponseWriter, r *http.Request) {
	var req sendRequest
	if !decode(w, r, &req) {
		return
	}
	if !p.authenticated(req.Request.Authentication) {
		writeError(w, codeAuthFailed, msgAuthFailed)
		return
	}
	order := req.Request.Order

	if order.IYS != "0" && order.IYS != "1" {
		writeError(w, codeIYSRequired, msgIYSRequired)
		return
	}
	if order.IYS == "1" && order.IYSList != "BIREYSEL" && order.IYSList != "TACIR" {
		writeError(w, codeIYSListRequired, msgIYSListRequired)
		return
	}
	if order.Sender == "" {
		writeError(w, codeSenderEmpty, msgSenderEmpty)
		return
	}
	if len([]rune(order.Sender)) > 11 || strings.ContainsFunc(order.Sender, isTurkishLetter) {
		writeError(w, codeSenderInvalid, msgSenderInvalid)
		return
	}
	if order.SendDateTime != "" {
		if _, err := time.Parse("02/01/2006 15:04", order.SendDateTime); err != nil {
			writeError(w, codeDateFormat, msgDateFormat)
			return
		}
	}
	if order.Message.Text == "" {
		writeError(w, codeTextEmpty, msgTextEmpty)
		return
	}
	raw := order.Message.Receipents.Number
	if len(raw) == 0 {
		writeError(w, codeRecipientInvalid, msgRecipientInvalid)
		return
	}
	to := make([]string, 0, len(raw))
	for _, n := range raw {
		norm, err := phone.Normalize(n)
		if err != nil {
			writeError(w, codeInvalidNumber, msgInvalidNumber)
			return
		}
		to = append(to, norm)
	}

	statuses := map[string]string{}
	stuck := map[string]bool{}
	for _, n := range to {
		if strings.HasSuffix(n, suffixInsufficient) {
			writeError(w, codeInsufficient, msgInsufficient)
			return
		}
		switch {
		case strings.HasSuffix(n, suffixFailed):
			statuses[n] = "failed"
		case strings.HasSuffix(n, suffixStuck):
			statuses[n] = "pending"
			stuck[n] = true
		default:
			statuses[n] = "pending"
		}
	}

	orderID := strconv.FormatInt(p.orderSeq.Add(1), 10)
	p.cfg.Store.Add(store.Message{
		At:      p.cfg.Now(),
		Sender:  order.Sender,
		To:      to,
		Text:    order.Message.Text,
		OrderID: orderID,
		Status:  statuses,
		Stuck:   stuck,
	})

	var resp sendResponse
	resp.Response.Status = status{Code: codeOK, Message: msgOK}
	resp.Response.Order.ID = orderID
	writeJSON(w, http.StatusOK, resp)
}

func (p *provider) report(w http.ResponseWriter, r *http.Request) {
	var req reportRequest
	if !decode(w, r, &req) {
		return
	}
	if !p.authenticated(req.Request.Authentication) {
		writeError(w, codeAuthFailed, msgAuthFailed)
		return
	}
	id := req.Request.Order.ID
	if id == "" {
		writeError(w, codeValidation, msgValidation)
		return
	}
	m, ok := p.cfg.Store.ByOrderID(id)
	if !ok {
		writeError(w, codeOrderNotFound, msgOrderNotFound)
		return
	}

	var resp reportResponse
	o := &resp.Response.Order
	resp.Response.Status = status{Code: codeOK, Message: msgOK}
	o.ID = m.OrderID
	o.Total = len(m.To)
	o.Sender = m.Sender
	o.Price = float64(len(m.To))
	o.SubmitAt = m.At.In(istanbul).Format(dateLayout)
	o.SendAt = o.SubmitAt
	for _, n := range m.To {
		st := m.Status[n]
		item := reportItem{Number: n}
		switch st {
		case "delivered":
			item.Status = lineDelivered
			o.Delivered++
		case "failed":
			item.Status = lineFailed
			o.Undelivered++
		default:
			item.Status = lineSending
			o.Waiting++
		}
		o.Message = append(o.Message, item)
	}
	switch {
	case o.Waiting > 0:
		o.Status = orderInProgress
	case o.Undelivered == o.Total:
		o.Status = orderFailed
	default:
		o.Status = orderCompleted
	}
	writeJSON(w, http.StatusOK, resp)
}

func (p *provider) balance(w http.ResponseWriter, r *http.Request) {
	var req balanceRequest
	if !decode(w, r, &req) {
		return
	}
	if !p.authenticated(req.Request.Authentication) {
		writeError(w, codeAuthFailed, msgAuthFailed)
		return
	}
	var resp balanceResponse
	resp.Response.Status = status{Code: codeOK, Message: msgOK}
	resp.Response.Balance.Amount = 10.584
	resp.Response.Balance.SMS = 67
	writeJSON(w, http.StatusOK, resp)
}

// istanbul sendDateTime ve rapor tarihleri için Türkiye saati (UTC+3, yaz saati yok).
var istanbul = time.FixedZone("TRT", 3*60*60)

const dateLayout = "2006-01-02 15:04:05"

func isTurkishLetter(r rune) bool {
	return strings.ContainsRune("çğıöşüÇĞİÖŞÜ", r)
}

func decode(w http.ResponseWriter, r *http.Request, v any) bool {
	if err := json.NewDecoder(r.Body).Decode(v); err != nil {
		writeError(w, codeBadRequest, msgBadRequest)
		return false
	}
	return true
}

// writeError hata zarfını yazar. HTTP durumu, kod 400-599 aralığındaysa kodla aynıdır;
// değilse 400 kullanılır.
func writeError(w http.ResponseWriter, code int, message string) {
	var resp errorResponse
	resp.Response.Status = status{Code: code, Message: message}
	httpCode := http.StatusBadRequest
	if code >= 400 && code <= 599 {
		httpCode = code
	}
	writeJSON(w, httpCode, resp)
}

func writeJSON(w http.ResponseWriter, httpCode int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(httpCode)
	_ = json.NewEncoder(w).Encode(v)
}
