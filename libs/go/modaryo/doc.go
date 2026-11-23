// Package modaryo, Modaryo'nun Go servisleri (e-fatura, bildirim vb.) için
// ortak kütüphanedir.
//
// Alt paketler; oturum okuma, iç servis çağrısı imzalama ve doğrulama,
// idempotency, olay (event) sözleşmesi ile ortak HTTP ve altyapı yardımcılarını
// içerir. Sözleşmeler contracts/README.md dosyasında tanımlıdır.
//
// # Servis kablolaması (kanonik zincir)
//
// Her Go servisi HTTP zincirini ERP ile aynı biçimde, açıkça kurar. İç API
// ("/internal/*") oturum ve CSRF kuralından muaftır; csrf ve session iç yolları
// kendiliğinden atlamaz, ayrımı yönlendirici yapar:
//
//  1. logx.RequestMiddleware (en dışta): request_id, erişim satırı.
//  2. logx.Recover: panik → 500 application/problem+json (code internal_error);
//     erişim satırı 500 gösterir, panik bir kez günlüğe yazılır.
//  3. Yönlendirici: internalapi.IsInternalPath(r.URL.Path) doğruysa
//     internalapi.Middleware(opts)(internalRouter). Kök yola bağlanır,
//     http.StripPrefix kullanılmaz; internalRouter tam "/internal/..." yolunu görür.
//  4. Diğer her istek: csrf.Middleware(session.Middleware(reader, opts)(tenant.FromSession(app))).
//
// Çalışan örnek: Example_service (example_test.go).
//
//	internal := internalapi.Middleware(internalOpts)(internalRouter)
//	public := csrf.Middleware(session.Middleware(reader, session.Options{})(tenant.FromSession(app)))
//	router := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
//		if internalapi.IsInternalPath(r.URL.Path) {
//			internal.ServeHTTP(w, r)
//			return
//		}
//		public.ServeHTTP(w, r)
//	})
//	handler := logx.RequestMiddleware(logger)(logx.Recover(router))
//
// main, sinyal bağlamını (signal.NotifyContext) bir kez oluşturur; aynı bağlamı
// server.Run'a ve akış tüketicisine (streams.Consumer.Run) verir. Böylece SIGINT/SIGTERM
// ikisini birlikte durdurur:
//
//	func main() {
//		ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
//		defer stop()
//		// ... logger, reader, internalOpts, consumer, handler kurulumu ...
//		done := make(chan struct{})
//		go func() { defer close(done); _ = consumer.Run(ctx) }()
//		err := server.Run(ctx, addr, handler, logger)
//		stop() // sunucu hatayla dönerse tüketici de dursun
//		<-done
//		if err != nil {
//			logger.Error("server", "error", err)
//			os.Exit(1)
//		}
//	}
package modaryo
