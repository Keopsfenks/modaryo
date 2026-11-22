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
(`secret`, `method`, `pathAndQuery`, `timestamp`, `tenant`, `body` girdileri; `bodySha256Hex`, `canonicalString`, `signatureHex` beklenen değerler).
Kütüphane: `libs/dotnet/Modaryo.Internal`; Go uygulaması aynı vektörü kullanır.

### Başlıklar

| Başlık | Anlam |
|---|---|
| `X-Modaryo-Service` | Çağıran servis adı (örn. `einvoice`); zorunlu |
| `X-Modaryo-Timestamp` | Unix saniye (UTC); zorunlu |
| `X-Modaryo-Signature` | Küçük harfli onaltılık HMAC-SHA256; zorunlu |
| `X-Modaryo-Tenant` | Firma UUID; `/internal/ping` dışındaki tüm yollarda zorunlu. İmzaya dahildir |
| `Idempotency-Key` | GET/HEAD dışındaki tüm isteklerde zorunlu (en çok 200 karakter) |

### İmza

```
signature = hex( HMAC-SHA256( secret, canonical ) )
canonical = "{METHOD}\n{pathAndQuery}\n{timestamp}\n{tenant}\n{sha256hex(body)}"
```

- `METHOD` büyük harfli (`GET`, `POST`...). `pathAndQuery` istemcinin gönderdiği ham yol + sorgu dizesidir (örn. `/internal/ping?x=1`); sunucu da ham istek hedefini kullanır.
- `tenant`: `X-Modaryo-Tenant` değeri, başlık yoksa boş metin. Böylece tenant başlığı değiştirilemez.
- `sha256hex(body)`: istek gövdesinin (ham bayt, gövde yoksa boş) küçük harfli onaltılık SHA-256'sı.
- Sunucu imzayı sabit zamanlı karşılaştırır. Zaman sapması: en çok ±300 sn (`Internal:MaxSkewSeconds`).

### Yapılandırma (ERP)

`Internal:Callers:{ad}:Secret` ve `Internal:Callers:{ad}:Routes` (izinli yol önekleri, örn. `/internal/einvoice/`; `/internal/ping` ayrıca verilir).
Geliştirme sırları yalnızca `appsettings.Development.json` içindedir; kodda sır bulunmaz.

### Idempotency

- Yazma isteği (GET/HEAD dışı) `Idempotency-Key` taşımalıdır. Kayıt anahtarı `(çağıran, Idempotency-Key)`'dir.
- İstek özeti = SHA-256(`method \n pathAndQuery \n tenant \n body`). İstek çalışmadan önce bekleyen kayıt eklenir.
- Aynı anahtar + aynı özet, tamamlanmışsa: kayıtlı durum ve gövde aynen döner (istek yeniden çalıştırılmaz).
- Aynı anahtar + farklı özet: `409 idempotency_conflict`. Aynı anahtar + aynı özet ama istek hâlâ işleniyor: `409 idempotency_conflict` (ileti, işlendiğini söyler).
- Yanıt durumu < 500 ise saklanır (4xx dahil); 5xx veya istisnada kayıt silinir, aynı anahtarla yeniden denenebilir. Kayıtlar 7 gün sonra silinir.

### Hatalar (ProblemDetails, `code` alanı)

| Durum | `code` | Ne zaman |
|---|---|---|
| 401 | `internal_unauthorized` | Başlık eksik/geçersiz, bilinmeyen çağıran, imza uyuşmuyor, zaman sapması aşıldı (hangisi olduğu belirtilmez) |
| 403 | `internal_forbidden` | Çağıran doğrulandı ama yol izinli öneklerinden birinde değil |
| 400 | `internal_tenant_invalid` | `X-Modaryo-Tenant` eksik (ping hariç) veya geçerli UUID değil |
| 400 | `idempotency_key_required` | Yazma isteğinde `Idempotency-Key` yok |
| 409 | `idempotency_conflict` | Anahtar başka bir istekle kullanılmış veya istek hâlâ işleniyor |

### Kimlik

İç istekte kimlik yalnızca `service` ve `tenant_id` claim'leridir: kullanıcı, izin ve modül yoktur; ERP'de denetim kaydının aktörü `sistem:{servis}` olur.
