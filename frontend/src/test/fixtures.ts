import type { SessionInfo } from "@/lib/api/identity";

export const demoCompany = { id: "0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b21", name: "Demo Firma", roleName: "Sahip" };

export function sessionInfo(overrides: Partial<SessionInfo> = {}): SessionInfo {
    return {
        user: { id: "0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90", email: "ali@ornek.com", fullName: "Ali Veli" },
        activeCompany: demoCompany,
        permissions: ["*"],
        companies: [demoCompany],
        ...overrides,
    };
}

export function problem(status: number, title: string, code?: string, errors?: Record<string, string[]>) {
    return { status, title, ...(code ? { code } : {}), ...(errors ? { errors } : {}) };
}
