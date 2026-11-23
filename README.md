# Modaryo

Kendi kendine üye olunan, çok kiracılı (SaaS) bir ERP. Mikroservis mimarisinde; servisler .NET, Python ve Go ile yazılır.

| Klasör | İçerik |
|---|---|
| `services/identity` | Kimlik ve firma servisi (.NET) — kayıt, giriş, oturum, firma, rol, davet |
| `services/erp` | ERP iş servisi (.NET) — cari hesaplar (müşteri/tedarikçi, hareketler, ekstre, vade takibi) |
| `libs/dotnet/Modaryo.Sessions` | .NET servislerinin ortak oturum okuma / CSRF / izin kodu |
| `libs/go/modaryo` | Go servislerinin ortak kütüphanesi (`go.work` ile bağlı) |
| `frontend` | Next.js arayüzü |
| `infra` | Yerel altyapı (Docker Compose) |
| `docs` | Tasarım, plan ve sözleşme belgeleri |

## Gereksinimler

- .NET SDK 10
- Node.js (frontend için)
- Docker (WSL içinde)
- Go 1.27.4 (`go.work`: `go 1.27`, `toolchain go1.27.4`) ve staticcheck v0.8.2:
  `go install honnef.co/go/tools/cmd/staticcheck@v0.8.2`

## Altyapı

Docker, WSL (Ubuntu) içinde çalışır. Container'lar Windows'tan `localhost` ile erişilebilir.

| Servis | Adres | Not |
|---|---|---|
| PostgreSQL | `localhost:5432` | kullanıcı `modaryo`, şifre `modaryo_dev`, veritabanları `identity`, `erp` |
| Valkey | `localhost:6379` | oturum deposu |
| Mailpit SMTP | `localhost:1025` | gönderilen tüm e-postaları yakalar |
| Mailpit arayüzü | http://localhost:8025 | yakalanan e-postaları görmek için |
| Sahte SMS arayüzü | http://localhost:8026 | gönderilen SMS'leri yakalar; sağlayıcı API (İleti Merkezi taklidi) `http://localhost:8027` |
| S3 uyumlu dosya deposu (SeaweedFS) | `localhost:8333` | erişim anahtarı `modaryo`, gizli anahtar `modaryo_dev`, bucket `modaryo` (`infra/up.sh` oluşturur) |

> Dosya deposu yalnızca S3 API'sini kullanır; üretimde herhangi bir S3 uyumlu depo kullanılabilir.

> Sahte SMS sağlayıcısı zamanlanmış gönderimleri (ileri tarihli `sendDateTime`) emüle etmez; mesaj hemen kaydedilir ve 1 s sonra iletildi olur.
> Sağlayıcı iş hatalarını HTTP 200 ile döner; adaptör yalnızca `response.status.code` değerine göre dallanmalıdır. Ayrıntılar `infra/fake-sms/testdata/README.md` dosyasında.

> **Not:** WSL boşta kalınca kapanabiliyor ve container'lar onunla iner. Servisleri veya testleri çalıştırmadan önce
> `infra/up.sh` (Git Bash) altyapıyı başlatır ve hazır olana kadar bekler; tekrar çağırmak güvenlidir.

Komutlar (PowerShell veya Git Bash'ten; Git Bash'te başına `MSYS_NO_PATHCONV=1` ekleyin):

```powershell
# başlat
wsl docker compose -f /mnt/c/Users/Gurbuz/Documents/projects/modaryo/infra/docker-compose.yml up -d
# durdur (veriler kalır)
wsl docker compose -f /mnt/c/Users/Gurbuz/Documents/projects/modaryo/infra/docker-compose.yml down
# tamamen sıfırla (veriler silinir)
wsl docker compose -f /mnt/c/Users/Gurbuz/Documents/projects/modaryo/infra/docker-compose.yml down -v
```

## Servisler

| Servis | Adres | API belgesi |
|---|---|---|
| Kimlik (`services/identity`) | http://localhost:5100 | http://localhost:5100/scalar |
| ERP (`services/erp`) | http://localhost:5282 | http://localhost:5282/scalar |
| Frontend (`frontend`) | http://localhost:3000 | — |

```powershell
dotnet run --project services/identity/src/Web --launch-profile http
dotnet run --project services/erp/src/Web --launch-profile http
```

Geliştirme ortamında servisler açılırken migration'ları kendileri uygular. Kimlik servisi bir demo hesap oluşturur:
**demo@modaryo.local / Demo-Modaryo-2026** (sahibi olduğu firma: "Demo Firma").

Oturum kuralları: tüm `GET` dışı isteklerde `X-Modaryo-Csrf: 1` başlığı zorunludur; oturum `modaryo_sid` cookie'sindedir.
Python/Go servisleri için oturum sözleşmesi ve örnek kayıt: `contracts/README.md`, `contracts/session-v1.example.json`.

## Testler

```bash
infra/up.sh
dotnet test libs/dotnet/Modaryo.Libs.slnx
dotnet test services/identity/IdentityService.slnx
dotnet test services/erp/ERP.slnx
bash scripts/go-check.sh   # gofmt, go vet, staticcheck, go test
```

Testler compose'daki PostgreSQL'de geçici veritabanları açıp sonunda siler. Valkey veritabanları test takımlarına şöyle ayrılmıştır
(birbirini silmesinler diye; `FLUSHDB` yalnızca tek sahibi olan DB'de kullanılır):

| Valkey DB | Kullanan |
|---|---|
| 1 | .NET kütüphaneleri (`libs/dotnet/Modaryo.Libs.slnx`) |
| 2 | Kimlik servisi (`services/identity`) |
| 3 | ERP (`services/erp`) |
| 4 | Go kütüphanesi paketleri, ortak (`libs/go/modaryo`: session, streams…); rastgele anahtar adlarıyla, `FLUSHDB` yok |
| 5 | Go `testinfra` paketinin kendi testleri |
| 6+ | Yeni Go servisleri; her biri kendi DB'sini seçer ve tek başına kullanır |

## Frontend

Next.js (`frontend/`, http://localhost:3000). Tarayıcı yalnızca bu adresle konuşur; `/api/identity/*` ve `/api/erp/*`
istekleri Next.js tarafından servislere aktarılır (`IDENTITY_API_URL`, `ERP_API_URL` ortam değişkenleriyle değiştirilebilir).

| Sayfa | Açıklama |
|---|---|
| `/giris`, `/kayit` | Giriş ve kayıt |
| `/sifremi-unuttum`, `/sifre-sifirla` | Şifre sıfırlama (bağlantı e-postayla gelir) |
| `/eposta-dogrula` | E-postadaki doğrulama bağlantısı |
| `/davet/[token]` | Firma daveti (katıl / giriş yap / kayıt ol) |
| `/baslangic` | Firması olmayan kullanıcı: firma oluştur veya bekleyen daveti kabul et |
| `/hesap` | Profil, şifre değiştirme, aktif oturumlar |
| `/firma/uyeler` | Üyeler, roller, davetler, firmadan ayrılma |
| `/finans/cari-hesaplar`, `/satis/musteriler`, `/satin-alma/tedarikciler` | Cari listeleri (tümü / müşteriler / tedarikçiler): arama, bakiye (B/A), vadesi geçmiş tutar, limit aşımı |
| `/cari/yeni`, `/cari/[id]` | Yeni cari; cari detayı (Genel, Adresler, Hareketler, Ekstre, Vade, Açık faturalar) |
| `/finans/vade-takibi` | Para birimi bazında tahsil edilecek / ödenecek özet ve en çok geciken cariler |

Cari izinleri: `cari.read` (görüntüleme), `cari.manage` (kart ve adresler), `cari.movements` (açılış, borç/alacak
dekontu, iptal). Hazır rollerde Sahip hepsine, Yönetici üçüne, Kullanıcı yalnızca `cari.read`'e sahiptir; kimlik servisi
açılışta yeni izinleri mevcut firmaların hazır rollerine ekler.

Çalıştırma sırası: `infra/up.sh` → kimlik servisi → ERP servisi → `npm --prefix frontend run dev`.
Testler: `npm --prefix frontend test` (Vitest + Testing Library + MSW).
