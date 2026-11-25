import type { InstrumentDirection, InstrumentKind, InstrumentStatus } from "./api/treasury";

/**
 * Çek/senet durum makinesi; sunucudaki ERP.Domain.Treasury.InstrumentStateMachine tablosunun aynısıdır
 * (instruments.test.ts C# dosyasıyla karşılaştırır). Her kayıt: [yön, nereden, nereye].
 */
export const INSTRUMENT_TRANSITIONS: readonly (readonly [InstrumentDirection, InstrumentStatus, InstrumentStatus])[] = [
    ["received", "inPortfolio", "endorsed"],
    ["received", "inPortfolio", "inCollection"],
    ["received", "inPortfolio", "returnedToCustomer"],
    ["received", "inCollection", "collected"],
    ["received", "inCollection", "bounced"],
    ["received", "endorsed", "bounced"],
    ["received", "bounced", "collected"],
    ["issued", "issued", "paid"],
    ["issued", "issued", "returnedFromSupplier"],
];

export function allowedTransitions(direction: InstrumentDirection, from: InstrumentStatus): InstrumentStatus[] {
    return INSTRUMENT_TRANSITIONS.filter(([d, f]) => d === direction && f === from).map(([, , to]) => to);
}

export const STATUS_LABELS: Record<InstrumentStatus, string> = {
    inPortfolio: "Portföyde",
    endorsed: "Ciro edildi",
    inCollection: "Tahsilde",
    collected: "Tahsil edildi",
    bounced: "Karşılıksız / iade",
    returnedToCustomer: "Müşteriye iade",
    issued: "Verildi",
    paid: "Ödendi",
    returnedFromSupplier: "Tedarikçiden iade",
};

/** Geçiş düğmesi/seçeneği metni (hedef durumu eylem olarak anlatır). */
export const MOVE_LABELS: Record<InstrumentStatus, string> = {
    inPortfolio: "Portföye al",
    endorsed: "Tedarikçiye ciro et",
    inCollection: "Bankaya tahsile ver",
    collected: "Tahsil edildi",
    bounced: "Karşılıksız / iade geldi",
    returnedToCustomer: "Müşteriye iade et",
    issued: "Verildi",
    paid: "Ödendi",
    returnedFromSupplier: "Tedarikçiden iade alındı",
};

export const KIND_LABELS: Record<InstrumentKind, string> = { check: "Çek", note: "Senet" };
export const DIRECTION_LABELS: Record<InstrumentDirection, string> = { received: "Alınan (müşteri)", issued: "Verilen (firma)" };
