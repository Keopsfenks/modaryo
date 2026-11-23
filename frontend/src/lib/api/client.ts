export const CSRF_HEADER = "X-Modaryo-Csrf";

/** Herhangi bir API çağrısı 401 döndüğünde window'a gönderilen olay. */
export const UNAUTHORIZED_EVENT = "modaryo:unauthorized";

export type ProblemBody = {
    status?: number;
    title?: string;
    code?: string;
    errors?: Record<string, string[]>;
};

const GENERIC_ERROR = "Beklenmeyen bir hata oluştu.";
const VALIDATION_ERROR = "Formdaki hataları düzeltin.";
const NETWORK_ERROR = "Sunucuya ulaşılamadı. Bağlantınızı kontrol edin.";

/**
 * Backend ProblemDetails yanıtının tipli hali. Doğrulama hataları alan adına (camelCase) göre
 * {@link fieldErrors} içinde, her alanın ilk mesajıyla durur.
 */
export class ApiError extends Error {
    readonly status: number;
    readonly code?: string;
    readonly fieldErrors: Record<string, string>;

    constructor(status: number, message: string, code?: string, fieldErrors: Record<string, string> = {}) {
        super(message);
        this.name = "ApiError";
        this.status = status;
        this.code = code;
        this.fieldErrors = fieldErrors;
    }

    static fromProblem(status: number, problem: ProblemBody | null): ApiError {
        const fieldErrors = Object.fromEntries(
            Object.entries(problem?.errors ?? {})
                .filter(([, messages]) => messages.length > 0)
                .map(([field, messages]) => [field.charAt(0).toLowerCase() + field.slice(1), messages[0]]),
        );
        const hasFieldErrors = Object.keys(fieldErrors).length > 0;
        const message = hasFieldErrors ? VALIDATION_ERROR : problem?.title || GENERIC_ERROR;
        return new ApiError(status, message, problem?.code, fieldErrors);
    }
}

type FetchInit = { method?: string; body?: unknown; signal?: AbortSignal };

/**
 * Tüm backend çağrıları buradan geçer: aynı köken (Next.js proxy), cookie ile, GET dışında CSRF başlığıyla.
 * Gövdesiz başarılı yanıtlar (202/204) undefined döner.
 */
export async function apiFetch<T>(path: string, init: FetchInit = {}): Promise<T> {
    const method = (init.method ?? "GET").toUpperCase();
    const headers: Record<string, string> = { Accept: "application/json" };

    if (method !== "GET" && method !== "HEAD") {
        headers[CSRF_HEADER] = "1";
    }

    const isForm = typeof FormData !== "undefined" && init.body instanceof FormData;
    if (init.body !== undefined && !isForm) {
        headers["Content-Type"] = "application/json";
    }

    const url = typeof window === "undefined" ? path : new URL(path, window.location.origin).toString();

    let response: Response;
    try {
        response = await fetch(url, {
            method,
            headers,
            body: init.body === undefined ? undefined : isForm ? (init.body as FormData) : JSON.stringify(init.body),
            credentials: "same-origin",
            signal: init.signal,
        });
    } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") {
            throw error;
        }
        throw new ApiError(0, NETWORK_ERROR);
    }

    const text = await response.text();

    if (response.status === 401 && typeof window !== "undefined") {
        // Oturum düşmüş: paneldeki SessionProvider dinler ve /giris'e yönlendirir.
        window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
    }

    if (!response.ok) {
        throw ApiError.fromProblem(response.status, parseJson<ProblemBody>(text));
    }

    return (text ? parseJson<T>(text) : undefined) as T;
}

function parseJson<T>(text: string): T | null {
    try {
        return JSON.parse(text) as T;
    } catch {
        return null;
    }
}
