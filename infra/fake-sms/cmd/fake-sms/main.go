// Command fake-sms, geliştirme ve test ortamı için sahte bir SMS sunucusudur.
// İleti Merkezi JSON API'sini taklit eder ve gönderilen mesajları bellekte tutar.
//
// Kullanım:
//
//	fake-sms               sunucuyu başlatır (UI + test API, sağlayıcı API)
//	fake-sms -healthcheck  çalışan sunucunun sağlık uç noktasını yoklar (çıkış kodu 0/1)
package main

import (
	"flag"
	"fmt"
	"log"
	"net"
	"net/http"
	"os"
	"time"

	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/provider"
	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/store"
	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/web"
)

const (
	storeCapacity = 5000
	defaultUIAddr = ":8026"
)

func main() {
	healthcheck := flag.Bool("healthcheck", false, "çalışan sunucunun sağlık uç noktasını yokla ve çık")
	flag.Parse()

	if *healthcheck {
		os.Exit(runHealthcheck(envOr("FAKE_SMS_UI_ADDR", defaultUIAddr)))
	}

	creds, err := provider.ParseCredentials(os.Getenv("FAKE_SMS_CREDENTIALS"))
	if err != nil {
		log.Fatalf("FAKE_SMS_CREDENTIALS: %v", err)
	}
	uiAddr := envOr("FAKE_SMS_UI_ADDR", defaultUIAddr)
	apiAddr := envOr("FAKE_SMS_API_ADDR", ":8027")

	st := store.New(storeCapacity, time.Now)
	ui := newServer(uiAddr, web.New(st))
	api := newServer(apiAddr, provider.New(provider.Config{Store: st, Credentials: creds, Now: time.Now}))

	go func() {
		log.Printf("arayüz ve test API'si dinliyor: %s", uiAddr)
		log.Fatal(ui.ListenAndServe())
	}()
	log.Printf("sağlayıcı API'si dinliyor: %s", apiAddr)
	log.Fatal(api.ListenAndServe())
}

func newServer(addr string, h http.Handler) *http.Server {
	return &http.Server{Addr: addr, Handler: h, ReadHeaderTimeout: 5 * time.Second}
}

// runHealthcheck arayüz adresinin port kısmından yerel sağlık uç noktasını yoklar.
func runHealthcheck(uiAddr string) int {
	_, port, err := net.SplitHostPort(uiAddr)
	if err != nil {
		fmt.Fprintln(os.Stderr, "FAKE_SMS_UI_ADDR geçersiz:", err)
		return 1
	}
	client := http.Client{Timeout: 3 * time.Second}
	res, err := client.Get("http://127.0.0.1:" + port + "/health")
	if err != nil {
		fmt.Fprintln(os.Stderr, "sağlık kontrolü başarısız:", err)
		return 1
	}
	defer res.Body.Close()
	if res.StatusCode != http.StatusOK {
		fmt.Fprintln(os.Stderr, "sağlık kontrolü başarısız, durum:", res.Status)
		return 1
	}
	return 0
}

func envOr(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}
