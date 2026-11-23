package web

import (
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/store"
)

type fakeClock struct{ t time.Time }

func (c *fakeClock) Now() time.Time          { return c.t }
func (c *fakeClock) Advance(d time.Duration) { c.t = c.t.Add(d) }

func newTestServer(t *testing.T) (http.Handler, *store.Store, *fakeClock) {
	t.Helper()
	clk := &fakeClock{t: time.Date(2026, 11, 23, 10, 0, 0, 0, time.UTC)}
	st := store.New(100, clk.Now)
	return New(st), st, clk
}

func do(t *testing.T, h http.Handler, method, target string) (int, string) {
	t.Helper()
	req := httptest.NewRequest(method, target, nil)
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, req)
	res := rec.Result()
	defer res.Body.Close()
	b, _ := io.ReadAll(res.Body)
	return res.StatusCode, string(b)
}

func addMsg(st *store.Store, at time.Time, num, text string) store.Message {
	return st.Add(store.Message{
		At:      at,
		Sender:  "MODARYO",
		To:      []string{num},
		Text:    text,
		OrderID: "1",
		Status:  map[string]string{num: "pending"},
	})
}

func TestApi_ListFiltersByToAndContains(t *testing.T) {
	h, st, clk := newTestServer(t)
	addMsg(st, clk.Now(), "+905321111111", "Kodunuz 1234")
	addMsg(st, clk.Now(), "+905322222222", "Kodunuz 1234")
	addMsg(st, clk.Now(), "+905321111111", "Merhaba")

	code, body := do(t, h, http.MethodGet, "/api/messages?to=5321111111&contains=Kodunuz")
	if code != 200 {
		t.Fatalf("HTTP %d: %s", code, body)
	}
	var list []store.Message
	if err := json.Unmarshal([]byte(body), &list); err != nil {
		t.Fatalf("JSON değil: %v", err)
	}
	if len(list) != 1 || list[0].To[0] != "+905321111111" || list[0].Text != "Kodunuz 1234" {
		t.Fatalf("süzgeç yanlış sonuç verdi: %+v", list)
	}

	code, body = do(t, h, http.MethodGet, "/api/messages?to=abc")
	if code != 400 {
		t.Fatalf("geçersiz numara için HTTP %d: %s", code, body)
	}
}

func TestApi_GetByID(t *testing.T) {
	h, st, clk := newTestServer(t)
	m := addMsg(st, clk.Now(), "+905321111111", "x")
	if code, _ := do(t, h, http.MethodGet, "/api/messages/"+m.ID); code != 200 {
		t.Fatalf("var olan mesaj HTTP %d", code)
	}
	if code, _ := do(t, h, http.MethodGet, "/api/messages/yok"); code != 404 {
		t.Fatalf("olmayan mesaj HTTP %d", code)
	}
}

func TestApi_DeleteClears(t *testing.T) {
	h, st, clk := newTestServer(t)
	addMsg(st, clk.Now(), "+905321111111", "x")
	if code, _ := do(t, h, http.MethodDelete, "/api/messages"); code != 204 {
		t.Fatalf("silme HTTP %d", code)
	}
	_, body := do(t, h, http.MethodGet, "/api/messages")
	if strings.TrimSpace(body) != "[]" {
		t.Fatalf("silmeden sonra liste: %s", body)
	}
}

func TestUi_EscapesMessageText(t *testing.T) {
	h, st, clk := newTestServer(t)
	addMsg(st, clk.Now(), "+905321111111", "<script>alert(1)</script>")
	_, body := do(t, h, http.MethodGet, "/")
	if strings.Contains(body, "<script>alert(1)</script>") {
		t.Fatal("mesaj metni kaçışsız render edildi")
	}
	if !strings.Contains(body, "&lt;script&gt;alert(1)&lt;/script&gt;") {
		t.Fatal("kaçışlı mesaj metni sayfada yok")
	}
}

func TestUi_RendersTurkishLabels(t *testing.T) {
	h, _, _ := newTestServer(t)
	_, body := do(t, h, http.MethodGet, "/")
	for _, label := range []string{`lang="tr"`, "Zaman", "Gönderen", "Alıcılar", "Mesaj", "Durum", "Temizle"} {
		if !strings.Contains(body, label) {
			t.Fatalf("sayfada %q yok", label)
		}
	}
	if strings.Contains(body, "src=\"http") || strings.Contains(body, "href=\"http") {
		t.Fatal("sayfa dış kaynak kullanıyor")
	}
}

func TestHealth_Ok(t *testing.T) {
	h, _, _ := newTestServer(t)
	code, body := do(t, h, http.MethodGet, "/health")
	if code != 200 || !strings.Contains(body, `"ok"`) {
		t.Fatalf("sağlık HTTP %d: %s", code, body)
	}
}
