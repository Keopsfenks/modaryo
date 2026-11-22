# Kimlik Doğrulama — Frontend Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Next.js frontend'e giriş, kayıt, şifre işlemleri, e-posta doğrulama, davet, başlangıç (firma oluştur / davet kabul), hesabım ve firma üyeleri ekranlarını eklemek ve paneli gerçek oturuma bağlamak.

**Architecture:**
- **Proxy:** Tarayıcı yalnızca `localhost:3000` ile konuşur. Next.js `rewrites` ayarı `/api/identity/*` isteklerini kimlik servisine, `/api/erp/*` isteklerini ERP servisine iletir. Böylece `modaryo_sid` cookie'si birinci taraf olur.
- **API çağrıları:** Tüm çağrılar tek bir istemciden (`apiFetch`) geçer. Bu istemci GET dışındaki isteklere `X-Modaryo-Csrf: 1` başlığını ekler ve hata yanıtlarını (ProblemDetails) tipli bir `ApiError` nesnesine çevirir.
- **Sayfa koruması:** `src/proxy.ts` cookie yoksa kullanıcıyı `/giris` sayfasına yönlendirir. Oturumun gerçekten geçerli olup olmadığına ise paneldeki `SessionProvider` karar verir (`GET /api/identity/auth/session`): oturum yoksa `/giris`, firma yoksa `/baslangic`.

**Tech Stack:** Next.js 16 (App Router), React 19, MUI 9, @tabler/icons-react; testler için Vitest 5 + React Testing Library + user-event + jsdom + MSW 3 (sahte API).

**Spec:** `docs/superpowers/specs/2026-11-21-kimlik-dogrulama-design.md`, özellikle 9. bölüm (frontend) ve 7.4–7.5 (uç noktalar ve kurallar). Backend'in nasıl yapıldığı ve oradaki kararlar: `docs/superpowers/plans/2026-11-21-kimlik-dogrulama-backend.md` + `.superpowers/sdd/2026-11-21-kimlik-dogrulama-backend/progress.md`.

## Global Constraints

- **Git:** Repo git deposu değil. "Commit" adımları yerine "Kontrol noktası" adımları var.
- **Next.js 16 kuralları:** `middleware` yerine `src/proxy.ts` (fonksiyon adı `proxy`) kullanılır. API kodu yazmadan önce `node_modules/next/dist/docs/` altındaki ilgili belge okunur (`frontend/AGENTS.md` kuralı).
- **Stil:** Yeni dosyalar 4 boşluk girinti kullanır, çift tırnak, `"use client"` yalnızca gereken yerde.
- **Dil ve görünüm:**
  - Arayüz metinleri Türkçedir. Sunucu hata başlıkları (ProblemDetails `title`) zaten Türkçe olduğu için doğrudan gösterilebilir.
  - Mevcut tema (`src/theme/theme.ts`) ve bileşen dili kullanılır, yeni renk tanımlanmaz.
- **Rotalar:**
  - **Giriş öncesi:** `/giris`, `/kayit`, `/sifremi-unuttum`, `/sifre-sifirla`, `/eposta-dogrula`, `/davet/[token]`.
  - **Oturum gerekir, panel dışı:** `/baslangic`.
  - **Panel içi:** `/hesap`, `/firma/uyeler`.
- **Backend adresleri:** Ortam değişkenleri `IDENTITY_API_URL` (varsayılan `http://localhost:5100`) ve `ERP_API_URL` (varsayılan `http://localhost:5282`).
- **Backend sözleşmesi** (değiştirilmez):
  - Cookie `modaryo_sid`, CSRF başlığı `X-Modaryo-Csrf: 1`.
  - JSON alanları camelCase.
  - Hata yanıtı ProblemDetails: `{ status, title, code?, errors? }`. `errors` alanı yalnızca doğrulama hatalarında gelir: `{ "Password": ["..."] }` biçiminde, anahtarlar PascalCase.
- **Doğrulama kuralları** (backend'le aynı):
  - E-posta: zorunlu, en fazla 256 karakter.
  - Şifre: 10–128 karakter.
  - Ad soyad: 2–100 karakter.
  - Firma adı: 2–100 karakter.
- **Yönlendirme sonrası adres (`next`):** Yalnızca `/` ile başlayan ve `//` ile başlamayan bir yol kabul edilir. Diğer her şey `/` olarak ele alınır (açık yönlendirme açığına karşı).

## Review Focus

1. **Cookie silinmiş ama sekme açık:** Herhangi bir API çağrısı 401 dönerse kullanıcı `/giris?next=<mevcut yol>` sayfasına gider. Sonsuz döngü olmamalı: `/giris` sayfası oturum kontrolü yapmaz. → Task 6 `SessionProvider_On401_RedirectsToLoginWithNext`.
2. **Açık yönlendirme:** `/giris?next=https://kotu.site` veya `next=//kotu.site` ile giriş yapan kullanıcı `/` adresine gider. → Task 3 `Login_IgnoresExternalNext`.
3. **Çift tıklama:** Form gönderilirken buton devre dışı kalır ve ikinci istek atılmaz (kayıt ikinci kez e-posta göndermesin). → Task 4 `Register_DisablesSubmitWhileSending`.
4. **Yetkisiz kullanıcıya gizli butonlar:** `members.read` izni olan ama `members.manage` izni olmayan kullanıcı rol değiştirme/çıkarma kontrollerini görmez. `members.invite` izni yoksa davet bölümünü görmez. → Task 10 `Members_HidesManageControlsWithoutPermission`.
5. **Sunucu doğrulama hataları alana düşer:** Backend 400 + `errors.Password` döndürürse mesaj şifre alanının altında görünür, genel uyarı olarak değil. → Task 1 `apiFetch_MapsValidationErrorsToFields` + Task 4 `Register_ShowsServerFieldError`.

---

## Dosya yapısı

```
frontend/
  next.config.ts                         # rewrites
  vitest.config.mts, vitest.setup.ts
  src/proxy.ts                           # cookie yoksa /giris
  src/lib/api/client.ts                  # apiFetch, ApiError
  src/lib/api/identity.ts                # uç nokta fonksiyonları + tipler
  src/lib/navigation.ts                  # safeNext()
  src/session/SessionProvider.tsx        # oturum bağlamı, useSession()
  src/test/                              # render yardımcıları, MSW sunucusu, sahte veriler
  src/components/auth/                   # AuthCard, PasswordField, FormAlert
  src/app/(auth)/layout.tsx              # ortalanmış kart düzeni
  src/app/(auth)/giris/page.tsx  kayit/  sifremi-unuttum/  sifre-sifirla/  eposta-dogrula/  davet/[token]/
  src/app/baslangic/page.tsx             # panel dışı, oturum gerekir
  src/app/(panel)/hesap/page.tsx
  src/app/(panel)/firma/uyeler/page.tsx
  src/components/layout/Header.tsx       # (değişir) gerçek kullanıcı, çıkış, firma değiştirici
  src/config/navigation.ts               # (değişir) "Firma Üyeleri"
```

Sayfa bileşenleri, test edilebilmeleri için ince tutulur. Her sayfa bir istemci bileşenini sarar (örneğin `giris/page.tsx` → `LoginForm.tsx`). Testler istemci bileşenini render eder.

---

### Task 1: Test altyapısı ve API istemcisi

**Files:**
- Modify: `frontend/package.json`
  - devDependencies: `vitest`, `@vitejs/plugin-react`, `jsdom`, `@testing-library/react`, `@testing-library/dom`, `@testing-library/user-event`, `@testing-library/jest-dom`, `msw`, `vite-tsconfig-paths`. Sürümler o günün kararlısı.
  - scripts: `"test": "vitest run"`, `"test:watch": "vitest"`.
- Create: `frontend/vitest.config.mts` (jsdom, `setupFiles: ["./vitest.setup.ts"]`, tsconfig yolları), `frontend/vitest.setup.ts` (jest-dom matchers, MSW sunucusu başlat/sıfırla/kapat)
- Create: `src/test/server.ts` (MSW `setupServer()`), `src/test/render.tsx` (`renderWithTheme(ui)`: `ThemeRegistry` ile sarar), `src/test/fixtures.ts` (örnek `SessionInfo`, üye, davet nesneleri)
- Create: `src/lib/api/client.ts`
- Test: `src/lib/api/client.test.ts`

**Interfaces:**
- Produces:
  ```ts
  export type ProblemBody = { status: number; title?: string; code?: string; errors?: Record<string, string[]> };
  export class ApiError extends Error {
      status: number; code?: string;
      fieldErrors: Record<string, string>;   // anahtarlar camelCase ("password"), her alanın ilk mesajı
  }
  export async function apiFetch<T>(path: string, init?: { method?: string; body?: unknown; signal?: AbortSignal }): Promise<T>;
  // 204/202 → undefined as T. JSON gövde → parse. Hata → ApiError (gövde yoksa title = "Beklenmeyen bir hata oluştu.")
  export const CSRF_HEADER = "X-Modaryo-Csrf";
  ```

- [ ] **Step 1: Test altyapısını kur.** Paketleri yükle; config ve setup dosyalarını yaz.
  Run: `npm --prefix frontend test` → Expected: "No test files found" (altyapı çalışıyor).
- [ ] **Step 2: Başarısız testleri yaz** (`client.test.ts`, MSW ile):
  - `apiFetch_PostSendsCsrfHeaderAndJson`: POST isteğinde `X-Modaryo-Csrf` = `"1"` ve `content-type: application/json`.
  - `apiFetch_GetSendsNoCsrfHeader`.
  - `apiFetch_204ReturnsUndefined`.
  - `apiFetch_ThrowsApiErrorWithCodeAndTitle`: 423 `{title:"…15 dakika kilitli.", code:"account_locked"}` → `status 423`, `code "account_locked"`, `message` = title.
  - `apiFetch_MapsValidationErrorsToFields`: 400 `{errors:{Password:["Şifre en az 10 karakter olmalıdır."], CurrentPassword:["x"]}}` → `fieldErrors` = `{ password: "Şifre en az 10…", currentPassword: "x" }`.
  - `apiFetch_NonJsonErrorBody_UsesGenericTitle`: 500 düz metin → `message` "Beklenmeyen bir hata oluştu.".
- [ ] **Step 3: Testleri çalıştır.** Run: `npm --prefix frontend test` → Expected: FAIL.
- [ ] **Step 4: `client.ts`'i yaz.**
- [ ] **Step 5: Testleri çalıştır.** Run: `npm --prefix frontend test` → Expected: PASS. Ayrıca `npm --prefix frontend run lint` temiz olmalı.
- [ ] **Kontrol noktası.**

---

### Task 2: Proxy yönlendirmeleri, sayfa koruması ve kimlik API modülü

**Files:**
- Modify: `frontend/next.config.ts`. `rewrites()` iki kural döndürür:
  - `/api/identity/:path*` → `${IDENTITY_API_URL}/api/identity/:path*`
  - `/api/erp/:path*` → `${ERP_API_URL}/api/erp/:path*`
- Create: `src/proxy.ts`, `src/lib/navigation.ts`, `src/lib/api/identity.ts`
- Test: `src/proxy.test.ts`, `src/lib/navigation.test.ts`

**Interfaces:**
- Produces:
  ```ts
  // navigation.ts
  export const PUBLIC_PATHS: string[]; // ["/giris","/kayit","/sifremi-unuttum","/sifre-sifirla","/eposta-dogrula","/davet"]
  export function isPublicPath(pathname: string): boolean;          // "/davet/abc" → true
  export function safeNext(value: string | null | undefined): string; // "/" ile başlayan, "//" ile başlamayan yol; aksi "/"
  export function loginUrl(nextPath: string): string;               // "/giris?next=%2Fhesap"
  // proxy.ts — matcher: API, _next, statik dosyalar ve favicon hariç her şey
  export function proxy(request: NextRequest): NextResponse;        // cookie yok + korumalı yol → 307 loginUrl; aksi next()
  // identity.ts — backend uç noktalarının birebir tipli sarmalayıcıları
  export type CompanyRef = { id: string; name: string; roleName: string };
  export type SessionInfo = { user: { id: string; email: string; fullName: string }; activeCompany: CompanyRef | null; permissions: string[]; companies: CompanyRef[] };
  export type SessionListItem = { id: string; createdAt: string; ip: string | null; userAgent: string | null; isCurrent: boolean; rememberMe: boolean };
  export type RoleDto = { id: string; name: string; systemKey: string | null };
  export type MemberDto = { userId: string; email: string; fullName: string; roleId: string; roleName: string; roleSystemKey: string | null; joinedAt: string };
  export type InvitationDto = { id: string; email: string; roleId: string; roleName: string; expiresAt: string; invitedBy: string };
  export type MyInvitationDto = { id: string; companyName: string; invitedBy: string; roleName: string; expiresAt: string };
  export type InvitationPreview = { companyName: string; invitedBy: string; email: string; expiresAt: string; accountExists: boolean };
  export const identity = {
      register(body: { email: string; password: string; fullName: string; inviteToken?: string }): Promise<SessionInfo | undefined>, // 202 → undefined, 200 (davetli) → SessionInfo
      confirmEmail(body: { userId: string; token: string }): Promise<SessionInfo>,
      resendConfirmation(email: string): Promise<void>,
      login(body: { email: string; password: string; rememberMe: boolean }): Promise<SessionInfo>,
      logout(): Promise<void>, logoutAll(): Promise<void>,
      getSession(): Promise<SessionInfo>, switchTenant(tenantId: string): Promise<SessionInfo>,
      forgotPassword(email: string): Promise<void>,
      resetPassword(body: { email: string; token: string; newPassword: string }): Promise<void>,
      changePassword(body: { currentPassword: string; newPassword: string }): Promise<void>,
      updateProfile(fullName: string): Promise<void>,
      getSessions(): Promise<SessionListItem[]>, revokeSession(id: string): Promise<void>,
      getMyInvitations(): Promise<MyInvitationDto[]>, acceptMyInvitation(id: string): Promise<SessionInfo>,
      getInvitationPreview(token: string): Promise<InvitationPreview>, acceptInvitation(token: string): Promise<SessionInfo>,
      createCompany(name: string): Promise<{ id: string; name: string; createdAt: string }>,
      updateCompany(name: string): Promise<void>, leaveCompany(): Promise<void>,
      getRoles(): Promise<RoleDto[]>, getMembers(): Promise<MemberDto[]>,
      changeMemberRole(userId: string, roleId: string): Promise<void>, removeMember(userId: string): Promise<void>,
      listInvitations(): Promise<InvitationDto[]>, createInvitation(email: string, roleId: string): Promise<InvitationDto>,
      resendInvitation(id: string): Promise<void>, revokeInvitation(id: string): Promise<void>,
  };
  ```
  Uç nokta yolları backend planındaki tablodadır (`/api/identity/auth/...`, `/account/...`, `/companies/current/...`, `/invitations/{token}`).

- [ ] **Step 1: Başarısız testleri yaz.**
  - `safeNext`: `"/hesap"` → `"/hesap"`; `"https://kotu.site"`, `"//kotu.site"`, `""`, `null`, `"javascript:alert(1)"` → `"/"`; `"/firma/uyeler?x=1"` korunur.
  - `isPublicPath`: `"/giris"` true, `"/davet/abc"` true, `"/hesap"` false, `"/"` false.
  - `proxy` (`new NextRequest("http://localhost:3000/hesap")` ile):
    - `Proxy_NoCookie_ProtectedPath_RedirectsToLoginWithNext`: `location` = `/giris?next=%2Fhesap`.
    - `Proxy_WithCookie_Passes`.
    - `Proxy_PublicPath_Passes`.
    - `Proxy_Baslangic_WithoutCookie_Redirects`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: `navigation.ts`, `proxy.ts`, `identity.ts` ve `next.config.ts`'i yaz.** `proxy.ts` yazılmadan önce `node_modules/next/dist/docs/01-app/03-api-reference/03-file-conventions/proxy.md` okunur.
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Step 5: Rewrites ve IP iletimini canlı doğrula.**
  - `infra/up.sh` çalıştır; identity, erp ve frontend'i `.claude/launch.json` ile başlat.
  - `curl -i http://localhost:3000/api/identity/auth/session` → 401 ProblemDetails (kimlik servisinden).
  - `curl -s -c jar -H "X-Modaryo-Csrf: 1" -H "Content-Type: application/json" -d '{"email":"demo@modaryo.local","password":"Demo-Modaryo-2026"}' http://localhost:3000/api/identity/auth/login` → 200 ve `Set-Cookie: modaryo_sid`.
  - Aynı jar ile `GET http://localhost:3000/api/erp/me` → 200.
  - Ardından `GET http://localhost:3000/api/identity/account/sessions`: listedeki oturumun `ip` alanı istemci adresi olmalı. Bu, Next.js'in `X-Forwarded-For` başlığını ilettiğini gösterir; boşsa veya `::1` ise ledger'a ruling yazılır ve spec 6.8'deki "Next.js tarafında eklenecek" maddesi uygulanır.
- [ ] **Kontrol noktası.**

---

### Task 3: Giriş öncesi sayfa düzeni ve Giriş sayfası

**Files:**
- Create: `src/app/(auth)/layout.tsx`. Ortalanmış tek kart: üstte `Logo`, altında içerik; sağ üstte `ThemeModeToggle`. Arka plan `background.default`, kart genişliği 420 px (mobilde tam genişlik).
- Create: `src/components/auth/AuthCard.tsx` (`title`, `subtitle?`, `children`, `footer?`), `src/components/auth/PasswordField.tsx` (göster/gizle butonlu `TextField`), `src/components/auth/FormAlert.tsx` (`error: ApiError | string | null` → MUI `Alert`)
- Create: `src/app/(auth)/giris/page.tsx`, `src/app/(auth)/giris/LoginForm.tsx`
- Test: `src/app/(auth)/giris/LoginForm.test.tsx`

**Interfaces:**
- Consumes: `identity.login`, `identity.resendConfirmation`, `safeNext` (Task 2).
- Produces: `AuthCard`, `PasswordField`, `FormAlert` (sonraki auth sayfaları kullanır).
  - `LoginForm` başarılı girişte `router.replace(safeNext(searchParams.get("next")))` çağırır. Aktif firma `null` ise `/baslangic`'e gider.
  - Alt bağlantılar: "Şifremi unuttum" (`/sifremi-unuttum`) ve "Hesabın yok mu? Kayıt ol" (`/kayit`).

- [ ] **Step 1: Başarısız testleri yaz.** `next/navigation` `vi.mock` ile taklit edilir: `useRouter().replace` casusu ve `useSearchParams`. API MSW ile taklit edilir.
  - `Login_Success_RedirectsToNext`: `next=/hesap` → `replace("/hesap")`.
  - `Login_WithoutCompany_RedirectsToBaslangic`.
  - `Login_IgnoresExternalNext`: `next=https://kotu.site` → `replace("/")`.
  - `Login_InvalidCredentials_ShowsServerTitle`: ekranda "E-posta veya şifre hatalı.".
  - `Login_EmailNotConfirmed_OffersResend`: 403 `email_not_confirmed` → "Doğrulama e-postasını yeniden gönder" butonu görünür; tıklanınca `resend-confirmation` çağrılır ve "Doğrulama e-postası gönderildi" mesajı çıkar.
  - `Login_Locked_ShowsMinutes`: 423 → başlıktaki "15 dakika" metni görünür.
  - `Login_EmptyFields_ShowsClientValidationWithoutRequest`: istek atılmaz.
  - `Login_RememberMeIsSent`: kutu işaretliyse istek gövdesinde `rememberMe: true`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Bileşenleri yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 4: Kayıt ve e-posta doğrulama sayfaları

**Files:**
- Create: `src/app/(auth)/kayit/page.tsx` + `RegisterForm.tsx`, `src/app/(auth)/eposta-dogrula/page.tsx` + `ConfirmEmail.tsx`
- Test: `RegisterForm.test.tsx`, `ConfirmEmail.test.tsx`

**Interfaces:**
- Consumes: `identity.register`, `identity.confirmEmail`, `identity.resendConfirmation`, `AuthCard`, `PasswordField`, `FormAlert`.
- **Kayıt başarılıysa (202):** Form yerine "E-postanızı kontrol edin" görünümü gelir; girilen adres gösterilir ve "Yeniden gönder" butonu bulunur.
- **`ConfirmEmail`:** Açılışta `u` ve `t` sorgu parametreleriyle bir kez `confirmEmail` çağırır (React StrictMode'un çift çağrısına karşı `useRef` bayrağı).
  - Başarılıysa: aktif firma yoksa `/baslangic`, varsa `/` sayfasına yönlendirir.
  - Hata varsa: "Bağlantı geçersiz veya süresi dolmuş" mesajı ve e-posta adresiyle yeniden gönderme formu gösterilir.
  - `u` veya `t` eksikse: istek atılmaz, aynı hata görünümü gösterilir.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `Register_Success_ShowsCheckEmailWithAddress`.
  - `Register_ClientValidation`: 9 karakterlik şifre → "Şifre en az 10 karakter olmalıdır." mesajı, istek atılmaz.
  - `Register_ShowsServerFieldError`: 400 `errors.Password` → mesaj şifre alanının altında görünür.
  - `Register_DisablesSubmitWhileSending`: çift tıklamada MSW yalnızca 1 istek görür.
  - `ConfirmEmail_Success_RedirectsToBaslangic`.
  - `ConfirmEmail_CallsApiOnlyOnce`: StrictMode ile render edildiğinde yalnızca 1 istek gider.
  - `ConfirmEmail_InvalidToken_ShowsResendForm`.
  - `ConfirmEmail_MissingParams_ShowsErrorWithoutRequest`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Bileşenleri yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 5: Şifremi unuttum ve şifre sıfırlama

**Files:**
- Create: `src/app/(auth)/sifremi-unuttum/page.tsx` + `ForgotPasswordForm.tsx`, `src/app/(auth)/sifre-sifirla/page.tsx` + `ResetPasswordForm.tsx`
- Test: `ForgotPasswordForm.test.tsx`, `ResetPasswordForm.test.tsx`

**Interfaces:**
- **`ForgotPasswordForm`:** Gönderimden sonra her durumda aynı metin görünür: "Bu adrese kayıtlı bir hesap varsa sıfırlama bağlantısı gönderdik." 429 dönerse sunucunun başlığı gösterilir.
- **`ResetPasswordForm`:** `e` ve `t` parametrelerini okur. Yeni şifre ve tekrarı istenir; ikisi eşleşmezse istemci tarafında hata verilir.
  - Başarılıysa (204): "Şifreniz değiştirildi. Tüm cihazlardaki oturumlarınız kapatıldı." mesajı ve "Giriş yap" butonu gösterilir.
  - `invalid_token` dönerse: hata mesajı ve "Yeni bağlantı iste" linki (`/sifremi-unuttum`) gösterilir.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `Forgot_AlwaysShowsSameConfirmation`: 202 yanıtında, istek gövdesi ne olursa olsun aynı metin görünür.
  - `Forgot_RateLimited_ShowsServerTitle`.
  - `Reset_MismatchedPasswords_NoRequest`.
  - `Reset_Success_ShowsLoginButton`: istek gövdesindeki `email` ve `token` URL'den gelir.
  - `Reset_InvalidToken_OffersNewLink`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Bileşenleri yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 6: Oturum bağlamı, panel koruması ve üst bar

**Files:**
- Create: `src/session/SessionProvider.tsx`
- Modify: `src/components/layout/PanelShell.tsx` (`SessionProvider` ile sarılır; oturum yüklenene kadar ortada bir `CircularProgress` gösterilir)
- Modify: `src/components/layout/Header.tsx`. Profil menüsünde: ad ve e-posta oturumdan gelir. "Profilim" `/hesap` sayfasına gider. "Çıkış Yap" `identity.logout()` çağırıp `/giris` sayfasına yönlendirir. Kullanıcının birden fazla firması varsa menüde "Firma değiştir" başlığı altında firmalar listelenir; aktif firmanın yanında işaret bulunur.
- Modify: `src/components/layout/SidebarContent.tsx` (alttaki kullanıcı kartı: ad ve aktif firma adı oturumdan gelir; güç ikonu çıkış yapar)
- Test: `src/session/SessionProvider.test.tsx`, `src/components/layout/Header.test.tsx`

**Interfaces:**
- Produces:
  ```ts
  export type SessionState = { session: SessionInfo; refresh(): Promise<void>; setSession(s: SessionInfo): void;
      hasPermission(p: string): boolean; switchTenant(id: string): Promise<void>; logout(): Promise<void> };
  export function SessionProvider({ children, requireCompany = true }: { children: ReactNode; requireCompany?: boolean }): JSX.Element;
  export function useSession(): SessionState;   // sağlayıcı dışında kullanılırsa hata fırlatır
  ```
  - **Açılışta:** `getSession()` çağrılır.
    - 401 dönerse: `router.replace(loginUrl(pathname + search))`.
    - `requireCompany` açıkken aktif firma `null` ise: `router.replace("/baslangic")`.
  - **`hasPermission`:** `"*"` her izni karşılar.
  - **`switchTenant`:** Başarılıysa `router.refresh()` çağrılır ve sayfa yeni firmanın verisiyle yeniden çizilir.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `SessionProvider_On401_RedirectsToLoginWithNext`: konum `/hesap?x=1` iken → `replace("/giris?next=%2Fhesap%3Fx%3D1")`.
  - `SessionProvider_NoCompany_RedirectsToBaslangic`.
  - `SessionProvider_NoCompany_AllowedWhenNotRequired`: `requireCompany={false}` iken yönlendirme yok.
  - `HasPermission_StarGrantsAll`.
  - `Header_ShowsUserNameAndEmail`.
  - `Header_Logout_CallsApiAndRedirects`.
  - `Header_CompanySwitcher_OnlyWithMultipleCompanies`.
  - `Header_SwitchCompany_CallsApiAndRefreshes`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Kodu yaz.**
- [ ] **Step 4: Testleri ve build'i çalıştır.** Run: `npm --prefix frontend test && npm --prefix frontend run build`. Expected: PASS ve build başarılı. Mevcut dashboard ve "Yakında" sayfaları oturumla açılmaya devam etmeli.
- [ ] **Kontrol noktası.**

---

### Task 7: Başlangıç ekranı (firma oluştur / bekleyen davetler)

**Files:**
- Create: `src/app/baslangic/layout.tsx` (auth düzenini paylaşır; `(auth)/layout.tsx`'teki kart düzeni `src/components/auth/AuthShell.tsx`'e çıkarılır ve iki yerde kullanılır), `src/app/baslangic/page.tsx` + `Onboarding.tsx`
- Test: `Onboarding.test.tsx`

**Interfaces:**
- Consumes: `SessionProvider` (`requireCompany={false}`), `identity.getMyInvitations`, `identity.acceptMyInvitation`, `identity.createCompany`.
- **Ekran:** "Hoş geldin, {ad}" başlığının altında iki bölüm var:
  1. **Bekleyen davetler:** Her davet için firma adı, davet eden, rol ve "Katıl" butonu. Hiç davet yoksa bu bölüm gizlenir.
  2. **Firma oluştur:** Firma adı alanı ve "Oluştur" butonu.

  Her iki işlem başarılı olunca `/` sayfasına gidilir.
- **Kullanıcının zaten firması varsa:** Ekranın üstünde "Panele dön" linki görünür (ikinci firma oluşturma veya davet kabul etme durumları için).
- **Çıkış:** Ekranda bir "Çıkış yap" linki bulunur.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `Onboarding_ListsInvitations_AndAcceptRedirects`.
  - `Onboarding_NoInvitations_HidesSection`.
  - `Onboarding_CreateCompany_ValidatesAndRedirects`: 1 karakterlik ad → hata, istek atılmaz; geçerli ad → `createCompany` çağrılır ve `/` sayfasına gidilir.
  - `Onboarding_WithExistingCompany_ShowsBackLink`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Kodu yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 8: Davet sayfası `/davet/[token]`

**Files:**
- Create: `src/app/(auth)/davet/[token]/page.tsx` + `InvitationLanding.tsx`
- Test: `InvitationLanding.test.tsx`

**Interfaces:**
- Consumes: `identity.getInvitationPreview`, `identity.getSession`, `identity.acceptInvitation`, `identity.register` (`inviteToken` ile).
- **Akış:**
  1. Önizleme yüklenir; 404 dönerse "Bu davet geçersiz, kullanılmış veya süresi dolmuş." mesajı gösterilir.
  2. Oturum kontrol edilir (`getSession`; 401 normal kabul edilir).
  3. Duruma göre:

| Durum | Gösterilen |
|---|---|
| Oturum var, e-posta davetle aynı | "{firma} firmasına katıl" butonu → `acceptInvitation` → `/` |
| Oturum var, e-posta farklı | "Bu davet {e-posta} adresine gönderildi" mesajı + "Çıkış yap ve o hesapla gir" butonu (çıkış sonrası `/giris?next=/davet/{token}`) |
| Oturum yok, `accountExists = true` | "Giriş yap" butonu → `/giris?next=/davet/{token}` |
| Oturum yok, `accountExists = false` | E-postası sabitlenmiş kayıt formu (ad soyad + şifre) → `register({..., inviteToken})` → 200 dönerse `/` |

  Kayıt yanıtı 409 `account_exists` olursa giriş butonu gösterilir.

- [ ] **Step 1: Başarısız testleri yaz.** Tablodaki her satır için bir test, ayrıca:
  - `Invitation_NotFound_ShowsMessage`.
  - `Invitation_Register_SendsInviteTokenAndLocksEmail`: e-posta alanı salt okunur; istek gövdesinde `inviteToken` var.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Kodu yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 9: Hesabım sayfası

**Files:**
- Create: `src/app/(panel)/hesap/page.tsx` + `AccountPage.tsx` ve parçaları: `ProfileCard.tsx`, `ChangePasswordCard.tsx`, `SessionsCard.tsx`
- Test: `ProfileCard.test.tsx`, `ChangePasswordCard.test.tsx`, `SessionsCard.test.tsx`

**Interfaces:**
- Consumes: `useSession`, `identity.updateProfile`, `identity.changePassword`, `identity.getSessions`, `identity.revokeSession`, `identity.logoutAll`.
- **Profil kartı:** Ad soyad düzenlenebilir. E-posta salt okunurdur ("E-posta değiştirme yakında"). Kaydettikten sonra `refresh()` çağrılır.
- **Şifre kartı:** Mevcut şifre, yeni şifre ve tekrarı istenir. Başarılıysa "Şifreniz değişti. Diğer cihazlardaki oturumlarınız kapatıldı." mesajı gösterilir. Yanlış mevcut şifre için `errors.CurrentPassword` mesajı ilgili alanın altına yazılır.
- **Oturumlar kartı:** Tablo sütunları: cihaz (user-agent'tan kısa tarayıcı/işletim sistemi adı), IP, açılış zamanı (tr-TR biçiminde), "Bu cihaz" etiketi ve "Kapat" butonu. "Bu cihaz" satırında "Kapat" butonu yoktur.
  - Kartın altında "Tüm cihazlardan çıkış yap" butonu bulunur. Onay penceresinden sonra `logoutAll` çağrılır ve kullanıcı `/giris` sayfasına gider.
  - Kısa cihaz adı için ayrı bir saf fonksiyon yazılır: `describeUserAgent(ua: string | null): string`, `src/lib/userAgent.ts` içinde. Örnek eşleşmeler: `"Chrome / Windows"`, `"Safari / iPhone"`, bilinmeyenler için `"Bilinmeyen cihaz"`.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `describeUserAgent`: Chrome/Windows, Firefox/Linux, Safari/iPhone, Edge/Windows ve null için.
  - `Profile_SavesAndRefreshesSession`.
  - `ChangePassword_WrongCurrent_ShowsFieldError`.
  - `ChangePassword_Success_ClearsFormAndShowsMessage`.
  - `Sessions_CurrentRowHasNoRevokeButton`.
  - `Sessions_Revoke_RemovesRow`.
  - `Sessions_LogoutAll_ConfirmsThenRedirects`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Kodu yaz.**
- [ ] **Step 4: Testleri çalıştır.** Expected: PASS.
- [ ] **Kontrol noktası.**

---

### Task 10: Firma üyeleri sayfası

**Files:**
- Create: `src/app/(panel)/firma/uyeler/page.tsx` + `MembersPage.tsx`, `MembersTable.tsx`, `InvitationsCard.tsx`, `CompanyCard.tsx`
- Modify: `src/config/navigation.ts`. "Sistem" bölümüne "Firma Üyeleri" (`/firma/uyeler`, `IconUsersGroup`) eklenir. Navigasyon öğelerine isteğe bağlı bir `permission` alanı eklenir; izni olmayan kullanıcı o öğeyi menüde görmez (`SidebarContent` `useSession().hasPermission` ile süzer). Bu öğenin izni `members.read`'dir.
- Modify: `src/app/(panel)/[...slug]/page.tsx`. Statik parametre listesi yalnızca `ComingSoon` gösteren öğeleri üretir; `/firma/uyeler` ve `/hesap` gerçek sayfa olduğundan listeden çıkarılır.
- Test: `MembersTable.test.tsx`, `InvitationsCard.test.tsx`, `CompanyCard.test.tsx`

**Interfaces:**
- Consumes: `useSession`, `identity.getMembers`, `getRoles`, `changeMemberRole`, `removeMember`, `listInvitations`, `createInvitation`, `resendInvitation`, `revokeInvitation`, `updateCompany`, `leaveCompany`.
- **Üyeler tablosu:** Sütunlar: ad, e-posta, rol, katılma tarihi.
  - `members.manage` izni varsa rol açılır listeden değiştirilebilir ve satırda "Çıkar" butonu bulunur.
  - "Sahip" seçeneği yalnızca kullanıcının kendisi sahipse listede görünür.
  - Kullanıcının kendi satırında "Çıkar" butonu yoktur; ayrılmak için "Firmadan ayrıl" kullanılır.
  - 409 `last_owner` dönerse sunucunun mesajı gösterilir.
- **Davetler kartı:** Yalnızca `members.invite` izni varsa görünür.
  - Form: e-posta ve rol seçimi (varsayılan rol "Kullanıcı").
  - Bekleyen davetler listesi: e-posta, rol, son geçerlilik tarihi, "Yeniden gönder" ve "İptal".
  - 409 `already_member` dönerse mesaj e-posta alanının altında gösterilir.
- **Firma kartı:**
  - `company.update` izni varsa firma adı düzenlenebilir.
  - Herkes "Firmadan ayrıl" butonunu görür. Onaydan sonra ayrılınır, oturum yenilenir; kullanıcı başka bir firmanın üyesiyse `/` sayfasına, değilse `/baslangic` sayfasına gider. `last_owner` hatası gösterilir.

- [ ] **Step 1: Başarısız testleri yaz.**
  - `Members_HidesManageControlsWithoutPermission`: izinler `["members.read"]` → rol listesi ve "Çıkar" butonu yok, davet kartı yok.
  - `Members_OwnerOptionOnlyForOwners`: yönetici (`members.manage`) seçeneklerde "Sahip"i görmez.
  - `Members_ChangeRole_CallsApi`.
  - `Members_LastOwner_ShowsServerMessage`.
  - `Invitations_Create_AddsToListAndClearsForm`.
  - `Invitations_AlreadyMember_ShowsFieldError`.
  - `Invitations_Revoke_RemovesRow`.
  - `Company_Leave_ConfirmsThenRedirectsToBaslangic`.
  - `Sidebar_HidesMembersItemWithoutPermission`.
- [ ] **Step 2: Testleri çalıştır.** Expected: FAIL.
- [ ] **Step 3: Kodu yaz.**
- [ ] **Step 4: Testleri, lint'i ve build'i çalıştır.** Run: `npm --prefix frontend test && npm --prefix frontend run lint && npm --prefix frontend run build`. Expected: hepsi başarılı.
- [ ] **Kontrol noktası.**

---

### Task 11: Uçtan uca doğrulama (tarayıcı) ve belgeler

**Files:**
- Modify: `README.md`. "Frontend" bölümü eklenir: hangi sayfaların olduğu, sıralı başlatma (önce `infra/up.sh`, sonra identity, erp, frontend) ve demo hesabı.

- [ ] **Step 1: Her şeyi başlat.** `infra/up.sh` çalıştır; identity, erp ve frontend'i `.claude/launch.json` ile başlat.
- [ ] **Step 2: Uygulamanın tarayıcı panelinde (masaüstü ve mobil genişlik, açık ve koyu tema) şu senaryoları tek tek yürüt:**
  1. `/` → oturum yok → `/giris?next=%2F`.
  2. Demo hesabıyla giriş → dashboard açılır, üst barda "Demo Kullanıcı" ve "Demo Firma" görünür.
  3. Çıkış → `/giris`.
  4. `/kayit` → yeni hesap → Mailpit (http://localhost:8025) arayüzündeki bağlantı → `/eposta-dogrula` → `/baslangic` → firma oluştur → dashboard.
  5. `/firma/uyeler` → ikinci kişiyi davet et → Mailpit'teki davet bağlantısını gizli pencerede aç (tarayıcı panelinde ayrı bir sekmede, önce çıkış yaparak) → davetli kayıt → firmaya katılır, "Kullanıcı" rolüyle davet kartını göremez.
  6. Sahip, davetlinin rolünü "Yönetici" yapar → davetli sayfayı yenileyince davet kartını görür (oturumu canlı güncellendi).
  7. `/hesap` → oturumlar listesinde iki cihaz → diğerini kapat → o sekme bir sonraki istekte `/giris` sayfasına düşer.
  8. Şifremi unuttum → Mailpit bağlantısı → şifre sıfırla → eski oturumlar kapanır → yeni şifreyle giriş.
  9. Konsolda hata olmamalı (`read_console_messages`).
- [ ] **Step 3: Tüm testleri çalıştır.** Run: `npm --prefix frontend test && npm --prefix frontend run lint && npm --prefix frontend run build`. Backend paketleri de yeşil kalmalı: `dotnet test` ×3 (backend README'deki komutlar).
- [ ] **Kontrol noktası.** Kimlik doğrulama modülü (spec'in 5. aşaması) tamamlandı.
