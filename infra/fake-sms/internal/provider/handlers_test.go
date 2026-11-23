package provider

import (
	"bytes"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/store"
)

type fakeClock struct{ t time.Time }

func (c *fakeClock) Now() time.Time          { return c.t }
func (c *fakeClock) Advance(d time.Duration) { c.t = c.t.Add(d) }

type harness struct {
	clk   *fakeClock
	store *store.Store
	srv   http.Handler
}

func newHarness(t *testing.T) *harness {
	t.Helper()
	clk := &fakeClock{t: time.Date(2026, 11, 23, 10, 0, 0, 0, time.UTC)}
	st := store.New(5000, clk.Now)
	creds, err := ParseCredentials("dev-key:dev-hash")
	if err != nil {
		t.Fatal(err)
	}
	return &harness{clk: clk, store: st, srv: New(Config{Store: st, Credentials: creds, Now: clk.Now})}
}

func (h *harness) post(t *testing.T, path string, body any) (int, []byte) {
	t.Helper()
	raw, err := json.Marshal(body)
	if err != nil {
		t.Fatal(err)
	}
	req := httptest.NewRequest(http.MethodPost, path, bytes.NewReader(raw))
	req.Header.Set("Content-Type", "application/json")
	rec := httptest.NewRecorder()
	h.srv.ServeHTTP(rec, req)
	res := rec.Result()
	defer res.Body.Close()
	out, _ := io.ReadAll(res.Body)
	return res.StatusCode, out
}

func readFixture(t *testing.T, name string) map[string]any {
	t.Helper()
	raw, err := os.ReadFile("../../testdata/" + name)
	if err != nil {
		t.Fatal(err)
	}
	var v map[string]any
	if err := json.Unmarshal(raw, &v); err != nil {
		t.Fatalf("%s: %v", name, err)
	}
	return v
}

func decodeMap(t *testing.T, raw []byte) map[string]any {
	t.Helper()
	var v map[string]any
	if err := json.Unmarshal(raw, &v); err != nil {
		t.Fatalf("yanıt JSON değil: %v (%s)", err, raw)
	}
	return v
}

// sameShape anahtar kümelerini ve değer türlerini karşılaştırır, değerlerin kendisini değil.
func sameShape(a, b any) bool {
	switch av := a.(type) {
	case map[string]any:
		bv, ok := b.(map[string]any)
		if !ok || len(av) != len(bv) {
			return false
		}
		for k, v := range av {
			w, ok := bv[k]
			if !ok || !sameShape(v, w) {
				return false
			}
		}
		return true
	case []any:
		bv, ok := b.([]any)
		if !ok {
			return false
		}
		if len(av) == 0 || len(bv) == 0 {
			return len(av) == len(bv)
		}
		return sameShape(av[0], bv[0])
	default:
		return reflect.TypeOf(a) == reflect.TypeOf(b)
	}
}

func setOrderID(req map[string]any, id string) {
	req["request"].(map[string]any)["order"].(map[string]any)["id"] = id
}

func setRecipient(req map[string]any, num string) {
	req["request"].(map[string]any)["order"].(map[string]any)["message"].(map[string]any)["receipents"].(map[string]any)["number"] = []any{num}
}

func TestFixtures_RoundTrip(t *testing.T) {
	h := newHarness(t)

	code, body := h.post(t, "/v1/send-sms/json", readFixture(t, "send-request.json"))
	if code != 200 {
		t.Fatalf("send-request.json kabul edilmedi: %d %s", code, body)
	}
	sent := decodeMap(t, body)
	if !sameShape(sent, readFixture(t, "send-response.json")) {
		t.Fatalf("send yanıtı fixture şekliyle uyuşmuyor: %s", body)
	}
	orderID := sent["response"].(map[string]any)["order"].(map[string]any)["id"].(string)

	h.clk.Advance(2 * time.Second)
	rep := readFixture(t, "report-request.json")
	setOrderID(rep, orderID)
	code, body = h.post(t, "/v1/get-report/json", rep)
	if code != 200 {
		t.Fatalf("report-request.json kabul edilmedi: %d %s", code, body)
	}
	if !sameShape(decodeMap(t, body), readFixture(t, "report-response.json")) {
		t.Fatalf("report yanıtı fixture şekliyle uyuşmuyor: %s", body)
	}

	code, body = h.post(t, "/v1/get-balance/json", readFixture(t, "balance-request.json"))
	if code != 200 {
		t.Fatalf("balance-request.json kabul edilmedi: %d", code)
	}
	if !sameShape(decodeMap(t, body), readFixture(t, "balance-response.json")) {
		t.Fatalf("balance yanıtı fixture şekliyle uyuşmuyor: %s", body)
	}

	bad := readFixture(t, "send-request.json")
	bad["request"].(map[string]any)["authentication"] = map[string]any{"key": "dev-key", "hash": "yanlis"}
	code, body = h.post(t, "/v1/send-sms/json", bad)
	if code != 401 || !reflect.DeepEqual(decodeMap(t, body), readFixture(t, "auth-error-response.json")) {
		t.Fatalf("auth hatası fixture ile uyuşmuyor: %d %s", code, body)
	}

	insuf := readFixture(t, "send-request.json")
	setRecipient(insuf, "905321239999")
	code, body = h.post(t, "/v1/send-sms/json", insuf)
	if code != 200 || !reflect.DeepEqual(decodeMap(t, body), readFixture(t, "insufficient-balance-response.json")) {
		t.Fatalf("bakiye hatası fixture ile uyuşmuyor: %d %s", code, body)
	}
}

func TestSend_TurkishTextPreserved(t *testing.T) {
	h := newHarness(t)
	text := "Sayın Şükrü, ödemeniz ığüşöç İĞÜŞÖÇ"
	req := readFixture(t, "send-request.json")
	req["request"].(map[string]any)["order"].(map[string]any)["message"].(map[string]any)["text"] = text
	code, body := h.post(t, "/v1/send-sms/json", req)
	if code != 200 {
		t.Fatalf("gönderim başarısız: %d %s", code, body)
	}
	all := h.store.List(store.Filter{})
	if len(all) != 1 || all[0].Text != text {
		t.Fatalf("metin bozuldu: %+v", all)
	}
}

func TestSend_BadCredentials_ProviderAuthError(t *testing.T) {
	h := newHarness(t)
	req := readFixture(t, "send-request.json")
	req["request"].(map[string]any)["authentication"] = map[string]any{"key": "bilinmeyen", "hash": "dev-hash"}
	code, body := h.post(t, "/v1/send-sms/json", req)
	if code != 401 || !reflect.DeepEqual(decodeMap(t, body), readFixture(t, "auth-error-response.json")) {
		t.Fatalf("yanlış anahtar hata vermedi: %d %s", code, body)
	}
	if len(h.store.List(store.Filter{})) != 0 {
		t.Fatal("yetkisiz istek mesaj kaydetti")
	}
}

func TestFailureInjection_ByNumberSuffix(t *testing.T) {
	h := newHarness(t)
	sendTo := func(num string) (int, string) {
		t.Helper()
		req := readFixture(t, "send-request.json")
		setRecipient(req, num)
		code, body := h.post(t, "/v1/send-sms/json", req)
		order, ok := decodeMap(t, body)["response"].(map[string]any)["order"].(map[string]any)
		if !ok {
			return code, ""
		}
		return code, order["id"].(string)
	}
	reportStatus := func(orderID, num string) float64 {
		t.Helper()
		rep := readFixture(t, "report-request.json")
		setOrderID(rep, orderID)
		_, body := h.post(t, "/v1/get-report/json", rep)
		items := decodeMap(t, body)["response"].(map[string]any)["order"].(map[string]any)["message"].([]any)
		for _, it := range items {
			m := it.(map[string]any)
			if m["number"] == num {
				return m["status"].(float64)
			}
		}
		t.Fatalf("%s için rapor satırı yok", num)
		return 0
	}

	_, failedID := sendTo("905321230000")
	if got := reportStatus(failedID, "+905321230000"); got != 112 {
		t.Fatalf("0000 için rapor kodu %v", got)
	}
	if _, id := sendTo("905321239999"); id != "" {
		t.Fatal("9999 için sipariş oluştu, bakiye hatası bekleniyordu")
	}
	_, stuckID := sendTo("905321235555")
	h.clk.Advance(time.Hour)
	if got := reportStatus(stuckID, "+905321235555"); got != 110 {
		t.Fatalf("5555 için rapor kodu %v", got)
	}
	_, okID := sendTo("905321234567")
	if got := reportStatus(okID, "+905321234567"); got != 110 {
		t.Fatalf("normal numara hemen 110 değil: %v", got)
	}
	h.clk.Advance(time.Second)
	if got := reportStatus(okID, "+905321234567"); got != 111 {
		t.Fatalf("normal numara 1 s sonra 111 değil: %v", got)
	}
}

func TestReport_DeliveredAfterDelay(t *testing.T) {
	h := newHarness(t)
	_, body := h.post(t, "/v1/send-sms/json", readFixture(t, "send-request.json"))
	orderID := decodeMap(t, body)["response"].(map[string]any)["order"].(map[string]any)["id"].(string)

	rep := func() map[string]any {
		r := readFixture(t, "report-request.json")
		setOrderID(r, orderID)
		_, b := h.post(t, "/v1/get-report/json", r)
		return decodeMap(t, b)["response"].(map[string]any)["order"].(map[string]any)
	}
	if o := rep(); o["status"].(float64) != 113 || o["waiting"].(float64) != 1 {
		t.Fatalf("gönderim sürerken sipariş durumu: %v", o)
	}
	h.clk.Advance(999 * time.Millisecond)
	if o := rep(); o["delivered"].(float64) != 0 {
		t.Fatal("1 s dolmadan iletildi")
	}
	h.clk.Advance(time.Millisecond)
	o := rep()
	if o["status"].(float64) != 114 || o["delivered"].(float64) != 1 || o["waiting"].(float64) != 0 {
		t.Fatalf("1 s sonra sipariş durumu: %v", o)
	}
}

func TestReport_UnknownOrder(t *testing.T) {
	h := newHarness(t)
	r := readFixture(t, "report-request.json")
	setOrderID(r, "999")
	code, body := h.post(t, "/v1/get-report/json", r)
	if code != 200 || !strings.Contains(string(body), "455") {
		t.Fatalf("bilinmeyen sipariş için 455 bekleniyordu: %d %s", code, body)
	}
}

func TestBalance_ReturnsFixtureShape(t *testing.T) {
	h := newHarness(t)
	code, body := h.post(t, "/v1/get-balance/json", readFixture(t, "balance-request.json"))
	if code != 200 {
		t.Fatalf("bakiye HTTP %d", code)
	}
	if !sameShape(decodeMap(t, body), readFixture(t, "balance-response.json")) {
		t.Fatalf("bakiye yanıtı fixture şekliyle uyuşmuyor: %s", body)
	}
}

func TestParseCredentials(t *testing.T) {
	creds, err := ParseCredentials("a:1, b:2")
	if err != nil || creds["a"] != "1" || creds["b"] != "2" {
		t.Fatalf("çoklu kimlik bilgisi ayrıştırılamadı: %v %v", creds, err)
	}
	for _, bad := range []string{"", "anahtarsiz", ":hash", "key:"} {
		if _, err := ParseCredentials(bad); err == nil {
			t.Fatalf("%q için hata bekleniyordu", bad)
		}
	}
}

func TestDecode_RejectsUnknownFields(t *testing.T) {
	h := newHarness(t)
	req := readFixture(t, "send-request.json")
	req["request"].(map[string]any)["order"].(map[string]any)["bilinmeyen"] = "x"
	code, _ := h.post(t, "/v1/send-sms/json", req)
	if code != 400 {
		t.Fatalf("bilinmeyen alan için HTTP %d, 400 bekleniyordu", code)
	}
}

func TestReport_AcceptsOptionalPageAndRowCount(t *testing.T) {
	h := newHarness(t)
	_, body := h.post(t, "/v1/send-sms/json", readFixture(t, "send-request.json"))
	orderID := decodeMap(t, body)["response"].(map[string]any)["order"].(map[string]any)["id"].(string)
	r := readFixture(t, "report-request.json")
	setOrderID(r, orderID)
	if code, body := h.post(t, "/v1/get-report/json", r); code != 200 {
		t.Fatalf("page/rowCount ile rapor reddedildi: %d %s", code, body)
	}
}
