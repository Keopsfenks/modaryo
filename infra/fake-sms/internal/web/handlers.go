// Package web, sahte SMS kutusu arayüzünü (GET /) ve test API'sini sunar.
package web

import (
	_ "embed"
	"encoding/json"
	"html/template"
	"net/http"
	"sort"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"

	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/phone"
	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/store"
)

//go:embed ui.html
var uiSource string

var uiTemplate = template.Must(template.New("ui").Parse(uiSource))

var istanbul = time.FixedZone("TRT", 3*60*60)

var statusLabels = map[string]string{
	"delivered": "İletildi",
	"failed":    "Başarısız",
	"pending":   "Beklemede",
}

type server struct {
	store *store.Store
}

type pageRow struct {
	Time   string
	Sender string
	To     string
	Text   string
	Status string
}

type pageData struct {
	Rows []pageRow
}

// New sahte SMS kutusu ve test API'si için HTTP işleyicisini döner.
func New(st *store.Store) http.Handler {
	s := &server{store: st}
	r := chi.NewRouter()
	r.Get("/", s.page)
	r.Get("/health", health)
	r.Get("/api/messages", s.list)
	r.Delete("/api/messages", s.clear)
	r.Get("/api/messages/{id}", s.get)
	return r
}

func (s *server) page(w http.ResponseWriter, r *http.Request) {
	var data pageData
	for _, m := range s.store.List(store.Filter{}) {
		data.Rows = append(data.Rows, pageRow{
			Time:   m.At.In(istanbul).Format("02.01.2006 15:04:05"),
			Sender: m.Sender,
			To:     strings.Join(m.To, ", "),
			Text:   m.Text,
			Status: statusText(m.Status),
		})
	}
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.Header().Set("Cache-Control", "no-store")
	if err := uiTemplate.Execute(w, data); err != nil {
		http.Error(w, "sayfa oluşturulamadı", http.StatusInternalServerError)
	}
}

func (s *server) list(w http.ResponseWriter, r *http.Request) {
	q := r.URL.Query()
	var f store.Filter
	if to := q.Get("to"); to != "" {
		norm, err := phone.Normalize(to)
		if err != nil {
			writeJSON(w, http.StatusBadRequest, map[string]string{"error": "geçersiz numara"})
			return
		}
		f.To = norm
	}
	f.Contains = q.Get("contains")
	if since := q.Get("since"); since != "" {
		t, err := time.Parse(time.RFC3339, since)
		if err != nil {
			writeJSON(w, http.StatusBadRequest, map[string]string{"error": "since RFC3339 olmalı"})
			return
		}
		f.Since = t
	}
	writeJSON(w, http.StatusOK, s.store.List(f))
}

func (s *server) get(w http.ResponseWriter, r *http.Request) {
	m, ok := s.store.Get(chi.URLParam(r, "id"))
	if !ok {
		writeJSON(w, http.StatusNotFound, map[string]string{"error": "mesaj bulunamadı"})
		return
	}
	writeJSON(w, http.StatusOK, m)
}

func (s *server) clear(w http.ResponseWriter, r *http.Request) {
	s.store.Clear()
	w.WriteHeader(http.StatusNoContent)
}

func health(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
}

func statusText(statuses map[string]string) string {
	nums := make([]string, 0, len(statuses))
	for n := range statuses {
		nums = append(nums, n)
	}
	sort.Strings(nums)
	parts := make([]string, 0, len(nums))
	for _, n := range nums {
		label := statusLabels[statuses[n]]
		if label == "" {
			label = statuses[n]
		}
		parts = append(parts, n+": "+label)
	}
	return strings.Join(parts, ", ")
}

func writeJSON(w http.ResponseWriter, code int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(code)
	_ = json.NewEncoder(w).Encode(v)
}
