package store

import (
	"testing"
	"time"
)

type fakeClock struct{ t time.Time }

func (c *fakeClock) Now() time.Time          { return c.t }
func (c *fakeClock) Advance(d time.Duration) { c.t = c.t.Add(d) }

func newClock() *fakeClock {
	return &fakeClock{t: time.Date(2026, 11, 23, 10, 0, 0, 0, time.UTC)}
}

func TestStore_CapDropsOldest(t *testing.T) {
	clk := newClock()
	s := New(3, clk.Now)
	var ids []string
	for i := 0; i < 5; i++ {
		m := s.Add(Message{At: clk.Now(), To: []string{"+905321234567"}, Status: map[string]string{"+905321234567": "pending"}})
		ids = append(ids, m.ID)
	}
	got := s.List(Filter{})
	if len(got) != 3 {
		t.Fatalf("kapasite 3 iken %d mesaj var", len(got))
	}
	if _, ok := s.Get(ids[0]); ok {
		t.Fatal("en eski mesaj düşmedi")
	}
	if _, ok := s.Get(ids[1]); ok {
		t.Fatal("ikinci en eski mesaj düşmedi")
	}
	if _, ok := s.Get(ids[4]); !ok {
		t.Fatal("en yeni mesaj kayboldu")
	}
}

func TestStore_ResolvesPendingAfterDelay(t *testing.T) {
	clk := newClock()
	s := New(10, clk.Now)
	num := "+905321234567"
	m := s.Add(Message{At: clk.Now(), To: []string{num}, Status: map[string]string{num: "pending"}})
	clk.Advance(999 * time.Millisecond)
	if got, _ := s.Get(m.ID); got.Status[num] != "pending" {
		t.Fatalf("1 s dolmadan durum %q", got.Status[num])
	}
	clk.Advance(time.Millisecond)
	if got, _ := s.Get(m.ID); got.Status[num] != "delivered" {
		t.Fatalf("1 s sonra durum %q", got.Status[num])
	}
}

func TestStore_StuckStaysPending(t *testing.T) {
	clk := newClock()
	s := New(10, clk.Now)
	num := "+905321234555"
	m := s.Add(Message{At: clk.Now(), To: []string{num}, Status: map[string]string{num: "pending"}, Stuck: map[string]bool{num: true}})
	clk.Advance(time.Hour)
	if got, _ := s.Get(m.ID); got.Status[num] != "pending" {
		t.Fatalf("takılı mesaj %q oldu", got.Status[num])
	}
}

func TestStore_FilterByToContainsSince(t *testing.T) {
	clk := newClock()
	s := New(10, clk.Now)
	s.Add(Message{At: clk.Now(), To: []string{"+905321111111"}, Text: "merhaba dünya", Status: map[string]string{"+905321111111": "delivered"}})
	clk.Advance(time.Minute)
	s.Add(Message{At: clk.Now(), To: []string{"+905322222222"}, Text: "kod 1234", Status: map[string]string{"+905322222222": "delivered"}})

	if n := len(s.List(Filter{To: "+905321111111"})); n != 1 {
		t.Fatalf("to filtresi %d sonuç verdi", n)
	}
	if n := len(s.List(Filter{Contains: "kod"})); n != 1 {
		t.Fatalf("contains filtresi %d sonuç verdi", n)
	}
	if n := len(s.List(Filter{Since: clk.Now()})); n != 1 {
		t.Fatalf("since filtresi %d sonuç verdi", n)
	}
}

func TestStore_Clear(t *testing.T) {
	clk := newClock()
	s := New(10, clk.Now)
	s.Add(Message{At: clk.Now()})
	s.Clear()
	if n := len(s.List(Filter{})); n != 0 {
		t.Fatalf("temizlemeden sonra %d mesaj var", n)
	}
}
