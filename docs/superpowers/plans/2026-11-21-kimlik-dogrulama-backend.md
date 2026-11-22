# Kimlik Doğrulama — Backend Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ayrı bir kimlik servisi (`services/identity`) kurmak; kayıt, giriş, şifre, oturum, firma, rol ve davet API'lerini yazmak; ERP servisini Valkey'deki ortak oturumu okuyacak hale getirmek.

**Architecture:** Oturumlar Valkey'de dilden bağımsız JSON kaydı olarak tutulur. Kaydı yalnızca kimlik servisi yazar; diğer servisler okur ve süresini uzatır. Okuma, CSRF ve izin kontrolü kodu `libs/dotnet/Modaryo.Sessions` paylaşılan kütüphanesinde durur; iki .NET servisi de onu kullanır. Her servisin kendi PostgreSQL veritabanı vardır. Altyapı WSL'deki Docker'da `docker compose` ile çalışır.

**Tech Stack:** .NET 10 (SDK 10.0.404), Jason Taylor Clean Architecture şablonu 11.0.0 (MediatR, FluentValidation, NUnit, Shouldly, Respawn), ASP.NET Identity (yalnızca kullanıcı deposu, şifre ve kod üretimi için), EF Core + Npgsql, StackExchange.Redis (Valkey istemcisi), MailKit, PostgreSQL, Valkey, Mailpit.

**Spec:** `docs/superpowers/specs/2026-11-21-kimlik-dogrulama-design.md`. Bu plan spec'in 10. bölümündeki 0–4. aşamaları kapsar. 5. aşama (frontend ekranları) ayrı bir plan olacak.

## Global Constraints

- **Repo git deposu değil.** Bu yüzden "commit" adımları yerine "Kontrol noktası" adımları var. Kullanıcı git başlatırsa her kontrol noktası bir commit olur.
- **SDK ve şablon:** Her .NET servisinde `global.json` = `{"sdk":{"version":"10.0.404","rollForward":"latestFeature","allowPrerelease":false}}`. Ayrıca `nuget.config` içinde `<clear/>` + `https://api.nuget.org/v3/index.json` olmalı. Şablon kurulum ve oluşturma komutlarına da `--add-source https://api.nuget.org/v3/index.json` eklenir.
- **`.editorconfig`:** Repo kökündeki kullanıcıya ait dosyadır. Servislerde `root = true` içeren `.editorconfig` bulunmaz. Yeni C# dosyaları 4 boşluk girinti kullanır.
- **Docker:** Docker yalnızca WSL içinde. Komutlar `wsl docker ...` biçiminde çalıştırılır. Aspire AppHost ve TestAppHost kullanılmaz.
- **Bağlantı bilgileri (geliştirme):**
  - PostgreSQL: `Host=localhost;Port=5432;Username=modaryo;Password=modaryo_dev`, veritabanları `identity` ve `erp`.
  - Valkey: `localhost:6379`. Testlerde `localhost:6379,defaultDatabase=1` kullanılır.
  - Mailpit: SMTP `localhost:1025`, arayüz ve API `http://localhost:8025`.
- **Portlar:** Kimlik servisi `http://localhost:5100`, ERP `http://localhost:5282`. Geliştirmede HTTPS yönlendirmesi kapalıdır.
- **Rota önekleri:** Kimlik servisinin tüm rotaları `/api/identity/...`, ERP'ninkiler `/api/erp/...` altındadır. Bu önekler `IEndpointGroup.RoutePrefix` ile verilir.
- **Oturum sözleşmesi:** Spec 6. bölümüyle birebir aynı olmalı.
  - Cookie adı `modaryo_sid`, CSRF başlığı `X-Modaryo-Csrf: 1`.
  - Anahtarlar `sess:{sha256_hex(sid)}` ve `user-sess:{user_id}`.
  - JSON alanları snake_case, `"v": 1`.
- **Oturum süreleri:**

  | Durum | Hareketsizlik | Mutlak |
  |---|---|---|
  | `remember_me=false` | 8 saat (28800 sn) | 24 saat |
  | `remember_me=true` | 14 gün (1209600 sn) | 30 gün |

  TTL = `min(idle_timeout_s, absolute_expires_at - now)`.
- **Güvenlik kuralları:**
  - Şifre: en az 10, en fazla 128 karakter; karakter türü zorunluluğu yok.
  - Kilit: 5 başarısız deneme → 15 dakika.
  - Kod süreleri: e-posta doğrulama 48 saat, şifre sıfırlama 1 saat, davet 7 gün. Davet token'ı 32 rastgele bayt, base64url; yalnızca SHA-256 özeti saklanır.
- **Rate limit (istemci IP'sine göre):**
  - Giriş: dakikada 10; ayrıca e-posta başına dakikada 5.
  - Kayıt: 10 dakikada 5.
  - Şifremi unuttum ve yeniden gönder: IP başına 10 dakikada 5; e-posta başına saatte 3.
- **Temizlik:** Doğrulanmamış hesap 7 gün sonra silinir. Firmasız kullanıcı için hatırlatma 23. günde gider, silme 30. günde olur. Sayaç `max(EmailConfirmedAt, LastMembershipEndedAt)` anından başlar.
- **İzinler ve hazır roller:**
  - İzinler: `company.update`, `members.read`, `members.invite`, `members.manage`; tümü için `*`.
  - Sahip (`owner`): `["*"]`.
  - Yönetici (`admin`): ilk dört izin.
  - Kullanıcı (`member`): `["members.read"]`.
- **Hata biçimi:** Hata mesajları Türkçe. Hata yanıtları ProblemDetails; makinece okunacak durumlar için `extensions.code` alanı kullanılır (bkz. Task 7 `AuthErrorCodes`).
- **Kimlikler:** UUID v7 (`Guid.CreateVersion7()`).

## Review Focus

1. **Silinmiş oturumun dirilmesi:** Bir oturum silindikten hemen sonra gelen rol değişikliği ya da firma değiştirme isteği oturumu yeniden oluşturmamalı. `SET ... XX` kullanılmalı. → Task 5 `UpdateContent_DoesNotReviveDeletedSession`.
2. **Mutlak sürenin aşılması:** Okuyucunun `EXPIRE` çağrısı, mutlak bitişe 10 saniye kalmış bir oturumu 8 saat uzatmamalı. → Task 2 `ReadAsync_NeverExtendsBeyondAbsoluteExpiry`.
3. **Büyük/küçük harf ve boşluk:** `Ali@Ornek.com ` ile kayıt olan biri `ali@ornek.com` ile giriş yapabilmeli. Davetin e-posta eşleşmesi de aynı normalizasyonla yapılmalı. → Task 8 `Register_NormalizesEmail`, Task 12 `Accept_MatchesEmailCaseInsensitively`.
4. **Son sahip:** Son sahip kendi rolünü düşürememeli, firmadan ayrılamamalı ve çıkarılamamalı. → Task 11 `LastOwner_CannotDemoteLeaveOrBeRemoved`.
5. **Başka firmanın kaynağı:** Aktif firması A olan bir yönetici, B firmasının üyesinin rolünü ya da davetini (id bilse bile) değiştirememeli; 404 dönmeli. → Task 11 `ChangeRole_ForMemberOfOtherTenant_Returns404`, Task 12 `RevokeInvitation_OfOtherTenant_Returns404`.

---

## Dosya yapısı

```
infra/
  docker-compose.yml                  # postgres, valkey, mailpit
  postgres/init/01-databases.sql      # identity ve erp veritabanları
docs/contracts/session-v1.example.json
libs/dotnet/
  Directory.Build.props, Directory.Packages.props, global.json, nuget.config
  Modaryo.Sessions/                   # paylaşılan oturum okuma/CSRF/izin kodu
    SessionRecord.cs  SessionKeys.cs  SessionTtl.cs  SessionReader.cs
    SessionAuthenticationHandler.cs  CsrfHeaderMiddleware.cs
    ClaimsPrincipalExtensions.cs  ServiceCollectionExtensions.cs
  Modaryo.Sessions.Tests/
services/identity/                    # IdentityService.* (yeni, şablondan)
  src/Domain/        Entities/{Tenant,TenantRole,TenantRolePermission,Membership,Invitation}.cs
                     Constants/{Permissions,SystemRoles}.cs
  src/Application/   Common/Interfaces/{ISessionStore,IEmailSender,IEmailComposer,IUserAccounts,ICurrentSession}.cs
                     Auth/ Account/ Companies/ Invitations/ Maintenance/   (özellik klasörleri)
  src/Infrastructure/Identity/{ApplicationUser,UserAccounts}.cs
                     Sessions/SessionStore.cs
                     Email/{SmtpEmailSender,EmailComposer}.cs
                     Data/ (DbContext, Configurations, Migrations, Seed)
                     Maintenance/AccountCleanupWorker.cs
  src/Web/           Endpoints/{Auth,Account,Companies,Invitations}.cs
                     Infrastructure/{SessionCookie,RateLimiting,CurrentSession}.cs
  tests/             Domain.UnitTests, Application.UnitTests, Web.FunctionalTests
services/erp/                         # dönüştürülür (Task 14)
```

---

### Task 1: Altyapı (compose) ve WSL erişim doğrulaması

**Files:**
- Create: `infra/docker-compose.yml`
- Create: `infra/postgres/init/01-databases.sql`
- Create: `README.md` (kök; altyapı komutları bölümüyle başlar)

**Interfaces:**
- Produces: Bağlantı bilgileri Global Constraints'teki gibi. Container adları: `modaryo-postgres`, `modaryo-valkey`, `modaryo-mailpit`.

- [ ] **Step 1: Compose dosyasını yaz.**
  - İmajlar: `postgres`, `valkey/valkey`, `axllent/mailpit`. Her biri o günün güncel kararlı sürüm etiketine sabitlenir (`latest` kullanılmaz).
  - Portlar: 5432, 6379, 1025, 8025.
  - Volume'lar: `modaryo-pg-data`, `modaryo-valkey-data`.
  - Valkey `--appendonly yes` ile çalışır.
  - `restart: unless-stopped`.
  - Postgres'in init klasörüne `01-databases.sql` bağlanır: `CREATE DATABASE identity; CREATE DATABASE erp;`.
  - Her servisin bir healthcheck'i olur.
- [ ] **Step 2: Başlat.**
  Run: `wsl docker compose -f infra/docker-compose.yml up -d` (Windows yolu WSL'de `/mnt/c/...` biçimine çevrilerek).
  Expected: Üç container da `healthy`.
- [ ] **Step 3: Windows'tan erişimi doğrula.** Bu adım spec'teki varsayımın testidir.
  Run (Windows Bash):
  - `curl -s http://localhost:8025/api/v1/info` → JSON döner.
  - Valkey: `bash -c 'exec 3<>/dev/tcp/localhost/6379; printf "PING\r\n" >&3; head -c 7 <&3'` → `+PONG`.
  - Postgres: `bash -c '</dev/tcp/localhost/5432' && echo ok` → `ok`.

  Expected: Üçü de başarılı.
  Başarısızsa: WSL2 localhost yönlendirmesini kontrol et (`%UserProfile%\.wslconfig` içinde `networkingMode`). Kullanıcıya rapor et ve **dur**; diğer task'lar buna bağlı.
- [ ] **Step 4: Kalıcılığı doğrula.**
  Run: `wsl docker ps --format '{{.Names}} {{.Status}}'`. Ardından ayrı bir komutla 3. adımdaki kontrolleri tekrar çalıştır.
  Expected: Container'lar `wsl` oturumu kapandıktan sonra da `Up`.
- [ ] **Step 5: README'ye bölüm ekle.** "Altyapıyı başlat / durdur / sıfırla" komutları ve Mailpit adresi.
- [ ] **Kontrol noktası.** Altyapı ayakta, erişim doğrulandı.

---

### Task 2: Paylaşılan oturum kütüphanesi `Modaryo.Sessions` (okuyucu tarafı)

**Files:**
- Create: `libs/dotnet/{global.json,nuget.config,Directory.Build.props,Directory.Packages.props}`. Değerler Global Constraints'teki gibi. CPM (Central Package Management) açık: `ManagePackageVersionsCentrally=true`.
- Create: `libs/dotnet/Modaryo.Sessions/Modaryo.Sessions.csproj`. `net10.0`, `FrameworkReference Microsoft.AspNetCore.App`, `PackageReference StackExchange.Redis`.
- Create: `libs/dotnet/Modaryo.Sessions/*.cs` (dosya yapısında listelenenler)
- Create: `libs/dotnet/Modaryo.Sessions.Tests/` (NUnit + Shouldly)
- Create: `docs/contracts/session-v1.example.json` (spec 6.3'teki örnek, gerçek UUID'lerle)

**Interfaces:**
- Produces (namespace `Modaryo.Sessions`):
  - `public sealed record SessionRecord`. Alanlar: `int V`, `string Id`, `Guid UserId`, `string Email`, `string FullName`, `Guid? TenantId`, `string[] Permissions`, `bool RememberMe`, `long CreatedAt`, `long AbsoluteExpiresAt`, `int IdleTimeoutS`, `string? Ip`, `string? UserAgent`.
    - JSON adlandırma snake_case (`JsonNamingPolicy.SnakeCaseLower`).
    - `public static JsonSerializerOptions Json { get; }`.
  - `public static class SessionKeys`:
    - Sabitler: `CookieName = "modaryo_sid"`, `CsrfHeader = "X-Modaryo-Csrf"`, `CurrentVersion = 1`, `AllPermissions = "*"`.
    - Metotlar: `string NewSid()`, `string NewPublicId()` (16 hex karakter), `string HashSid(string sid)` (küçük harf hex SHA-256), `string SessionKey(string sidHash)`, `string UserIndexKey(Guid userId)`.
  - `public static class SessionTtl`. Süre sabitleri:
    - `RegularIdle` = 8 saat, `RegularAbsolute` = 24 saat
    - `RememberIdle` = 14 gün, `RememberAbsolute` = 30 gün

    Metot: `TimeSpan? Compute(SessionRecord r, DateTimeOffset now)`. Süresi dolmuşsa `null` döner.
  - `public sealed record SessionLookup(string SidHash, SessionRecord Record)`.
  - `public sealed class SessionReader(IConnectionMultiplexer redis, TimeProvider time)`:
    - Metot: `Task<SessionLookup?> ReadAsync(string sid, CancellationToken ct)`.
    - Akış: `GET`; JSON çözümle; `v == 1` ve süresi dolmamış mı kontrol et; `KeyExpireAsync(key, ttl)`.
  - `SessionAuthenticationHandler`: scheme adı `SessionAuthenticationDefaults.Scheme = "ModaryoSession"`. Üretilen claim'ler:
    - `ClaimTypes.NameIdentifier` = `user_id`
    - `ClaimTypes.Email`, `ClaimTypes.Name` = `full_name`
    - `"tenant_id"` (varsa), `"permission"` (her izin için bir tane), `"session_id"` = `id`, `"sid_hash"`
  - `ClaimsPrincipalExtensions`: `Guid? GetUserId()`, `Guid? GetTenantId()`, `IReadOnlyList<string> GetPermissions()`, `bool HasPermission(string p)` (`*` her izni karşılar), `string? GetSidHash()`, `string? GetSessionId()`.
  - `CsrfHeaderMiddleware`: GET/HEAD/OPTIONS/TRACE dışındaki isteklerde `X-Modaryo-Csrf: 1` yoksa 403 ProblemDetails döner, `extensions.code = "csrf_header_missing"`.
  - `ServiceCollectionExtensions.AddModaryoSessions(this IServiceCollection s, string valkeyConnectionString)`: multiplexer (singleton), `SessionReader`, authentication scheme ve varsayılan şema kaydı. `IApplicationBuilder UseModaryoCsrf(this IApplicationBuilder app)`.

- [ ] **Step 1: Başarısız testleri yaz** (`SessionReaderTests`, `SessionTtlTests`, `ContractTests`). Valkey db 1 kullanılır, her test kendi anahtarını üretir.
  - `Compute_RegularSession_ReturnsIdleWhenFarFromAbsolute`: kayıt `IdleTimeoutS=28800`, mutlak bitiş 24 saat sonra → 8 saat.
  - `ReadAsync_NeverExtendsBeyondAbsoluteExpiry`: mutlak bitiş 10 saniye sonra; okumadan sonra `KeyTimeToLive` ≤ 10 saniye.
  - `ReadAsync_ExpiredAbsolute_ReturnsNull`: mutlak bitiş geçmişte → `null`.
  - `ReadAsync_UnknownVersion_ReturnsNull`: `"v":2` → `null`.
  - `ReadAsync_IgnoresUnknownFields`: JSON'a `"extra":1` eklenir → kayıt yine okunur.
  - `ReadAsync_MissingKey_ReturnsNull`.
  - `ContractExample_Deserializes`: `docs/contracts/session-v1.example.json` okunur; tüm alanlar dolu, `V == 1`.
  - `HashSid_IsLowercaseHexSha256`: `HashSid("abc")` == `"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"`.
  - `HasPermission_StarGrantsEverything`.
- [ ] **Step 2: Testleri çalıştır, başarısız olduklarını gör.**
  Run: `dotnet test libs/dotnet/Modaryo.Sessions.Tests`
  Expected: Derleme hatası veya FAIL.
- [ ] **Step 3: Interfaces bloğundaki tipleri yaz.** Handler, `SessionKeys.CookieName` cookie'sini okur. Cookie yoksa veya kayıt bulunamazsa `AuthenticateResult.NoResult()` döner (401'i yetki katmanı üretir).
- [ ] **Step 4: Handler ve middleware testlerini ekle** (`Microsoft.AspNetCore.TestHost` ile kurulan, tek uç noktalı minimal bir host üzerinde):
  - `Handler_ValidCookie_SetsClaims`: `permission` claim'leri ve `tenant_id` doğru.
  - `Csrf_PostWithoutHeader_Returns403`.
  - `Csrf_GetWithoutHeader_Passes`.
- [ ] **Step 5: Tüm testleri çalıştır.**
  Run: `dotnet test libs/dotnet/Modaryo.Sessions.Tests`
  Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 3: Kimlik servisi iskeleti

**Files:**
- Create: `services/identity/**` (şablondan)
- Delete: şablonun Todo/Weather özellikleri, uç noktaları, varlıkları ve testleri; `src/AppHost`, `tests/TestAppHost`; `Infrastructure.IntegrationTests` (boş). Şablonun `MapIdentityApi` / bearer / `Users.cs` kısmı ve `IIdentityService` / `IdentityService` da silinir; Task 7–9'da yerine yenileri yazılır.
- Modify: `ERP.slnx` eşdeğeri `IdentityService.slnx` (silinen projeler çıkarılır, `../../libs/dotnet/Modaryo.Sessions/Modaryo.Sessions.csproj` eklenir)
- Modify: `tests/Application.FunctionalTests` → `tests/Web.FunctionalTests` olarak yeniden adlandırılır; Aspire yerine compose Postgres kullanır

**Interfaces:**
- Produces: `Web.FunctionalTests` içinde `IdentityApiFactory : WebApplicationFactory<Program>`. Şu ayarları yapar:
  - Rastgele adlı bir veritabanı (`identity_test_{Guid:N}`) oluşturur, migration uygular; `OneTimeTearDown` sırasında `DROP DATABASE ... WITH (FORCE)` çalıştırır.
  - Valkey bağlantısı `localhost:6379,defaultDatabase=1`.
  - `IEmailSender` yerine `FakeEmailSender` koyar (Task 6).
  - Yardımcılar: `HttpClient CreateClientWithCookies()`, `Task ResetAsync()` (Respawn + Valkey db 1 `FLUSHDB`).

- [ ] **Step 1: Şablonu kur ve oluştur.**
  Run: `dotnet new ca-sln -n IdentityService -o services/identity -f net10.0 -cf None -db postgresql --no-update-check`
  Expected: Oluşturuldu.
- [ ] **Step 2: Ayarları ERP'den taşı.** `global.json` ve `nuget.config` Global Constraints'teki gibi. `.editorconfig` silinir ve `.slnx` içinde `../../.editorconfig` gösterilir. `.gitignore` sonuna `*.db*` eklenir.
- [ ] **Step 3: Silme listesini uygula** (Files bölümündeki). AppHost'a referans veren `Shared` projesi kalabilir; `Services.Database` sabiti `"IdentityDb"` olur.
- [ ] **Step 4: Veritabanını hazırla.** `ApplicationDbContextInitialiser.InitialiseAsync` içindeki `EnsureDeleted/EnsureCreated` yerine `Database.MigrateAsync()` gelir. Bağlantı adı: `ConnectionStrings:IdentityDb`. `appsettings.Development.json` içine geliştirme bağlantısı yazılır. Başlangıç migration'ı `Initial` adıyla oluşturulur.
- [ ] **Step 5: Web ayarlarını yap.**
  - `Program.cs` içinde `UseHttpsRedirection` yalnızca geliştirme dışında çalışır.
  - Herkese açık `UseCors(AllowAnyOrigin)` kaldırılır.
  - `launchSettings` `http` profili: `http://localhost:5100`.
- [ ] **Step 6: Test altyapısını yaz.** `IdentityApiFactory` ve `Health_ReturnsHealthy` testi (`GET /health` → 200).
- [ ] **Step 7: Build ve testleri çalıştır.**
  Run: `dotnet build services/identity/IdentityService.slnx && dotnet test services/identity/IdentityService.slnx`
  Expected: 0 hata; `Health_ReturnsHealthy` PASS.
- [ ] **Kontrol noktası.**

---

### Task 4: Kimlik domain modeli ve veritabanı

**Files:**
- Create: `src/Domain/Entities/{Tenant,TenantRole,TenantRolePermission,Membership,Invitation}.cs`
- Create: `src/Domain/Constants/Permissions.cs`, `src/Domain/Constants/SystemRoles.cs`
- Create: `src/Infrastructure/Identity/ApplicationUser.cs` (`IdentityUser<Guid>`)
- Create: `src/Infrastructure/Data/Configurations/*.cs`
- Modify: `src/Infrastructure/Data/ApplicationDbContext.cs` (`IdentityUserContext<ApplicationUser, Guid>` + `IDataProtectionKeyContext`)
- Modify: `src/Application/Common/Interfaces/IApplicationDbContext.cs`
- Test: `tests/Domain.UnitTests/SystemRolesTests.cs`

**Interfaces:**
- Produces:
  - `Permissions` sabitleri: `CompanyUpdate = "company.update"`, `MembersRead = "members.read"`, `MembersInvite = "members.invite"`, `MembersManage = "members.manage"`, `All = "*"`.
  - `SystemRoles`:
    - `public static IReadOnlyList<TenantRole> CreateDefaults(Guid tenantId)`: sırasıyla Sahip/`owner`, Yönetici/`admin`, Kullanıcı/`member`. Hepsinde `IsSystem = true`; izinler Global Constraints'teki gibi.
    - Sabitler: `Owner = "owner"`, `Admin = "admin"`, `Member = "member"`.
  - Varlıklar spec 7.2'deki alanlarla. `ApplicationUser` alanları: `FullName`, `CreatedAt`, `EmailConfirmedAt?`, `LastMembershipEndedAt?`, `CleanupReminderSentAt?`, `LastActiveTenantId?`.
  - `IApplicationDbContext` (Application katmanının gördüğü): `DbSet<Tenant> Tenants`, `DbSet<TenantRole> TenantRoles`, `DbSet<Membership> Memberships`, `DbSet<Invitation> Invitations`, `Task<int> SaveChangesAsync(CancellationToken)`.
  - Kısıtlar:
    - `Membership(TenantId, UserId)` benzersiz.
    - `Invitation(TenantId, Email)` benzersiz, filtre: `AcceptedAt IS NULL AND RevokedAt IS NULL`.
    - `Invitation.TokenHash` benzersiz.
    - `TenantRolePermission` anahtarı `(RoleId, Permission)`.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `CreateDefaults_CreatesThreeSystemRoles`: anahtarlar `owner/admin/member`.
  - `CreateDefaults_OwnerHasStar`.
  - `CreateDefaults_AdminHasFourPermissions`.
  - `CreateDefaults_MemberHasMembersRead`.
- [ ] **Step 2: Testleri çalıştır.** Run: `dotnet test services/identity/tests/Domain.UnitTests`. Expected: FAIL.
- [ ] **Step 3: Varlıkları, sabitleri ve EF yapılandırmasını yaz.** Data Protection anahtar tablosu: `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, uygulama adı `"Modaryo.Identity"`. `Initial` migration'ını sil ve yeniden oluştur.
  Run: `dotnet ef migrations add Initial -p src/Infrastructure -s src/Web -o Data/Migrations`
- [ ] **Step 4: Testleri çalıştır.** Run: `dotnet test services/identity/IdentityService.slnx`. Expected: PASS (`Health` testi migration'larla da geçer).
- [ ] **Kontrol noktası.**

---

### Task 5: Oturum deposu (yazıcı tarafı)

**Files:**
- Create: `src/Application/Common/Interfaces/ISessionStore.cs`
- Create: `src/Infrastructure/Sessions/SessionStore.cs`
- Test: `tests/Web.FunctionalTests/Sessions/SessionStoreTests.cs` (gerçek Valkey db 1)

**Interfaces:**
- Consumes: `Modaryo.Sessions.SessionRecord`, `SessionKeys`, `SessionTtl` (Task 2).
- Produces:
  ```csharp
  public sealed record NewSession(Guid UserId, string Email, string FullName, Guid? TenantId,
      string[] Permissions, bool RememberMe, string? Ip, string? UserAgent);
  public sealed record CreatedSession(string Sid, SessionRecord Record);
  public interface ISessionStore
  {
      Task<CreatedSession> CreateAsync(NewSession s, CancellationToken ct);
      Task<bool> UpdateTenantAsync(string sidHash, Guid? tenantId, string[] permissions, CancellationToken ct); // SET KEEPTTL XX
      Task UpdateTenantForUserAsync(Guid userId, Guid tenantId, Guid? newTenantId, string[] permissions, CancellationToken ct); // yalnızca tenant_id == tenantId olan oturumlar
      Task<IReadOnlyList<(string SidHash, SessionRecord Record)>> ListForUserAsync(Guid userId, CancellationToken ct); // bayat index üyelerini temizler
      Task DeleteAsync(string sidHash, CancellationToken ct);
      Task<bool> DeleteByPublicIdAsync(Guid userId, string publicId, CancellationToken ct);
      Task DeleteAllForUserAsync(Guid userId, string? exceptSidHash, CancellationToken ct);
  }
  ```
  Oluşturma `SET NX EX` ile yapılır. `user-sess:{userId}` SET'ine eklenir ve dizinin TTL'i 30 güne yenilenir. Güncellemeler `SET key value KEEPTTL XX` ile yapılır (StackExchange.Redis: `When.Exists` + `keepTtl: true`).

- [ ] **Step 1: Başarısız testleri yaz.**
  - `Create_WritesRecordWithTtlAndIndex`: TTL ≈ 8 saat; index SET'i hash'i içeriyor.
  - `UpdateContent_DoesNotReviveDeletedSession`: önce `DeleteAsync`, sonra `UpdateTenantAsync` → `false`; anahtar yok.
  - `UpdateContent_KeepsTtl`: `EXPIRE 100` → güncelleme → TTL ≤ 100.
  - `UpdateTenantForUser_OnlyTouchesMatchingTenant`: A ve B firmalarında birer oturum; A güncellenince B değişmiyor.
  - `DeleteAllForUser_ExceptCurrent`: üç oturumdan yalnız mevcut olan kalıyor.
  - `ListForUser_PrunesStaleIndexMembers`: süresi dolmuş anahtar listeden ve index'ten düşüyor.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: `SessionStore`'u yaz** ve DI'a ekle (`AddModaryoSessions` multiplexer'ını yeniden kullanır).
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 6: E-posta gönderimi ve şablonlar

**Files:**
- Create: `src/Application/Common/Interfaces/{IEmailSender,IEmailComposer}.cs`
- Create: `src/Infrastructure/Email/{SmtpEmailSender,EmailComposer,SmtpOptions}.cs`
- Create: `tests/Web.FunctionalTests/Infrastructure/FakeEmailSender.cs`
- Test: `tests/Web.FunctionalTests/Email/SmtpEmailSenderTests.cs`, `tests/Application.UnitTests/Email/EmailComposerTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);
  public interface IEmailSender { Task SendAsync(EmailMessage m, CancellationToken ct); }
  public interface IEmailComposer
  {
      EmailMessage ConfirmEmail(string to, string fullName, Guid userId, string token);
      EmailMessage ResetPassword(string to, string fullName, string token);
      EmailMessage AccountAlreadyExists(string to);
      EmailMessage Invitation(string to, string companyName, string inviterName, string inviteToken);
      EmailMessage CleanupReminder(string to, string fullName, DateTimeOffset deleteOn);
  }
  ```
  Linkler `App:FrontendBaseUrl` adresine gider:
  - `/eposta-dogrula?u={userId}&t={urlencoded token}`
  - `/sifre-sifirla?e={urlencoded email}&t={…}`
  - `/davet/{inviteToken}`

  SMTP ayarları: `Smtp:Host`, `Smtp:Port`, `Smtp:From` (geliştirmede `localhost`, `1025`, `Modaryo <no-reply@modaryo.local>`).

  `FakeEmailSender`: `List<EmailMessage> Sent`, `EmailMessage LastTo(string email)`, `string ExtractLink(EmailMessage m)` (metindeki ilk `http` linki).

- [ ] **Step 1: Başarısız testleri yaz.**
  - `ConfirmEmail_ContainsLinkWithEncodedToken`: token `a+b/c=` → linkte `a%2Bb%2Fc%3D`.
  - `Invitation_ContainsCompanyNameAndLink`.
  - Tüm şablonlar Türkçe konu içeriyor; örneğin doğrulama e-postasının konusu `"Modaryo: e-posta adresinizi doğrulayın"`.
  - `Smtp_DeliversToMailpit`: `SmtpEmailSender` ile gönder, ardından `GET http://localhost:8025/api/v1/search?query=to:{rastgele@test.local}` → 1 sonuç.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Yaz.** SMTP için MailKit kullanılır. HTML gövde sade bir yerleşim: tema rengi `#5D87FF`, buton görünümlü link, altında düz link.
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 7: Web katmanı altyapısı (oturum, CSRF, proxy, rate limit, izin)

**Files:**
- Create: `src/Web/Infrastructure/SessionCookie.cs`, `src/Web/Infrastructure/RateLimiting.cs`, `src/Web/Services/CurrentSession.cs`
- Create: `src/Application/Common/Interfaces/ICurrentSession.cs`, `src/Application/Common/Security/AuthErrorCodes.cs`
- Modify: `src/Application/Common/Security/AuthorizeAttribute.cs` (`Permission` özelliği eklenir; `Roles` ve `Policy` kaldırılır)
- Modify: `src/Application/Common/Behaviours/AuthorizationBehaviour.cs`
- Modify: `src/Web/Program.cs`, `src/Web/DependencyInjection.cs`, `src/Web/Services/CurrentUser.cs` (silinir; yerine `CurrentSession`)
- Test: `tests/Web.FunctionalTests/Pipeline/PipelineTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface ICurrentSession
  {
      Guid? UserId { get; } Guid? TenantId { get; } string? SidHash { get; } string? SessionId { get; }
      IReadOnlyList<string> Permissions { get; } bool HasPermission(string permission);
      string? Ip { get; } string? UserAgent { get; }
  }
  public static class AuthErrorCodes
  {
      public const string InvalidCredentials = "invalid_credentials";
      public const string EmailNotConfirmed = "email_not_confirmed";
      public const string AccountLocked = "account_locked";
      public const string InvalidToken = "invalid_token";
      public const string LastOwner = "last_owner";
      public const string InvitationEmailMismatch = "invitation_email_mismatch";
      public const string AlreadyMember = "already_member";
  }
  public sealed class AuthException(int status, string code, string message) : Exception(message);
  // SessionCookie
  public static void Write(HttpResponse r, CreatedSession s, bool secure);
  public static void Clear(HttpResponse r);
  // RateLimiting politika adları
  "login", "register", "email-sensitive"
  ```
  - `AuthException`, `ProblemDetailsExceptionHandler` içinde `status` + `extensions.code` olarak eşlenir.
  - `[Authorize(Permission = "...")]`: oturum yoksa `UnauthorizedAccessException` (401). İzin yoksa veya `TenantId` null ise `ForbiddenAccessException` (403).
  - Pipeline sırası: `UseForwardedHeaders` (KnownProxies = loopback) → `UseModaryoCsrf` → `UseRateLimiter` → `UseAuthentication` → `UseAuthorization`.
  - Rate limit değerleri `RateLimits:*` yapılandırmasından okunur; varsayılanlar Global Constraints'teki gibi. Rate limit aşılınca 429 döner.
  - Cookie: `Sessions:SecureCookie` ayarı (geliştirmede `false`). `remember_me` ise `Expires = absolute_expires_at`.

- [ ] **Step 1: Başarısız testleri yaz.** Test için `/api/identity/_test/...` gibi geçici uç noktalar açılmaz; testler Task 8–9'da gelecek gerçek uç noktalarla tamamlanır. Bu task'ta yalnızca şunlar test edilir:
  - `Post_WithoutCsrfHeader_Returns403WithCode`: herhangi bir POST rotası (`/api/identity/auth/logout`) → 403, `code = csrf_header_missing`.
  - `ForwardedFor_FromLoopback_IsUsedAsClientIp`: `CurrentSession.Ip` değeri, 9. task'taki `GET /auth/sessions` yanıtında görülür. Bu test Task 10'da tamamlanır; burada `[Ignore("Task 10")]` ile işaretlenir.
  - `AuthorizationBehaviour_WithoutTenant_Throws403`, `..._WithStar_Allows`, `..._MissingPermission_Throws403` (Application.UnitTests, sahte `ICurrentSession` ile).
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Interfaces bloğundakileri yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS (Ignore edilenler hariç).
- [ ] **Kontrol noktası.**

---

### Task 8: Kayıt, e-posta doğrulama, yeniden gönderme

**Files:**
- Create: `src/Application/Common/Interfaces/IUserAccounts.cs`, `src/Infrastructure/Identity/UserAccounts.cs`
- Create: `src/Application/Auth/ActiveTenantResolver.cs`, `src/Application/Auth/SessionInfo.cs`, `src/Web/Infrastructure/SessionIssuer.cs`
- Create: `src/Application/Auth/Commands/{Register,ConfirmEmail,ResendConfirmation}/*.cs` (komut + doğrulayıcı + handler)
- Create: `src/Web/Endpoints/Auth.cs` (`RoutePrefix = "/api/identity/auth"`)
- Test: `tests/Web.FunctionalTests/Auth/RegisterTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record UserSummary(Guid Id, string Email, string FullName, bool EmailConfirmed, Guid? LastActiveTenantId);
  public interface IUserAccounts   // UserManager sarmalayıcısı
  {
      Task<UserSummary?> FindByEmailAsync(string email, CancellationToken ct);
      Task<UserSummary?> FindByIdAsync(Guid id, CancellationToken ct);
      Task<UserSummary> CreateAsync(string email, string fullName, string password, bool emailConfirmed, CancellationToken ct); // ValidationException fırlatır
      Task<string> CreateEmailConfirmationTokenAsync(Guid userId);
      Task<bool> ConfirmEmailAsync(Guid userId, string token);     // EmailConfirmedAt'i yazar
      Task MarkEmailConfirmedAsync(Guid userId);                     // davet akışı için
      Task<PasswordCheck> CheckPasswordAsync(string email, string password); // kilit sayacını işletir
      Task<string> CreatePasswordResetTokenAsync(Guid userId);
      Task<bool> ResetPasswordAsync(string email, string token, string newPassword);
      Task<bool> ChangePasswordAsync(Guid userId, string current, string next);
      Task SetPasswordAsync(Guid userId, string password);           // doğrulanmamış hesap + davet
      Task UpdateProfileAsync(Guid userId, string fullName);
      Task SetLastActiveTenantAsync(Guid userId, Guid? tenantId);
      Task DeleteAsync(Guid userId);
  }
  public enum PasswordCheckResult { Success, InvalidCredentials, NotConfirmed, LockedOut }
  public sealed record PasswordCheck(PasswordCheckResult Result, UserSummary? User, DateTimeOffset? LockoutEnd);

  public sealed record CompanyRef(Guid Id, string Name, string RoleName);
  public sealed record UserDto(Guid Id, string Email, string FullName);
  public sealed record SessionInfo(UserDto User, CompanyRef? ActiveCompany, string[] Permissions, CompanyRef[] Companies);

  // src/Application/Auth/ActiveTenantResolver.cs
  Task<(Guid? TenantId, string[] Permissions)> ResolveAsync(Guid userId, Guid? preferredTenantId, CancellationToken ct);
  // Önce preferred (hâlâ üyeyse), sonra LastActiveTenantId, sonra en son JoinedAt; hiçbiri yoksa (null, [])
  Task<string[]> PermissionsForAsync(Guid userId, Guid tenantId, CancellationToken ct);
  Task<SessionInfo> BuildSessionInfoAsync(Guid userId, Guid? activeTenantId, CancellationToken ct);

  // src/Web/Infrastructure/SessionIssuer.cs — aktif firmayı çözer, ISessionStore.CreateAsync çağırır, cookie yazar
  Task<SessionInfo> IssueAsync(UserSummary user, bool rememberMe, HttpContext ctx, Guid? preferredTenantId = null);
  ```
  - Identity seçenekleri:
    - Şifre: `RequiredLength = 10`, diğer `Require*` = false; `MaxLength 128` doğrulayıcıda kontrol edilir.
    - Kilit: `MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 dakika`.
    - `User.RequireUniqueEmail = true`.
    - Doğrulama token ömrü 48 saat (ayrı bir `DataProtectorTokenProvider` adı), sıfırlama 1 saat.
  - Uç noktalar: `POST /register` → 202; `POST /confirm-email` → 200 + cookie + `SessionInfo` (Task 9); `POST /resend-confirmation` → 202.
  - Hepsi `RequireRateLimiting("register")` (kayıt) veya `"email-sensitive"` (yeniden gönder).

- [ ] **Step 1: Başarısız testleri yaz.**
  - `Register_NewEmail_Returns202AndSendsConfirmation`: `FakeEmailSender` doğrulama e-postası almış.
  - `Register_ExistingEmail_Returns202AndSendsAlreadyExists`: yanıt yeni kayıtla aynı; ikinci bir kullanıcı oluşmamış.
  - `Register_ShortPassword_Returns400`: 9 karakter.
  - `Register_NormalizesEmail`: `"  Ali@Ornek.COM "` ile kayıt ol → doğrula → `ali@ornek.com` ile giriş yap (Task 9'da tamamlanır; şimdilik `FindByEmailAsync("ali@ornek.com")` bulunuyor).
  - `ConfirmEmail_ValidLink_SetsConfirmedAndLogsIn`: e-postadaki linkten `u` ve `t` alınır → 200, `Set-Cookie: modaryo_sid=...; httponly; samesite=lax`.
  - `ConfirmEmail_TamperedToken_Returns400InvalidToken`.
  - `ResendConfirmation_UnknownEmail_Returns202AndSendsNothing`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Yaz.** Doğrulama sonrası oturum `SessionIssuer` ile açılır (yukarıdaki Interfaces bloğu). Bu servisi Task 9 de kullanır.
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 9: Giriş, çıkış ve oturum bilgisi

**Files:**
- Create: `src/Application/Auth/Commands/{Login,Logout,LogoutAll,SwitchTenant}/*.cs`, `src/Application/Auth/Queries/GetSession/*.cs`
- Modify: `src/Web/Endpoints/Auth.cs`
- Test: `tests/Web.FunctionalTests/Auth/LoginTests.cs`

**Interfaces:**
- Consumes: `IUserAccounts`, `ISessionStore`, `ICurrentSession`, `SessionIssuer`, `ActiveTenantResolver`, `SessionInfo` (Task 8).
- Produces:
  - `POST /login` `{email,password,rememberMe}`:
    - Başarılı → 200 + cookie + `SessionInfo`.
    - Yanlış bilgi → 401 `invalid_credentials`, mesaj "E-posta veya şifre hatalı.".
    - Doğrulanmamış hesap → 403 `email_not_confirmed`.
    - Kilitli hesap → 423 `account_locked`; mesajda kalan dakika yazar.
  - `POST /logout` → 204 + cookie silme.
  - `POST /logout-all` → 204.
  - `GET /session` → `SessionInfo` (oturum yoksa 401).
  - `POST /session/tenant` `{tenantId}` → 200 `SessionInfo`; üye değilse 403. `LastActiveTenantId` yazılır.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `Login_Valid_Returns200AndCookie`.
  - `Login_RememberMe_SetsPersistentCookieAnd14DayIdle`: cookie'de `expires` var; Valkey kaydında `idle_timeout_s == 1209600`.
  - `Login_WrongPassword_And_UnknownEmail_ReturnSameBody`.
  - `Login_Unconfirmed_Returns403Code`.
  - `Login_FiveFailures_LocksFor15Minutes`: 6. denemede doğru şifre → 423.
  - `Login_RateLimit_11thRequestInMinute_Returns429`. Bu testte rate limit açık bir factory örneği kullanılır.
  - `Logout_DeletesSessionKey`: çıkıştan sonra aynı cookie ile `GET /session` → 401.
  - `LogoutAll_DeletesEveryDevice`: iki client'tan giriş yap; `logout-all` sonrası ikisi de 401.
  - `Session_WithoutCompany_ReturnsNullActiveCompany`.
  - `Register_NormalizesEmail` (Task 8'deki test burada tamamlanır).
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 10: Şifre işlemleri, profil ve aktif oturumlar

**Files:**
- Create: `src/Application/Auth/Commands/{ForgotPassword,ResetPassword}/*.cs`
- Create: `src/Application/Account/Commands/{ChangePassword,UpdateProfile,RevokeSession}/*.cs`, `src/Application/Account/Queries/GetSessions/*.cs`
- Create: `src/Web/Endpoints/Account.cs` (`RoutePrefix = "/api/identity/account"`)
- Test: `tests/Web.FunctionalTests/Account/PasswordAndSessionTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record SessionListItem(string Id, DateTimeOffset CreatedAt, string? Ip, string? UserAgent, bool IsCurrent, bool RememberMe);
  ```
  - `POST /auth/forgot-password` → her zaman 202.
  - `POST /auth/reset-password` → 204 veya 400 `invalid_token`. Başarılıysa tüm oturumlar silinir.
  - `POST /account/change-password` → 204; mevcut oturum dışındaki tüm oturumlar silinir. Mevcut şifre yanlışsa 400.
  - `PATCH /account/profile` `{fullName}` → 204. Kullanıcının tüm oturumlarında `full_name` alanı güncellenmez; yeni girişte güncel değer gelir (yalnızca `GET /session` DB'den okur). Bu bilinçli bir sadeleştirme.
  - `GET /account/sessions` → `SessionListItem[]`.
  - `DELETE /account/sessions/{id}` → 204 veya 404.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `ForgotPassword_UnknownEmail_Returns202NoMail`.
  - `ResetPassword_ValidToken_ChangesPasswordAndKillsAllSessions`.
  - `ResetPassword_TokenOlderThan1Hour_Returns400`. Süre `FakeTimeProvider` ile ilerletilir; token sağlayıcı `TimeProvider` kullanmalı.
  - `ChangePassword_KeepsCurrentSessionKillsOthers`.
  - `Sessions_ListShowsCurrentFlagAndForwardedIp`: istek `X-Forwarded-For: 203.0.113.5` başlığıyla gönderilir → listede `Ip == "203.0.113.5"`. Bu, Task 7'deki Ignore edilmiş testi tamamlar.
  - `RevokeSession_OtherUsersSessionId_Returns404`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 11: Firma, roller ve üyeler

**Files:**
- Create: `src/Application/Companies/Commands/{CreateCompany,UpdateCompany,LeaveCompany,ChangeMemberRole,RemoveMember}/*.cs`
- Create: `src/Application/Companies/Queries/{GetCurrentCompany,GetRoles,GetMembers}/*.cs`
- Create: `src/Application/Companies/MembershipChanges.cs`. Üyelik değiştiğinde oturumları ve `LastMembershipEndedAt` alanını güncelleyen tek yer burası.
- Create: `src/Web/Endpoints/Companies.cs` (`RoutePrefix = "/api/identity/companies"`)
- Test: `tests/Web.FunctionalTests/Companies/CompanyTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record CompanyDto(Guid Id, string Name, DateTimeOffset CreatedAt);
  public sealed record RoleDto(Guid Id, string Name, string? SystemKey);
  public sealed record MemberDto(Guid UserId, string Email, string FullName, Guid RoleId, string RoleName, DateTimeOffset JoinedAt);
  // MembershipChanges
  Task OnJoinedAsync(Guid userId, Guid tenantId, string? currentSidHash, CancellationToken ct); // aktif firmayı yeni firma yapar
  Task OnRoleChangedAsync(Guid userId, Guid tenantId, CancellationToken ct);
  Task OnLeftAsync(Guid userId, Guid tenantId, CancellationToken ct); // oturumlarda tenant=null; başka üyeliği yoksa LastMembershipEndedAt=now
  ```
  - `POST /companies` `{name}` (2–100 karakter) → 201 `CompanyDto`. Hazır roller oluşur, kullanıcı sahip olur, `OnJoinedAsync` çağrılır.
  - `GET /current` → 200 `CompanyDto`; aktif firma yoksa 404.
  - `PATCH /current` → `company.update` izni gerekir.
  - `POST /current/leave` → son sahipse 409 `last_owner`.
  - `GET /current/roles`, `GET /current/members` → `members.read` izni gerekir.
  - `PATCH /current/members/{userId}` `{roleId}` → `members.manage` izni gerekir. `owner` rolünü yalnızca bir sahip verebilir veya geri alabilir (aksi halde 403). Son sahip düşürülemez (409 `last_owner`).
  - `DELETE /current/members/{userId}` → `members.manage` izni gerekir. Son sahip silinemez; sahibi yalnızca sahip silebilir.
  - Tüm sorgular `TenantId == ICurrentSession.TenantId` ile filtrelenir; başka firmaya ait id'ler 404 döner.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `CreateCompany_MakesCallerOwnerAndSwitchesSession`: `GET /auth/session` → `ActiveCompany` yeni firma, `Permissions == ["*"]`.
  - `CreateCompany_WhenAlreadyInCompany_AddsSecondMembership`: `Companies.Length == 2`.
  - `SwitchTenant_ToCompanyNotMember_Returns403`.
  - `LastOwner_CannotDemoteLeaveOrBeRemoved`: üç işlemin üçü de 409 `last_owner`.
  - `ChangeRole_UpdatesTargetsLiveSessionPermissions`: hedef kullanıcının açık oturumunda `GET /auth/session` → izinler yeni rolün izinleri.
  - `RemoveMember_TargetSessionLosesTenant`: hedefin `GET /auth/session` yanıtında `ActiveCompany == null`. Başka firması yoksa `LastMembershipEndedAt` dolu.
  - `ChangeRole_ForMemberOfOtherTenant_Returns404`.
  - `Admin_CannotGrantOwner_Returns403`.
  - `Member_CannotListInvitations_Returns403` (`members.invite` izni yok).
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 12: Davetler

**Files:**
- Create: `src/Application/Invitations/Commands/{CreateInvitation,ResendInvitation,RevokeInvitation,AcceptInvitation}/*.cs`
- Create: `src/Application/Invitations/Queries/{ListInvitations,GetInvitationPreview,GetMyInvitations}/*.cs`
- Modify: `src/Application/Auth/Commands/Register/*` (`InviteToken` dalı)
- Create: `src/Web/Endpoints/Invitations.cs` (`RoutePrefix = "/api/identity/invitations"`). Firma altındaki davet rotaları `Companies.cs` içine eklenir; hesaba ait davetler `Account.cs` içine (`GET /account/invitations`).
- Test: `tests/Web.FunctionalTests/Invitations/InvitationTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record InvitationDto(Guid Id, string Email, Guid RoleId, string RoleName, DateTimeOffset ExpiresAt, string InvitedBy);
  public sealed record InvitationPreview(string CompanyName, string InvitedBy, string Email, DateTimeOffset ExpiresAt, bool AccountExists);
  ```
  - `POST /companies/current/invitations` `{email, roleId}` → 201. Davet e-postası gider. Kişi zaten üyeyse 409 `already_member`. Bekleyen davet varsa yeniden gönderme gibi davranır (eski token geçersiz olur). Sahip rolüyle davet yalnızca sahip gönderebilir.
  - `GET /invitations/{token}` → `InvitationPreview`; geçersiz veya süresi dolmuş token için 404. `AccountExists` alanı yalnızca token sahibine gösterilir; token e-posta sahipliğini kanıtladığı için bu bir sızıntı sayılmaz.
  - `POST /invitations/{token}/accept` → oturum gerekir. Oturumdaki e-posta davetle aynı değilse 403 `invitation_email_mismatch`. Başarılıysa üyelik oluşur, `OnJoinedAsync` çağrılır ve `SessionInfo` döner.
  - `POST /auth/register` `{..., inviteToken}`:
    - Token geçerli ve e-posta eşleşiyorsa: hesap yoksa doğrulanmış olarak oluşturulur. Hesap var ama doğrulanmamışsa `SetPasswordAsync` + `MarkEmailConfirmedAsync` çağrılır. Ardından davet kabul edilir, oturum açılır ve 200 `SessionInfo` döner.
    - Hesap var ve doğrulanmışsa 409 döner, mesaj "Bu e-postayla bir hesap var, giriş yapıp daveti kabul edin.". Token e-posta sahipliğini kanıtladığı için bu bir sızıntı sayılmaz.
    - Token geçersizse normal kayıt akışına düşer.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `Invite_NewPerson_RegisterViaLink_JoinsAndIsConfirmed`.
  - `Invite_UnverifiedExistingAccount_RegisterViaLink_ConfirmsAndJoins`.
  - `Accept_WrongAccount_Returns403Mismatch`.
  - `Accept_MatchesEmailCaseInsensitively`: davet `Veli@Ornek.com` adresine; hesap `veli@ornek.com`.
  - `Accept_WhenAlreadyInOtherCompany_AddsMembershipAndSwitches`.
  - `Invite_ExistingMember_Returns409`.
  - `Resend_InvalidatesOldToken`: eski token → 404.
  - `Accept_ExpiredAfter7Days_Returns404` (`FakeTimeProvider` ile).
  - `RevokeInvitation_OfOtherTenant_Returns404`.
  - `MyInvitations_ListsPendingForSessionEmail`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 13: Otomatik temizlik ve geliştirme verisi

**Files:**
- Create: `src/Application/Maintenance/Commands/RunAccountCleanup/*.cs`
- Create: `src/Infrastructure/Maintenance/AccountCleanupWorker.cs` (`BackgroundService`, `PeriodicTimer` 24 saat; `Maintenance:CleanupEnabled` ayarıyla açılıp kapanır, testlerde kapalı)
- Modify: `src/Infrastructure/Data/ApplicationDbContextInitialiser.cs` (geliştirme seed'i)
- Test: `tests/Web.FunctionalTests/Maintenance/CleanupTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record CleanupReport(int UnconfirmedDeleted, int RemindersSent, int CompanylessDeleted);
  public sealed record RunAccountCleanupCommand : IRequest<CleanupReport>;
  ```
  - Silinen kullanıcının oturumları `DeleteAllForUserAsync` ile silinir.
  - Geliştirme seed'i: kullanıcı `demo@modaryo.local` (adı "Demo Kullanıcı", doğrulanmış, şifre `Seed:DemoPassword` ayarından), firma "Demo Firma", rol sahip. Yalnızca `IsDevelopment()` ortamında ve kullanıcı yoksa oluşturulur.

- [ ] **Step 1: Başarısız testleri yaz** (`FakeTimeProvider` ile).
  - `Unconfirmed_Day8_Deleted_Day6_Kept`.
  - `Companyless_Day23_ReminderOnce`: komut iki kez çalıştırılır → 1 e-posta.
  - `Companyless_Day30_DeletedWithSessions`.
  - `RemovedFromLastCompany_ClockStartsAtRemoval`: kayıttan 40 gün sonra çıkarılan kullanıcı, çıkarıldıktan 1 gün sonra silinmiyor.
  - `UserWithMembership_NeverTouched`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 14: ERP servisinin dönüşümü

**Files:**
- Delete: `services/erp` içinde:
  - TodoItems, TodoLists, WeatherForecasts özellikleri, uç noktaları, varlıkları, yapılandırmaları ve testleri
  - `Domain/ValueObjects/Colour.cs` ve bağımlıları
  - Identity kısmı: `Infrastructure/Identity/*`, `Web/Endpoints/Users.cs`, `IIdentityService`, `Domain/Constants/Roles.cs`
  - `src/AppHost`, `tests/TestAppHost`
- Modify: `ERP.slnx` (`Modaryo.Sessions` eklenir), `Directory.Packages.props` (Npgsql; SQLite ve Aspire testing kaldırılır), `Infrastructure/DependencyInjection.cs`, `Web/Program.cs`, `Web/DependencyInjection.cs`, `AuthorizeAttribute` ve `AuthorizationBehaviour` (Task 7'deki izin tabanlı sürüm)
- Create: `src/Application/Common/Interfaces/ICurrentTenant.cs`, `src/Domain/Common/ITenantOwned.cs`, `src/Web/Endpoints/Me.cs` (`RoutePrefix = "/api/erp"`)
- Modify: `ApplicationDbContext`. `ITenantOwned` uygulayan her varlığa global sorgu filtresi; `SaveChanges` sırasında `TenantId` otomatik atanır (interceptor).
- Test: `tests/Web.FunctionalTests` (yeniden adlandırılan fonksiyonel test projesi)

**Interfaces:**
- Consumes: `Modaryo.Sessions` (Task 2).
- Produces:
  ```csharp
  public interface ICurrentTenant { Guid? TenantId { get; } }
  public interface ITenantOwned { Guid TenantId { get; set; } }
  public sealed record MeDto(Guid UserId, string Email, string FullName, Guid? TenantId, string[] Permissions);
  // GET /api/erp/me → 200 MeDto | 401
  ```
  - Veritabanı: `ConnectionStrings:ErpDb` (geliştirmede `erp` veritabanı). İlk migration `Initial` adıyla oluşturulur; şimdilik boştur, yalnızca migration geçmiş tablosu.
  - Testlerde oturum kaydı doğrudan Valkey db 1'e yazılır (kimlik servisini başlatmadan). JSON kaynağı `docs/contracts/session-v1.example.json` olur; alanlar test içinde değiştirilir.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `Me_WithValidSession_ReturnsIdentityFromRecord`.
  - `Me_WithoutCookie_Returns401`.
  - `Me_AfterKeyDeleted_Returns401`.
  - `Post_WithoutCsrfHeader_Returns403`. Bu test için ERP'de geçici bir POST uç noktası açılmaz; `Modaryo.Sessions` middleware'i tüm POST isteklerine uygulandığından herhangi bir rotaya (`POST /api/erp/me` → 403, rota olmasa bile middleware önce çalışır) istek yeterlidir.
  - `TenantFilter_HidesOtherTenantsRows`. Testlere özel bir `TestTenantOwnedEntity` varlığı yalnızca test DbContext'inde kaydedilir; üretim modeline eklenmez. Gerekirse bu test, ilk gerçek tenant varlığı geldiği modüle ertelenir ve not düşülür.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Silme ve değişiklik listesini uygula, ardından kodu yaz.**
- [ ] **Step 4: Build ve testleri çalıştır.**
  Run: `dotnet build services/erp/ERP.slnx && dotnet test services/erp/ERP.slnx`
  Expected: 0 hata, PASS.
- [ ] **Kontrol noktası.**

---

### Task 15: Çalıştırma ayarları ve uçtan uca doğrulama

**Files:**
- Modify: `.claude/launch.json` (`identity`: `dotnet run --project services/identity/src/Web --launch-profile http`, port 5100; `erp`: aynı biçimde, port 5282)
- Modify: `README.md` (servisleri çalıştırma, demo kullanıcı, Mailpit)

- [ ] **Step 1: Altyapıyı ve iki servisi başlat.** Her iki servis de migration'ları uygulamalı ve `/health` 200 dönmeli.
- [ ] **Step 2: Uçtan uca akışı curl ile çalıştır.** Bir cookie jar dosyası kullanılır; tüm POST isteklerinde `-H 'X-Modaryo-Csrf: 1'` başlığı olur.
  1. `POST http://localhost:5100/api/identity/auth/register` → 202.
  2. `GET http://localhost:8025/api/v1/search?query=to:<e-posta>` → doğrulama e-postası; linkten `u` ve `t` alınır.
  3. `POST /auth/confirm-email` → 200, cookie alındı.
  4. `POST /companies {name:"Test A.Ş."}` → 201.
  5. Aynı cookie ile `GET http://localhost:5282/api/erp/me` → 200; `tenantId` firma id'si, `permissions == ["*"]`.

     Bu adım, iki servisin oturum sözleşmesinde gerçekten anlaştığının kanıtıdır.
  6. `POST /auth/logout` → 204; aynı cookie ile ERP `/me` → 401.

  Expected: Her adım beklenen kodu döner.
- [ ] **Step 3: Tüm test paketlerini çalıştır.**
  Run: `dotnet test libs/dotnet/Modaryo.Sessions.Tests && dotnet test services/identity/IdentityService.slnx && dotnet test services/erp/ERP.slnx`
  Expected: Hepsi PASS.
- [ ] **Kontrol noktası.** Backend tamamlandı. Frontend planı (spec 5. aşama) ayrıca yazılacak.
