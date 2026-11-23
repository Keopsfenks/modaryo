// Command fake-sms, geliştirme ve test ortamı için sahte bir SMS sunucusudur.
// İleti Merkezi JSON API'sini taklit eder ve gönderilen mesajları bellekte tutar.
package main

import (
	"log"
	"net/http"
	"os"
	"time"

	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/provider"
	"github.com/keopsfenks/modaryo/infra/fake-sms/internal/store"
)

const storeCapacity = 5000

func main() {
	creds, err := provider.ParseCredentials(os.Getenv("FAKE_SMS_CREDENTIALS"))
	if err != nil {
		log.Fatalf("FAKE_SMS_CREDENTIALS: %v", err)
	}
	apiAddr := envOr("FAKE_SMS_API_ADDR", ":8027")

	st := store.New(storeCapacity, time.Now)
	api := &http.Server{
		Addr:              apiAddr,
		Handler:           provider.New(provider.Config{Store: st, Credentials: creds, Now: time.Now}),
		ReadHeaderTimeout: 5 * time.Second,
	}
	log.Printf("sağlayıcı API'si dinliyor: %s", apiAddr)
	log.Fatal(api.ListenAndServe())
}

func envOr(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}
