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

export function accountDto(overrides: Record<string, unknown> = {}) {
    return {
        id: "acc-1",
        code: "C-0001",
        name: "Akın Tekstil",
        kind: "corporate",
        taxNumber: null,
        taxOffice: null,
        isEInvoiceTaxpayer: false,
        isCustomer: true,
        isSupplier: false,
        phone: null,
        email: null,
        notes: null,
        currency: "TRY",
        defaultDueDays: 30,
        creditLimit: 50000,
        isActive: true,
        balance: 1250.5,
        overdueAmount: 0,
        hasMovements: false,
        addresses: [],
        ...overrides,
    };
}

export function addressDto(overrides: Record<string, unknown> = {}) {
    return {
        id: "addr-1",
        title: "Merkez",
        line: "Atatürk Cad. 1",
        district: "Kadıköy",
        city: "İstanbul",
        postalCode: null,
        country: "Türkiye",
        isBilling: true,
        isShipping: false,
        isDefaultBilling: true,
        isDefaultShipping: false,
        ...overrides,
    };
}
