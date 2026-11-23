# Sözleşmeler (contracts)

Servisler arası ortak sözleşme belgeleri. Python/Go servisleri bu klasörü esas alır.

## Oturum kaydı v1 (`session-v1.example.json`)

Oturum kaydı Valkey'de `sess:{sha256_hex(sid)}` anahtarında JSON olarak tutulur; alan adları `snake_case`'tir.
Tarayıcıda yalnızca `modaryo_sid` cookie'si vardır. Kaydı yalnızca kimlik servisi yazar; diğer servisler okur.
Örnek kayıt: `session-v1.example.json`. Eski/tam örnekler: `fixtures/`.

| Alan | Anlam | Yoksa varsayılan |
|---|---|---|
| `v` | Sözleşme sürümü (şimdilik `1`) | zorunlu |
| `id` | Oturum kimliği (sid'den türetilmez, günlüklerde kullanılabilir) | zorunlu |
| `user_id` | Kullanıcı UUID | zorunlu |
| `email`, `full_name` | Kullanıcı bilgileri | zorunlu |
| `tenant_id` | Aktif firma; `null` ise firmasız oturum | `null` |
| `permissions` | İzin kodları; `"*"` her izni karşılar | zorunlu |
| `remember_me` | Beni hatırla | `false` |
| `created_at`, `absolute_expires_at` | Unix saniye | zorunlu |
| `idle_timeout_s` | Boşta kalma süresi (sn) | zorunlu |
| `ip`, `user_agent` | İstemci bilgisi | `null` |
| `user_type` | `employee`, `dealer` veya `self_service` | `employee` |
| `cari_id` | Bayi/self-servis oturumunda bağlı cari | `null` |
| `modules` | Firmada açık modül anahtarları (yalnızca anahtarı olan açılır/kapanır modüller) | `[]` |
| `branch_ids` | Erişilebilen şubeler | `null` (tüm şubeler) |
| `max_discount_pct` | Uygulanabilecek en yüksek iskonto yüzdesi | `0` |

### Kurallar

- **Şubeler:** `branch_ids: null` tüm şubeler, `[]` hiçbir şube demektir. Okuyucu kütüphane `null` değerini `branch` claim'i olarak `*` ile ifade eder.
- **Kullanıcı türleri:** yalnızca `employee`, `dealer`, `self_service` bilinir. Bilinmeyen tür her zaman reddedilir (401). Varsayılan olarak yalnızca `employee` kabul edilir; bayi ve self-servis oturumlarını kabul etmek servisin kendi açık seçimidir.
- **Modüller yalnızca anahtarlardır:** `modules` içinde sadece açılıp kapanabilen modüllerin anahtarları (örn. `pos`, `sube`) bulunur. Çekirdek özelliklerin (cari vb.) anahtarı yoktur; kayıtta görünmezler.
- **İzinler modüllere göre süzülmez:** kapalı bir modülün izinleri `permissions` içinde kalabilir. Bu yüzden bir servis hem `HasModule(...)` hem de izin kontrolünü birlikte yapmalıdır.
- **Sahip iskontosu:** Sahip rolü her zaman `max_discount_pct: 100` taşır (rol düzenlenemez).
- **Yetki yenileme:** yetki paketi yenilemesi en iyi çabayla "oku-hesapla-yaz" biçimindedir. Rol düzenlemesiyle yarışan bir giriş/firma değişikliği, bir sonraki yenilemeye kadar eski paketi taşıyabilir (bilinen sınırlama).

## İç API (internal-api v1)

Servisten servise (Go/Python → ERP) imzalı HTTP çağrıları. Yol öneki `/internal/*`; Next.js üzerinden asla yönlendirilmez.
Oturum ve CSRF başlığı kuralından muaftır, oturum cookie'siyle erişilemez. Bilinen test vektörü: `fixtures/internal-signature-v1.json`
(`secret`, `method`, `pathAndQuery`, `timestamp`, `tenant`, `idempotencyKey`, `body` girdileri; `bodySha256Hex`, `canonicalString`, `signatureHex` beklenen değerler).
Kütüphane: `libs/dotnet/Modaryo.Internal`; Go uygulaması aynı vektörü kullanır. Vektör dosyası bilerek BOM'suz UTF-8'dir (Go'nun JSON çözücüsü BOM'u reddeder); düzenlerken BOM eklemeyin.

### Başlıklar

| Başlık | Anlam |
|---|---|
| `X-Modaryo-Service` | Çağıran servis adı (örn. `einvoice`); zorunlu |
| `X-Modaryo-Timestamp` | Unix saniye (UTC); zorunlu |
| `X-Modaryo-Signature` | Küçük harfli onaltılık HMAC-SHA256; zorunlu |
| `X-Modaryo-Tenant` | Firma UUID; `/internal/ping` dışındaki tüm yollarda zorunlu. İmzaya dahildir |
| `Idempotency-Key` | GET/HEAD dışındaki tüm isteklerde zorunlu (en çok 200 karakter). İmzaya dahildir |

### İmza

```
signature = hex( HMAC-SHA256( secret, canonical ) )
canonical = "{METHOD}\n{pathAndQuery}\n{timestamp}\n{tenant}\n{idempotencyKey}\n{sha256hex(body)}"
```

- `METHOD` büyük harfli (`GET`, `POST`...). `pathAndQuery` istemcinin gönderdiği ham yol + sorgu dizesidir (örn. `/internal/ping?x=1`); sunucu da ham istek hedefini kullanır.
- `tenant`: `X-Modaryo-Tenant` değeri, başlık yoksa boş metin. Böylece tenant başlığı değiştirilemez.
- `idempotencyKey`: `Idempotency-Key` başlığı değeri, başlık yoksa (örn. GET) boş metin. Böylece anahtar da değiştirilemez.
- `sha256hex(body)`: istek gövdesinin (ham bayt, gövde yoksa boş) küçük harfli onaltılık SHA-256'sı.
- Gövde en çok 1 MiB olabilir (`Internal:MaxBodyBytes`); aşılırsa `413 internal_body_too_large`. Başlıkları eksik, bilinmeyen çağıranlı veya zaman damgası eski istekte gövde okunmadan 401 döner.
- `secret`, HMAC anahtarı olarak ham UTF-8 baytlarıyla kullanılır (onaltılık/base64 çözülmez).
- Sunucu imzayı sabit zamanlı karşılaştırır. Zaman sapması: en çok ±300 sn (`Internal:MaxSkewSeconds`).

### Yapılandırma (ERP)

`Internal:Callers:{ad}:Secret` ve `Internal:Callers:{ad}:Routes` (izinli yol önekleri, örn. `/internal/einvoice/`; `/internal/ping` ayrıca verilir).
Geliştirme sırları yalnızca `appsettings.Development.json` içindedir; kodda sır bulunmaz.

### Idempotency

- Yazma isteği (GET/HEAD dışı) `Idempotency-Key` taşımalıdır. Kayıt anahtarı `(çağıran, Idempotency-Key)`'dir.
- İstek özeti = SHA-256(`method \n pathAndQuery \n tenant \n body`). İstek çalışmadan önce bekleyen kayıt, bir sahiplik belirteciyle birlikte eklenir; tamamlama ve serbest bırakma yalnızca bu belirteç hâlâ kaydın sahibiyse etki eder.
- Aynı anahtar + aynı özet, tamamlanmışsa: kayıtlı durum ve gövde `Content-Type: application/json` ile aynen döner (istek yeniden çalıştırılmaz). Diğer yanıt başlıkları (örn. `Location`) yeniden oynatılmaz.
- Aynı anahtar + farklı özet: `409 idempotency_conflict`. Aynı anahtar + aynı özet ama istek hâlâ işleniyor: `409 idempotency_in_progress` ve `Retry-After: 1` başlığı; istemci kısa süre sonra aynı isteği tekrarlar.
- Bekleyen kayıt 5 dakikadan (`Internal:PendingTimeoutSeconds`) eskiyse (işleyen süreç çökmüş olabilir) aynı çağıran, anahtar ve özetle gelen yeni istek kaydı devralır (yeni sahiplik belirteci); eski sahibin geç gelen tamamlama/silmesi yeni kaydı etkilemez. Daha tazeyse `409 idempotency_in_progress`.
- Yalnızca uç noktanın kendi yazdığı, durumu < 500 olan yanıtlar saklanır. İstisnayla üretilen hata yanıtları (doğrulama 400, 404, iş kuralı 409, yetkilendirmeden 401/403) middleware'in dışındaki istisna işleyicisinden döner; bu durumda kayıt silinir ve aynı anahtarla yeniden deneme isteği yeniden çalıştırır. Uç nokta 5xx döndürürse veya istisna fırlatırsa kayıt silinir.
- Uç nokta < 500 ile döndükten sonra kayıt asla silinmez; tamamlama başarısız olursa kayıt bekleyen kalır (uyarı günlüğe yazılır) ve zaman aşımından sonra devralınabilir.
- Yeniden deneme kuralları: 5xx, ağ hatası veya zaman aşımında aynı anahtar ve aynı gövdeyle yeniden denenir. En-az-bir-kez penceresi: işleyici işlemi kaydettikten sonra ama kayıt tamamlanmadan süreç çökerse, anahtar zaman aşımı sonunda devralınır ve istek yeniden çalışabilir. Bu yüzden yazma işleyicileri, tamamlama sonraki bir planda atomik yapılana dek kendi başlarına idempotent olmalıdır.
- Kimlik servisi iç yazma isteği kabul etmez (idempotency deposu yoktur): imzalı GET/HEAD dışı istek `405 internal_method_not_allowed` alır.
- Kayıtlar 7 gün sonra silinir.

### Hatalar (ProblemDetails, `code` alanı)

| Durum | `code` | Ne zaman |
|---|---|---|
| 401 | `internal_unauthorized` | Başlık eksik/geçersiz, bilinmeyen çağıran, imza uyuşmuyor, zaman sapması aşıldı (hangisi olduğu belirtilmez) |
| 403 | `internal_forbidden` | Çağıran doğrulandı ama yol izinli öneklerinden birinde değil |
| 400 | `internal_tenant_invalid` | `X-Modaryo-Tenant` eksik (ping hariç) veya geçerli UUID değil |
| 400 | `idempotency_key_required` | Yazma isteğinde `Idempotency-Key` yok |
| 413 | `internal_body_too_large` | Gövde `MaxBodyBytes` sınırını aşıyor (Content-Length veya okunan bayt) |
| 409 | `idempotency_conflict` | Anahtar farklı bir gövde/istek özetiyle kullanılmış |
| 409 | `idempotency_in_progress` | Aynı anahtar ve aynı istek hâlâ işleniyor; `Retry-After: 1` başlığı döner |
| 405 | `internal_method_not_allowed` | Servis iç yazma isteği kabul etmiyor (kimlik servisi) |
| 503 | `internal_unavailable` | Idempotency deposu yanıt vermedi (Go servisleri; .NET 500 döner). Aynı anahtarla yeniden denenebilir |
| 400 | `internal_bad_path` | `/internal` yolu normalleştirilmiş biçimde değil (`//`, `.`/`..` parçaları); Go servisleri |

### Kimlik servisi ↔ ERP uçları (şubeler)

Şubeler kimlik servisinde yaşar. Servisler birbirini imzalı iç API ile çağırır; her yön için ayrı sır kullanılır
(giden istemci: `Internal:Outbound:{Hedef}:{BaseUrl,ServiceName,Secret}`; gelen çağıran: `Internal:Callers:{ad}`).

| Çağıran → Hedef | Uç | Yanıt |
|---|---|---|
| `erp` → kimlik | `GET /internal/tenants/{tenantId}/branches` | `[{id, code, name, isActive, isDefault}]`, aktif ve pasif tüm şubeler. Yoldaki `tenantId` imzalı `X-Modaryo-Tenant` ile aynı olmalıdır; değilse `403 internal_forbidden`. ERP sonucu firma başına 60 sn önbelleğe alır |
| `identity` → ERP | `GET /internal/branches/{branchId}/usage` | `{inUse: bool}`; firma `X-Modaryo-Tenant` başlığından gelir. Kayıtlı herhangi bir `IBranchUsageProvider` true derse `inUse` true; sağlayıcı yoksa false |

Kimlik servisi şubeyi silmeden önce ERP'ye sorar (5 sn zaman aşımı): `inUse` true ise `409 branch_in_use`; ERP'ye ulaşılamaz, zaman aşımına uğrar,
2xx dışı veya bozuk yanıt dönerse `503 erp_unavailable` (güvenli tarafta kalır, şube silinmez).

### Kimlik

İç istekte kimlik yalnızca `service` ve `tenant_id` claim'leridir: kullanıcı, izin ve modül yoktur; ERP'de denetim kaydının aktörü `sistem:{servis}` olur.

## Olaylar (event v1)

ERP, iş değişikliğiyle aynı veritabanı işleminde bir outbox tablosuna olay yazar (transactional outbox); arka plan rölesi bunları Valkey akışına iletir.
Go/Python servisleri (bildirim, e-fatura) bu akışı okur. Örnek zarf: `event-v1.example.json`.

### Akış ve girdi alanları

- Akış anahtarı: `erp.events` (Valkey DB'si ERP'nin kullandığı bağlantıdaki DB'dir; üretimde 0).
- Her olay `XADD erp.events MAXLEN ~ 100000 * ...` ile eklenir (yaklaşık kırpma; ~100 bin girdi tutulur).
- Her akış girdisinin üç alanı vardır:

| Alan | Anlam |
|---|---|
| `type` | Olay türü (zarftaki `type` ile aynı); tüketici JSON ayrıştırmadan süzebilir |
| `tenant_id` | Firma UUID (zarftaki ile aynı) |
| `event` | Tam zarf (aşağıdaki JSON), UTF-8 metin |

### Zarf (`event` alanı)

| Alan | Anlam |
|---|---|
| `v` | Sözleşme sürümü, şimdilik `1` |
| `id` | Olay kimliği (UUIDv7); outbox kaydının kimliğidir ve yeniden iletimde değişmez |
| `type` | `kebab.case` olay türü, örn. `current-account.entry-added` |
| `tenant_id` | Olayın ait olduğu firma (UUID) |
| `occurred_at` | Olayın oluştuğu an, ISO-8601 UTC (`2026-11-22T21:30:15.123Z`) |
| `actor` | Olayı tetikleyen: kullanıcının tam adı; iç API çağrılarında `sistem:{servis}`; yalnızca ad hiç yoksa yedek olarak çıplak `sistem` |
| `data` | Olaya özgü gövde; anahtarlar `snake_case` |

### İletim güvencesi: en az bir kez (at-least-once)

Röle, bekleyen kayıtları `Id` sırasıyla (en çok 100'lük partiler) iletir ve ardından kaydı "iletildi" olarak işaretler.
XADD ile işaretleme arasında süreç ölürse aynı olay akışa **yeniden** yazılabilir. Tüketiciler zarf `id` alanıyla tekilleştirmelidir.
Röle kayıtları yaklaşık oluşturulma sırasıyla iletir (`id` UUIDv7'dir ve kayıt oluşturulurken atanır, işlem onayında değil; eşzamanlı işlemler araya girebilir). Tüketiciler kesin sıraya güvenmemelidir. Birden çok röle örneği aynı kaydı aynı anda iletmez (`FOR UPDATE SKIP LOCKED`), ancak örnekler arasında sıra garantisi yoktur.
Valkey erişilemezken olaylar bekler; röle 1 sn, 2 sn, 4 sn ... en çok 60 sn aralıkla yeniden dener.

### `current-account.entry-added`

Cari hesaba her hareket eklendiğinde (hareket girişi ve kart açılışındaki açılış bakiyesi dahil; hareket başına bir olay) yayılır.

| `data` alanı | Tür | Anlam |
|---|---|---|
| `current_account_id` | UUID | Cari |
| `entry_id` | UUID | Hareket |
| `entry_type` | string (`opening`, `debit-note` veya `credit-note`) | Hareketin türü: açılış bakiyesi, borç dekontu, alacak dekontu |
| `kind` | string (`debit` veya `credit`) | Yalnızca hareketin yönü (açılış bakiyesi dahil her hareket borç ya da alacaktır) |
| `amount` | string | Tutar, her zaman 2 ondalıklı ve nokta ayraçlı (`"250.50"`); kayan nokta kaybı olmasın diye metindir |
| `currency` | string | Para birimi (`TRY`, `USD`, `EUR`, `GBP`) |
| `date` | string | Hareket tarihi, `yyyy-MM-dd` |
| `due_date` | string veya `null` | Vade tarihi, `yyyy-MM-dd` |
| `description` | string veya `null` | Açıklama |

## Hata biçimi (ProblemDetails)

Tüm servisler (.NET ve Go) hataları aynı gövdeyle döndürür. İçerik türü `application/problem+json`; Türkçe başlıklar kullanıcıya olduğu gibi gösterilir (frontend alan hatası yoksa `title`'ı gösterir, `code` ile dallanır).

| Alan | Zorunlu | Açıklama |
|---|---|---|
| `type` | hayır | RFC 9110 bölüm bağlantısı (400/401/403/404 için) |
| `title` | evet | Türkçe, kullanıcıya gösterilebilir kısa başlık |
| `status` | evet | HTTP durum kodu |
| `detail` | hayır | Ek açıklama |
| `code` | hayır | Makine tarafından okunur kod (üst düzey alan) |
| `errors` | hayır | Alan adı → ileti listesi (yalnızca `validation_failed`) |

`traceId` ve `instance` değişkendir; karşılaştırmalarda yok sayılır ve örneklerde yer almaz.

| Durum | `code` | `title` | `type` bölümü |
|---|---|---|---|
| 400 | `validation_failed` | Formdaki hataları düzeltin. | 15.5.1 |
| 400 | `bad_request` | İstek okunamadı. Gönderilen verinin biçimini kontrol edin. | 15.5.1 |
| 401 | `unauthorized` | Oturum açmanız gerekiyor. | 15.5.2 |
| 403 | `forbidden` | Bu işlem için yetkiniz yok. | 15.5.4 |
| 403 | `tenant_required` | Önce bir firma seçin. | 15.5.4 |
| 403 | `csrf_header_missing` | İstek reddedildi. | 15.5.4 |
| 404 | `not_found` | Kayıt bulunamadı. | 15.5.5 |
| 503 | `session_store_unavailable` | Oturum servisine şu anda ulaşılamıyor. | yok |

`tenant_required`: oturumda aktif firma yoksa Go servisleri bunu döndürür; ERP şimdilik aynı durumda 403 `forbidden` döner ve ileride bu koda geçecektir. `csrf_header_missing` gövdesi `fixtures/problem-csrf.json` ile aynıdır.

İş kuralı hataları (`AppException`) `type` taşımaz: `{ title, status, code }` (örn. 403 `module_disabled`, 409 `period_locked`).

Gerçek ERP yanıtlarından alınmış örnekler: `fixtures/problem-validation.json`, `problem-unauthorized.json`, `problem-forbidden.json`, `problem-not-found.json`, `problem-app.json`, `problem-csrf.json`. ERP testi (`ProblemFixtureTests`) yanıtları bu dosyalarla, Go testi (`Problem_JsonShapeMatchesDotnetFixture`) kendi gövdelerini bu dosyalarla karşılaştırır. Yeniden üretmek için ERP işlevsel testlerini `MODARYO_WRITE_FIXTURES=1` ile çalıştırın.
