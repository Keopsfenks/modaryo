/** Oturum listesindeki kısa cihaz adı: "Tarayıcı / İşletim sistemi". Tanınmazsa "Bilinmeyen cihaz". */
export function describeUserAgent(userAgent: string | null | undefined): string {
    if (!userAgent || !userAgent.startsWith("Mozilla/")) {
        return "Bilinmeyen cihaz";
    }

    const browser = /Edg\//.test(userAgent)
        ? "Edge"
        : /OPR\//.test(userAgent)
          ? "Opera"
          : /Firefox\//.test(userAgent)
            ? "Firefox"
            : /Chrome\//.test(userAgent)
              ? "Chrome"
              : /Safari\//.test(userAgent)
                ? "Safari"
                : null;

    const os = /iPhone/.test(userAgent)
        ? "iPhone"
        : /iPad/.test(userAgent)
          ? "iPad"
          : /Android/.test(userAgent)
            ? "Android"
            : /Windows/.test(userAgent)
              ? "Windows"
              : /Mac OS X/.test(userAgent)
                ? "macOS"
                : /Linux/.test(userAgent)
                  ? "Linux"
                  : null;

    if (!browser && !os) {
        return "Bilinmeyen cihaz";
    }

    return [browser ?? "Tarayıcı", os ?? "Bilinmeyen sistem"].join(" / ");
}
