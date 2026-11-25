import type { InstrumentDto } from "@/lib/api/treasury";

/** Çek/senet bileşen testleri için örnek kayıt. */
export const instrumentDto = (overrides: Partial<InstrumentDto> = {}): InstrumentDto => ({
    id: "i-1",
    kind: "check",
    direction: "received",
    status: "inPortfolio",
    number: "CK-100",
    dueDate: "2026-12-15",
    amount: 5000,
    currency: "TRY",
    drawerName: "Mert Ltd.",
    bankName: "Garanti",
    bankBranch: null,
    accountNo: null,
    endorsementCount: 0,
    currentAccountId: "c-1",
    currentAccountName: "Akın Tekstil",
    endorsedToId: null,
    endorsedToName: null,
    collectionAccountId: null,
    protestFlag: false,
    ...overrides,
});
