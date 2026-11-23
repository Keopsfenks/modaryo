import { NextResponse, type NextRequest } from "next/server";
import { isPublicPath, loginUrl } from "@/lib/navigation";

/**
 * Oturum cookie'si olmayan kullanıcıyı korumalı sayfalardan /giris'e yönlendirir.
 * Cookie'nin gerçekten geçerli olup olmadığına paneldeki SessionProvider karar verir (GET /auth/session).
 */
export function proxy(request: NextRequest) {
    const { pathname, search } = request.nextUrl;

    if (isPublicPath(pathname) || request.cookies.has("modaryo_sid")) {
        return NextResponse.next();
    }

    return NextResponse.redirect(new URL(loginUrl(pathname + search), request.url));
}

export const config = {
    // API (Next.js rewrites ile servislere gider), Next.js iç dosyaları ve statik dosyalar hariç her şey.
    matcher: ["/((?!api|_next/static|_next/image|favicon.ico|.*\\.(?:png|jpg|jpeg|svg|ico|webp|xml|txt)$).*)"],
};
