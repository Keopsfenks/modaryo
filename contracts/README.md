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
