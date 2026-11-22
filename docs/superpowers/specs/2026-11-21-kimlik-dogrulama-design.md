# Kimlik Doğrulama ve Firma Yönetimi — Tasarım

- **Tarih:** 2026-11-21
- **Durum:** Onaylandı (2026-11-21)
- **Not:** Repo henüz git deposu değil; bu dosya commit edilmedi.

Etiketler: **[Kullanıcı]** kullanıcının verdiği karar, **[Öneri]** benim verdiğim ve kullanıcının düzeltebileceği karar.

---

## 1. Amaç ve neden

Modaryo, firmaların internetten kendi kendine üye olduğu çok kiracılı (multi-tenant) bir **SaaS ERP**. **[Kullanıcı]**

İlk modül kimlik doğrulama: kayıt, giriş, şifre işlemleri, firma oluşturma ve davet. Sonraki tüm modüller (stok, satış, finans, İK…) "kim, hangi firmada, hangi izinle?" sorusunun cevabına dayanacak. Bu yüzden önce bu temel kuruluyor. Sistem ileride Python ve Go servisleri de içerecek. Bu servislerin oturumu kendi başlarına doğrulayabilmesi tasarımın merkezinde.

**Sıra:** önce backend API, sonra frontend ekranları. **[Kullanıcı]**

## 2. Hedefler, kapsam dışı ve asla olmaması gerekenler

### Hedefler (ilk sürüm)
- Kayıt ol, e-posta doğrulama (yeniden gönder dahil), giriş ("beni hatırla"), çıkış, tüm cihazlardan çıkış.
- Şifremi unuttum ve şifre sıfırlama, şifre değiştirme, aktif oturumları listeleme ve kapatma.
- Çok sayıda yanlış denemede hesap kilidi, istek sınırlama (rate limit).
- Firma oluşturma; davet gönderme, iptal etme, yeniden gönderme ve kabul etme.
- Üyeleri listeleme, rol değiştirme, firmadan çıkarma; firmadan ayrılma.
- İzin tabanlı yetki altyapısı ve her firmada hazır rol seti.
- Firmasız ve doğrulanmamış kullanıcıların otomatik temizlenmesi.
- ERP servisinin, kimlik servisinin oluşturduğu oturumu tanıması.

### Kapsam dışı (sonraki sürümler)
- İki adımlı doğrulama (2FA) ve Google/Microsoft ile giriş. **[Öneri]**
- Gelişmiş çoklu firma ekranları (firma listesi sayfası, firmalar arası özet gibi). İlk sürümde yalnızca profil menüsünde basit bir firma değiştirici olacak. **[Kullanıcı]**
- Rol düzenleme ekranı ve özel rol oluşturma. Altyapı hazır olacak, ekran modüller yazıldıkça gelecek. **[Kullanıcı]**
- E-posta adresi değiştirme, kullanıcının kendi hesabını silmesi, firma sahipliği devri, firma silme. **[Öneri]**
- Abonelik/ödeme, SaaS operatörü için yönetim paneli, güvenlik denetim kaydı (audit log). **[Öneri]**
- API Gateway (YARP). Şimdilik gateway işini Next.js proxy'si yapacak. **[Öneri, kullanıcıyla konuşuldu]**

### Asla olmaması gerekenler
- Bir firmanın kullanıcısının başka bir firmanın verisini görmesi.
- Python ve Go servislerinin okuyamayacağı, .NET'e özel bir oturum biçimi kullanılması.
- Kayıt veya şifremi unuttum ekranının "bu e-posta kayıtlı / kayıtlı değil" bilgisini sızdırması.
- Bir oturumun silindikten sonra geri gelmesi ya da mutlak bitiş zamanından öteye uzaması.

## 3. Kısıtlar

- **Docker:** Docker yalnızca WSL (Ubuntu) içinde var, Windows tarafında yok. PostgreSQL, Valkey ve sahte SMTP orada `docker compose` ile çalışacak. **[Kullanıcı]**
- **Ağ varsayımı (doğrulanmadı):** WSL2'deki container portlarına Windows'tan `localhost` ile erişilebildiği ve `wsl` komutu döndükten sonra container'ların çalışmaya devam ettiği varsayılıyor. Planın ilk adımı bunu doğrulamak.
- **Aspire:** Aspire'ın container kaynakları Windows'tan WSL Docker'ına ulaşamaz. Ayrıca AppHost, güvenilmeyen geliştirme sertifikası yüzünden takılıyor.
- **.NET:** .NET 10 (SDK 10.0.404 sabit) ve Jason Taylor Clean Architecture şablonu (v11.0.0). **[Kullanıcı: şablon]**
- **NuGet:** Kullanıcının genel NuGet kaynağı ölü V2 adresinde. Her servis kendi `nuget.config` dosyasıyla V3 kaynağını kullanmalı.
- **`.editorconfig`:** Kökteki `.editorconfig` kullanıcının dosyası ve tüm repo onu kullanmalı. Şablonun ürettiği `root = true` içeren `.editorconfig` silinmeli.

## 4. Değerlendirilen yaklaşımlar

| # | Yaklaşım | Artı | Eksi |
|---|---|---|---|
| 1 | Kimlik doğrulama ERP servisinin içinde, oturumlar Valkey'de JSON | En az parça | Kimlik ve iş verisi aynı serviste; mikroservis ayrımı zayıf |
| 2 | 1 + önde API Gateway | Temiz yönlendirme | Tek servis varken fazladan parça |
| **3** | **Ayrı kimlik servisi, oturumlar Valkey'de JSON, şimdilik Next.js proxy'si gateway görevi görür** | **Sorumluluklar net ayrılır; Python ve Go aynı sözleşmeyle bağlanır** | **İkinci bir .NET servisi; oturum biçimi iki serviste birebir aynı olmalı** |

Seçilen: **3. yol**. **[Kullanıcı]**

Elenen: ASP.NET'in standart şifreli cookie bileti. .NET'e özel şifreleme nedeniyle Python ve Go bu bileti okuyamaz.

## 5. Genel mimari

```
Tarayıcı ──(aynı köken: localhost:3000)──> Next.js
                                             │  /api/identity/*  ──> identity servisi  (localhost:5100)
                                             │  /api/erp/*       ──> erp servisi       (localhost:5282)
identity servisi ── PostgreSQL (veritabanı: identity)
                 ── Valkey     (oturum yazar / günceller / siler)
                 ── Mailpit    (SMTP 1025, arayüz 8025)
erp servisi      ── PostgreSQL (veritabanı: erp)
                 ── Valkey     (oturum sadece okur, süresini uzatır)
Python / Go servisleri (ileride) ── Valkey (erp ile aynı kurallar)
```

- Servisler kendi rotalarını zaten `/api/identity/...` ve `/api/erp/...` önekiyle yayınlar. Böylece proxy yol yeniden yazmaz ve ileride gateway eklendiğinde servislerde değişiklik gerekmez. **[Öneri]**
- Her servisin kendi veritabanı vardır; servisler birbirinin tablosuna erişmez. **[Öneri]**
- Altyapı `infra/docker-compose.yml` ile kurulur:
  - **PostgreSQL:** tek sunucu, `identity` ve `erp` veritabanları.
  - **Valkey.**
  - **Mailpit.**

  İmaj sürümleri uygulama sırasında güncel kararlı sürüme sabitlenir. Veriler kalıcı volume'larda tutulur. **[Öneri]**

## 6. Oturum sözleşmesi (v1)

Python ve Go servisleri bu bölümü birebir uygulayacak. Örnek kayıt `docs/contracts/session-v1.example.json` dosyasında tutulacak.

### 6.1 Cookie
- **Ad:** `modaryo_sid`
- **Değer:** 32 baytlık kriptografik rastgele sayının base64url hali (oturum kimliği, `sid`).
- **Bayraklar:** `HttpOnly`, `SameSite=Lax`, `Path=/`. `Secure` üretimde zorunlu, geliştirmede (http://localhost) yapılandırmadan kapatılabilir.
- **"Beni hatırla" açık:** kalıcı cookie, `Max-Age` mutlak bitiş süresine eşit.
- **"Beni hatırla" kapalı:** tarayıcı oturumu cookie'si (`Max-Age` yok).

### 6.2 Valkey anahtarları
- **Oturum:** `sess:{sha256_hex(sid)}`. Ham `sid` Valkey'de saklanmaz; Valkey'e erişen biri çalışır bir cookie elde edemez.
- **Kullanıcı dizini:** `user-sess:{user_id}`. Kullanıcının oturum anahtar hash'lerinden oluşan bir SET. Bayat üyeler okunurken temizlenir. Bu dizini yalnızca kimlik servisi kullanır. Her yeni oturumda TTL'i 30 güne (en uzun mutlak süre) yenilenir.

### 6.3 Kayıt içeriği (JSON, string olarak saklanır)
```json
{
  "v": 1,
  "id": "a1b2c3d4e5f60718",
  "user_id": "0193...-uuid",
  "email": "ali@ornek.com",
  "full_name": "Ali Veli",
  "tenant_id": "0193...-uuid veya null",
  "permissions": ["*"],
  "remember_me": true,
  "created_at": 1795200000,
  "absolute_expires_at": 1797792000,
  "idle_timeout_s": 1209600,
  "ip": "203.0.113.5",
  "user_agent": "Mozilla/5.0 ..."
}
```
- **Zamanlar:** Unix saniyesi (UTC).
- **`id`:** Aktif oturumlar ekranında kullanılan, cookie'den türetilemeyen ayrı bir rastgele kimlik.
- **`tenant_id` boşsa:** Kullanıcının aktif firması yok demek. Bu durumda `permissions` boş liste olur.
- **`permissions`:** Aktif firmadaki rolün izinleri. `"*"` tüm izinler anlamına gelir.
- **Bilinmeyen alanlar:** Okuyucular bilinmeyen alanları yok sayar. `v` değeri 1 değilse oturum geçersiz sayılır.

### 6.4 Süreler [Öneri]
| Durum | Hareketsizlik süresi (`idle_timeout_s`) | Mutlak süre |
|---|---|---|
| Beni hatırla kapalı | 8 saat | 24 saat |
| Beni hatırla açık | 14 gün | 30 gün |

Valkey TTL değeri her zaman `min(idle_timeout_s, absolute_expires_at - şimdi)` olur.

### 6.5 Kim ne yapabilir
- **Kimlik servisi (tek yazıcı):**
  - **Oluşturma:** `SET key value EX ttl NX`.
  - **İçerik güncelleme:** `SET key value KEEPTTL XX`. `XX` silinmiş bir oturumun geri gelmesini, `KEEPTTL` sürenin uzamasını engeller.
  - **Silme:** `DEL`.
- **Diğer tüm servisler (okuyucu):**
  - **Okuma:** yalnızca `GET`.
  - **Süre uzatma:** `EXPIRE key ttl` (6.4'teki formülle). Değeri asla yazmaz, oturumu asla silmez.

### 6.6 Okuyucu algoritması
1. `modaryo_sid` cookie'si yoksa isteği anonim say.
2. `GET sess:{sha256_hex(sid)}` boş dönerse anonim say.
3. JSON'u çözümle. `v != 1` ise ya da `absolute_expires_at <= şimdi` ise anonim say.
4. Süreyi uzat (6.5'teki `EXPIRE` ile).
5. Kimliği kur: `user_id`, `email`, `tenant_id`, `permissions`.

### 6.7 Oturumu değiştiren olaylar
| Olay | Etki |
|---|---|
| Çıkış | O oturum silinir |
| Tüm cihazlardan çıkış | Kullanıcının tüm oturumları silinir |
| Şifre değiştirme | Mevcut oturum dışındaki tüm oturumlar silinir |
| Şifre sıfırlama | Tüm oturumlar silinir |
| Oturumlar ekranından kapatma | Seçilen oturum silinir |
| Firma oluşturma / davet kabul / firma değiştirme | Mevcut oturumun `tenant_id` ve `permissions` alanları güncellenir |
| Üyenin rolü değişti | Kullanıcının o firmadaki tüm oturumlarında `permissions` güncellenir |
| Üye firmadan çıkarıldı / ayrıldı | Kullanıcının o firmadaki tüm oturumlarında `tenant_id = null`, `permissions = []` olur |
| Kullanıcı silindi (temizlik işi) | Tüm oturumları silinir |
| Rol izinlerinin düzenlenmesi | İlk sürümde yok (rol düzenleme ekranı kapsam dışı). Eklendiğinde o role sahip herkesin oturumu güncellenecek. |

### 6.8 CSRF ve proxy
- **CSRF:** `GET/HEAD/OPTIONS` dışındaki tüm isteklerde `X-Modaryo-Csrf: 1` başlığı zorunludur. Başlık yoksa servis 403 döner. Bu kontrolü **her servis kendisi yapar**, yalnızca frontend'e güvenilmez. Servisler CORS'a izin vermez. Böylece başka bir site bu başlığı gönderemez; `SameSite=Lax` da ikinci koruma katmanıdır. **[Öneri]**
- **İstemci IP'si:** Servisler `X-Forwarded-For` başlığını yalnızca güvenilen proxy'den (geliştirmede loopback) kabul eder. Rate limit ve oturumdaki `ip` alanı bu adrese göre çalışır. Next.js proxy'sinin bu başlığı eklediği uygulama sırasında doğrulanacak; eklemiyorsa Next.js tarafında eklenecek.

## 7. Kimlik servisi (`services/identity`)

### 7.1 Kurulum [Öneri]
- **Şablon:** `dotnet new ca-sln -n IdentityService -o services/identity -f net10.0 -cf None -db postgresql`. Ad `IdentityService`, çünkü `Identity` adı `Microsoft.AspNetCore.Identity` ad alanıyla çakışır.
- **Temizlik:** Şablonun Todo/Weather örnekleri ve testleri silinir. AppHost ve TestAppHost projeleri kaldırılır (bkz. 3. Kısıtlar); ServiceDefaults kalır.
- **ERP servisinden taşınacak ayarlar:**
  - `global.json`: 10.0.404, `latestFeature`.
  - `nuget.config`: V3 kaynağı.
  - Şablonun `.editorconfig` dosyası silinir, solution kökteki dosyayı gösterir.
  - `.gitignore` dosyasına `*.db` eklenir.
- **Veritabanı:** `EnsureDeleted/EnsureCreated` yerine EF Core migration'ları kullanılır. Geliştirmede uygulama açılırken migration'lar otomatik uygulanır.
- **Kimlik altyapısı:** ASP.NET Identity yalnızca kullanıcı deposu, şifre özeti, hesap kilidi ve doğrulama/sıfırlama kodları için kullanılır. `MapIdentityApi` ve bearer token kaldırılır; uç noktalar elle yazılır.
- **Port:** `http://localhost:5100`. Geliştirmede HTTPS yönlendirmesi kapalıdır (servisler proxy arkasında).
- **Oturum okuma:** Kimlik servisi, oturum gerektiren kendi uç noktalarında da 6.6'daki okuyucuyu kullanır; .NET cookie kimlik doğrulaması kullanılmaz.
- **Data Protection anahtarları:** E-posta doğrulama ve şifre sıfırlama kodları ASP.NET Data Protection ile üretilir. Anahtarlar PostgreSQL'de kalıcı tutulur; aksi halde servis yeniden başlayınca gönderilmiş linkler çalışmaz.
- **Geliştirme verisi:** Yalnızca geliştirme ortamında `demo@modaryo.local` kullanıcısı ve ona ait "Demo Firma" otomatik oluşturulur. Şifre `appsettings.Development.json` dosyasında tutulur. Frontend geliştirirken her seferinde kayıt olmamak içindir.

### 7.2 Veri modeli
- **`users`:** Identity kullanıcısı (`IdentityUser<Guid>`) ve ek alanlar: `FullName`, `CreatedAt`, `EmailConfirmedAt`, `LastMembershipEndedAt`, `CleanupReminderSentAt`, `LastActiveTenantId`.
- **`tenants`:** `Id`, `Name`, `CreatedAt`, `CreatedByUserId`.
- **`tenant_roles`:** `Id`, `TenantId`, `Name`, `SystemKey` (`owner` | `admin` | `member` | null), `IsSystem`.
- **`tenant_role_permissions`:** `RoleId`, `Permission`.
- **`memberships`:** `Id`, `TenantId`, `UserId`, `RoleId`, `JoinedAt`. `(TenantId, UserId)` benzersizdir.
- **`invitations`:** `Id`, `TenantId`, `Email` (normalize edilmiş), `RoleId`, `TokenHash`, `InvitedByUserId`, `CreatedAt`, `ExpiresAt`, `AcceptedAt`, `RevokedAt`.

Kimlikler UUID v7 olur. Identity'nin genel `AspNetRoles` yapısı kullanılmaz; roller firma bazlıdır. **[Öneri]**

### 7.3 İzinler ve hazır roller
- **İzin adları:** `modül.eylem` biçiminde sabit string'lerdir. Kod rol adına değil izne bakar. **[Kullanıcı: izin tabanlı]**
- **İlk sürümdeki izinler:** `company.update`, `members.read`, `members.invite`, `members.manage`. **[Öneri]**
- **Hazır roller (firma oluşturulunca otomatik eklenir):** **[Kullanıcı: hazır roller; isimler ve izinler Öneri]**
  - **Sahip:** `["*"]`. Firmada en az bir sahip bulunmak zorundadır. Sahip rolünü yalnızca sahip verebilir veya geri alabilir.
  - **Yönetici:** `company.update`, `members.read`, `members.invite`, `members.manage`.
  - **Kullanıcı:** `members.read`. ERP modülleri geldikçe izinleri eklenecek.
- **Yeni modül izinleri:** Yeni modüllerin izinleri hazır rollere bir migration/seed adımıyla eklenir.

### 7.4 Uç noktalar
Tümü `/api/identity` öneki altındadır. "Oturum" sütunu, uç noktanın giriş yapmış kullanıcı gerektirip gerektirmediğini gösterir.

| Yöntem ve yol | Oturum | Açıklama |
|---|---|---|
| `POST /auth/register` | — | `{email, password, fullName, inviteToken?}`. Ayrıntılar 7.5'te. |
| `POST /auth/confirm-email` | — | `{userId, token}`. E-postayı doğrular ve oturum açar. |
| `POST /auth/resend-confirmation` | — | `{email}`. Her zaman 202 döner. |
| `POST /auth/login` | — | `{email, password, rememberMe}` |
| `POST /auth/logout` | ✓ | |
| `POST /auth/logout-all` | ✓ | |
| `GET /auth/session` | ✓ | Kullanıcı, aktif firma, izinler, üyelik listesi (firma değiştirici bunu kullanır) |
| `POST /auth/session/tenant` | ✓ | `{tenantId}`. Aktif firmayı değiştirir |
| `POST /auth/forgot-password` | — | `{email}`. Her zaman 202 döner. |
| `POST /auth/reset-password` | — | `{email, token, newPassword}` |
| `POST /account/change-password` | ✓ | `{currentPassword, newPassword}` |
| `PATCH /account/profile` | ✓ | `{fullName}` |
| `GET /account/sessions` | ✓ | Cihaz, IP, oluşturulma zamanı, "bu cihaz" işareti |
| `DELETE /account/sessions/{id}` | ✓ | |
| `GET /account/invitations` | ✓ | Kullanıcının e-postasına gelmiş bekleyen davetler |
| `POST /companies` | ✓ | `{name}`. Firma ve hazır roller oluşur, kullanıcı sahip olur, oturumun firması güncellenir. |
| `GET /companies/current` | ✓ | |
| `PATCH /companies/current` | `company.update` | |
| `POST /companies/current/leave` | ✓ | Son sahip ayrılamaz |
| `GET /companies/current/roles` | `members.read` | Davet formundaki rol listesi |
| `GET /companies/current/members` | `members.read` | |
| `PATCH /companies/current/members/{userId}` | `members.manage` | Rol değiştirme |
| `DELETE /companies/current/members/{userId}` | `members.manage` | Son sahip çıkarılamaz |
| `GET /companies/current/invitations` | `members.invite` | |
| `POST /companies/current/invitations` | `members.invite` | `{email, roleId}` |
| `POST /companies/current/invitations/{id}/resend` | `members.invite` | |
| `DELETE /companies/current/invitations/{id}` | `members.invite` | İptal |
| `GET /invitations/{token}` | — | Davet önizlemesi: firma adı, davet eden, davet edilen e-posta |
| `POST /invitations/{token}/accept` | ✓ | |

Hatalar ProblemDetails biçiminde ve Türkçe mesajla döner. **[Öneri]**

### 7.5 Kurallar ve uç durumlar [Öneri]

**Kayıt:**
- Yanıt her zaman 202 "E-postanızı kontrol edin" olur.
- E-posta zaten kayıtlıysa yeni hesap açılmaz. O adrese "zaten hesabınız var, giriş yapın veya şifrenizi sıfırlayın" e-postası gider. Böylece yanıt, e-postanın kayıtlı olup olmadığını sızdırmaz.

**Davet linkiyle kayıt:**
- Davet token'ı geçerliyse ve e-posta davet edilen adresle aynıysa, hesap doğrulanmış olarak açılır. Davet linki e-posta sahipliğini zaten kanıtlar.
- Davet kabul edilir, oturum açılır ve yanıt 200 döner.

**Giriş:**
- Yanlış e-posta ve yanlış şifre aynı genel hatayı verir.
- 5 başarısız denemeden sonra hesap 15 dakika kilitlenir ve kilit mesajı gösterilir. Bu mesaj yalnızca hedef alınmış hesaplarda varlığı belli eder; kabul edilebilir bir ödünleşim.
- E-postası doğrulanmamış hesap, şifre doğruysa "e-postanızı doğrulayın" yanıtını alır (yeniden gönder seçeneğiyle).

**Şifre politikası:** En az 10 karakter. Karakter türü zorunluluğu yok (NIST yaklaşımı).

**Kod ve token süreleri:** E-posta doğrulama 48 saat, şifre sıfırlama 1 saat, davet 7 gün. Davet token'ı 32 baytlık rastgele bir değerdir; yalnızca SHA-256 özeti saklanır.

**Rate limit (istemci IP'sine göre):**
- Giriş: dakikada 10. Ayrıca e-posta başına dakikada 5.
- Kayıt: 10 dakikada 5.
- Şifremi unuttum ve yeniden gönder: 10 dakikada 5. Ayrıca e-posta başına saatte 3.

**Davet:**
- Davet, davet edilen e-postaya bağlıdır. Başka e-postayla giriş yapmış biri kabul etmeye çalışırsa "Bu davet x@… adresine gönderildi, o hesapla giriş yapın" hatası alır.
- Davet edilen e-postayla açılmış ama henüz doğrulanmamış bir hesap varsa, davet linkinden yapılan işlem o hesabı doğrulanmış sayar. Kullanıcı yeni şifresini belirler ve davet kabul edilir.
- Zaten o firmanın üyesi olan birine davet gönderilemez.
- Aynı e-postaya aynı firmadan bekleyen tek davet olur. Yeniden göndermek yeni bir token üretir, eskisi geçersizleşir.
- **Başka firmadan davet:** Zaten bir firması olan kullanıcı başka bir firmadan gelen daveti kabul edebilir. Yeni üyelik oluşur ve kabul edilen firma oturumun aktif firması olur. Kullanıcı profil menüsündeki basit firma değiştiriciyle diğer firmasına dönebilir. **[Kullanıcı]**

**Aktif firma:**
- **Girişte:** Kullanıcının en son aktif olarak seçtiği firma seçilir (`users.LastActiveTenantId`, yeni alan). Bu yoksa en son katıldığı firma seçilir. **[Öneri]**
- **Firma değiştirme:** `POST /auth/session/tenant {tenantId}`. Kullanıcı o firmanın üyesiyse oturumun `tenant_id` ve `permissions` alanları güncellenir (`KEEPTTL XX`) ve `LastActiveTenantId` kaydedilir. Üye değilse 403 döner.
- **Firmasız kullanıcı:** `tenant_id = null` olur ve frontend onu "başlangıç" ekranına (firma oluştur / bekleyen davetler) yönlendirir.

### 7.6 Otomatik temizlik [Kullanıcı: kural; süreler Öneri]
Kimlik servisinde günde bir çalışan bir arka plan işi:
- **Doğrulanmamış hesaplar:** Kayıttan 7 gün sonra hâlâ doğrulanmamış hesaplar silinir.
- **Firmasız kullanıcılar:** Süre `max(EmailConfirmedAt, LastMembershipEndedAt)` anından itibaren işler. 23. günde hatırlatma e-postası gider (`CleanupReminderSentAt`), 30. günde kullanıcı ve tüm oturumları silinir.
- **Süreyi sıfırlayan durum:** Kullanıcı bu süre içinde bir firmaya katılırsa sayaç sıfırlanır. Firmadan çıkınca `LastMembershipEndedAt` yeniden yazılır.
- **Bekleyen davetler:** Sayacı durdurmaz. Silinen kişi aynı davet linkiyle yeniden kayıt olabilir.

### 7.7 E-posta
- **Gönderim:** SMTP ile. Geliştirmede Mailpit'e (`localhost:1025`) gider.
- **Arayüz:** E-postalar soyut bir `IEmailSender` arayüzünden gönderilir. Testlerde bellekte toplayan bir sahte uygulama kullanılır.
- **Şablonlar:** Türkçe HTML ve düz metin. Konular: doğrulama, şifre sıfırlama, "zaten hesabınız var", davet, temizlik hatırlatması.
- **Linkler:** Frontend adresine gider (`App:FrontendBaseUrl`, geliştirmede `http://localhost:3000`):
  - `/eposta-dogrula?u=…&t=…`
  - `/sifre-sifirla?e=…&t=…`
  - `/davet/{token}`

## 8. ERP servisindeki değişiklikler (`services/erp`)
- **Silinecekler:** Şablonun Todo/Weather örnekleri ve testleri; Identity kısmı (`ApplicationUser`, `IdentityService`, `Users` uç noktaları, kullanıcı seed'i); AppHost ve TestAppHost.
- **Veritabanı:** SQLite'tan PostgreSQL'e (Npgsql) geçilir ve migration'lar kullanılır.
- **Oturum okuyucu:** 6.6'daki algoritmayı uygulayan bir kimlik doğrulama handler'ı. CSRF başlık kontrolü ve `RequirePermission("x.y")` yetki politikası. Mevcut kullanıcı ve firma bilgisi `IUser` / `ICurrentTenant` üzerinden sağlanır.
- **Firma verisi ayrımı:** Firmaya ait varlıklar `TenantId` taşır. EF global sorgu filtresi her sorguyu aktif firmaya kısıtlar; yeni kayda `TenantId` otomatik yazılır.
- **Doğrulama uç noktası:** Sözleşmeyi kanıtlamak için `GET /api/erp/me` eklenir; oturumdan okunan kimliği döndürür.

## 9. Frontend (backend tamamlandıktan sonra)
- **Proxy:** `next.config.ts` içinde `/api/identity/:path*` ve `/api/erp/:path*` yönlendirmeleri. Tüm istemci çağrılarına `X-Modaryo-Csrf: 1` başlığı eklenir.
- **Sayfalar (panel düzeni dışında, ortalanmış kart görünümünde):** `/giris`, `/kayit`, `/sifremi-unuttum`, `/sifre-sifirla`, `/eposta-dogrula`, `/davet/[token]`, `/baslangic` (firma oluştur / bekleyen davetler).
- **Panel içi sayfalar:**
  - `/hesap`: profil, şifre değiştirme, aktif oturumlar.
  - `/firma/uyeler`: üyeler, roller, davetler.
- **Sayfa koruması:** Next.js'in istek ara katmanı cookie yoksa kullanıcıyı `/giris` sayfasına yönlendirir. Gerçek doğrulama `GET /api/identity/auth/session` çağrısıyla yapılır. Firması olmayan kullanıcı `/baslangic` sayfasına gönderilir.
- **Üst bar:** Profil menüsündeki "Çıkış Yap" gerçek çıkış yapar; ad ve e-posta oturumdan gelir.
- **Firma değiştirici:** Kullanıcı birden fazla firmanın üyesiyse profil menüsünde firmalarının listesi görünür. Birine tıklayınca `POST /api/identity/auth/session/tenant` çağrılır ve sayfa yeni firmanın verisiyle yeniden yüklenir.
- **Örnek veri:** Dashboard'daki örnek veri bu modülde değişmez.

## 10. Çalıştırma, test ve aşamalar

**Çalıştırma [Öneri]**
- **Altyapı:** `wsl docker compose -f infra/docker-compose.yml up -d`.
- **Servisler:** `dotnet run` ile çalışır; `.claude/launch.json` dosyasına `identity` ve `erp` girdileri eklenir.
- **Belgeler:** Kökteki README'de tüm komutlar listelenir.

**Test [Öneri]**
- **Fonksiyonel testler:** Testler compose'daki PostgreSQL ve Valkey'e bağlanır. Her test çalıştırması kendi geçici veritabanını (`identity_test_<rastgele>`) oluşturur ve sonunda siler. Valkey'de test çalıştırmasına özel bir anahtar öneki kullanılır.
- **Test verisi:** Template'teki Respawn testler arası temizliği yapar. E-posta testleri bellekteki sahte gönderici üzerinden doğrulanır.
- **Sözleşme testi:** ERP'nin oturum okuyucusu, `docs/contracts/session-v1.example.json` dosyasını ve kimlik servisinin gerçekten yazdığı bir kaydı doğru okuyabilmeli.

**Aşamalar ve "bitti" ölçütleri**

| Aşama | İş | Bitti ölçütü |
|---|---|---|
| 0 | Altyapı | Compose ayakta; Windows'tan PostgreSQL bağlantısı, Valkey `PING` ve Mailpit arayüzü (8025) çalışıyor; `wsl` komutu döndükten sonra container'lar hâlâ açık |
| 1 | Kimlik servisi iskeleti | Şablon temizlendi, 7.1'deki ayarlar taşındı, PostgreSQL ve migration'lar çalışıyor, build ve testler yeşil |
| 2 | Kimlik akışları | Kayıt → Mailpit'te e-posta → doğrulama → giriş → oturumlar → çıkış; şifremi unuttum ve sıfırlama; kilit ve rate limit; CSRF. Fonksiyonel testler geçiyor; aynı akış Scalar veya curl ile elle de doğrulandı |
| 3 | Firma, rol ve davet | Firma oluşturma, davet akışları, rol ve üye işlemleri, oturum güncellemeleri (6.7), temizlik işi. Testler geçiyor |
| 4 | ERP dönüşümü | ERP, kimlik servisinin oluşturduğu oturumla `/api/erp/me` çağrısını kabul ediyor; silinmiş oturumu ve CSRF başlığı olmayan isteği reddediyor; izin kontrolü çalışıyor |
| 5 | Frontend ekranları | 9. bölümdeki sayfalar; tarayıcıda uçtan uca kayıt → doğrulama → firma oluşturma → davet → ikinci kullanıcıyla kabul |

## 11. Açık sorular
Yok. Son açık soru (başka firmadan gelen davet) cevaplandı: kabul edilebilir, basit bir firma değiştirici eklenir. **[Kullanıcı]**

## 12. Uygulayıcıya bırakılanlar
- Container imajlarının ve NuGet paketlerinin kesin sürümleri (güncel kararlı sürüm).
- E-posta şablonlarının görsel tasarımı (temanın renkleriyle sade bir yerleşim).
- Arka plan işinin zamanlaması (`PeriodicTimer` ile günde bir; geliştirmede elle tetiklenebilir bir uç nokta veya yapılandırma ayarı).
- Rate limit'in ASP.NET'in yerleşik `RateLimiter` mekanizmasıyla uygulanması; ayrıntılar uygulayıcıda.
