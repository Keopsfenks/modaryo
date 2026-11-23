import { apiFetch } from "./client";

// Herkese açık tanıtım sitesi uçları (oturum gerektirmez).

export type ContactInput = {
    name: string;
    company?: string;
    email: string;
    phone?: string;
    topic: "demo" | "iletisim";
    interest?: string;
    message?: string;
    kvkkConsent: boolean;
    /** Tuzak alan: insanlar boş bırakır; dolu gelirse sunucu isteği sessizce yok sayar. */
    website: string;
};

export const site = {
    contact: (body: ContactInput) => apiFetch<void>("/api/identity/public/contact", { method: "POST", body }),
};
