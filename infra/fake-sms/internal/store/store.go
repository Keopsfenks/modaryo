// Package store, sahte SMS mesajlarını bellekte tutar.
//
// Teslim durumu okuma anında hesaplanır: "pending" olan ve takılı olmayan
// bir numara, gönderimden DeliveryDelay sonra "delivered" görünür.
package store

import (
	"strconv"
	"strings"
	"sync"
	"time"
)

// DeliveryDelay normal bir numaranın iletildi olması için beklenen süredir.
const DeliveryDelay = time.Second

// Message tek bir gönderilmiş SMS'i (birden fazla alıcı olabilir) temsil eder.
// Status değerleri: delivered | failed | pending.
type Message struct {
	ID      string            `json:"id"`
	At      time.Time         `json:"at"`
	Sender  string            `json:"sender"`
	To      []string          `json:"to"`
	Text    string            `json:"text"`
	OrderID string            `json:"orderId"`
	Status  map[string]string `json:"status"`
	// Stuck, numaraya karşılık gelen durumun süresiz "pending" kalacağını belirtir.
	Stuck map[string]bool `json:"-"`
}

// Filter test API'si için mesaj süzgecidir. Sıfır değerler süzme yapmaz.
type Filter struct {
	To       string    // normalize edilmiş +90 numarası
	Contains string    // metinde geçen alt dize
	Since    time.Time // bu andan itibaren gönderilenler
}

// Store, en fazla capacity mesaj tutan iş parçacığı güvenli bir depodur.
type Store struct {
	mu       sync.Mutex
	capacity int
	now      func() time.Time
	seq      int64
	messages []Message // en eski başta
}

// New, verilen kapasite ve saat kaynağıyla boş bir depo döner.
func New(capacity int, now func() time.Time) *Store {
	return &Store{capacity: capacity, now: now}
}

// Add mesajı ekler, ID atar ve teslim durumu çözülmüş bir kopya döner.
// Kapasite aşılırsa en eski mesajlar düşürülür.
func (s *Store) Add(m Message) Message {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.seq++
	m.ID = strconv.FormatInt(s.seq, 10)
	m.Status = copyStatus(m.Status)
	m.Stuck = copyStuck(m.Stuck)
	m.To = append([]string(nil), m.To...)
	s.messages = append(s.messages, m)
	if over := len(s.messages) - s.capacity; over > 0 {
		kept := make([]Message, s.capacity)
		copy(kept, s.messages[over:])
		s.messages = kept
	}
	return s.resolve(m)
}

// List süzgece uyan mesajları en yeniden en eskiye sıralı döner.
func (s *Store) List(f Filter) []Message {
	s.mu.Lock()
	defer s.mu.Unlock()
	now := s.now()
	out := []Message{}
	for i := len(s.messages) - 1; i >= 0; i-- {
		m := s.messages[i]
		if !matches(m, f) {
			continue
		}
		out = append(out, s.resolveAt(m, now))
	}
	return out
}

// Get verilen ID'li mesajı döner.
func (s *Store) Get(id string) (Message, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	for _, m := range s.messages {
		if m.ID == id {
			return s.resolve(m), true
		}
	}
	return Message{}, false
}

// ByOrderID verilen sipariş numaralı mesajı döner.
func (s *Store) ByOrderID(orderID string) (Message, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	for _, m := range s.messages {
		if m.OrderID == orderID {
			return s.resolve(m), true
		}
	}
	return Message{}, false
}

// Clear tüm mesajları siler.
func (s *Store) Clear() {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.messages = nil
}

func (s *Store) resolve(m Message) Message {
	return s.resolveAt(m, s.now())
}

func (s *Store) resolveAt(m Message, now time.Time) Message {
	out := m
	out.Status = copyStatus(m.Status)
	out.To = append([]string(nil), m.To...)
	out.Stuck = nil
	if now.Before(m.At.Add(DeliveryDelay)) {
		return out
	}
	for num, st := range out.Status {
		if st == "pending" && !m.Stuck[num] {
			out.Status[num] = "delivered"
		}
	}
	return out
}

func matches(m Message, f Filter) bool {
	if f.To != "" {
		found := false
		for _, n := range m.To {
			if n == f.To {
				found = true
				break
			}
		}
		if !found {
			return false
		}
	}
	if f.Contains != "" && !strings.Contains(m.Text, f.Contains) {
		return false
	}
	if !f.Since.IsZero() && m.At.Before(f.Since) {
		return false
	}
	return true
}

func copyStatus(in map[string]string) map[string]string {
	out := make(map[string]string, len(in))
	for k, v := range in {
		out[k] = v
	}
	return out
}

func copyStuck(in map[string]bool) map[string]bool {
	if in == nil {
		return nil
	}
	out := make(map[string]bool, len(in))
	for k, v := range in {
		out[k] = v
	}
	return out
}
