"use client";

import Box from "@mui/material/Box";
import GlobalStyles from "@mui/material/GlobalStyles";
import type { SlipDto } from "@/lib/api/pos";
import { code128Widths } from "./code128";
import { fmtQty, money } from "./format";

const METHOD_LABELS: Record<string, string> = { cash: "Nakit", card: "Kart", onAccount: "Veresiye", returnCredit: "İade mahsubu" };
const FISCAL_NOTE = "Bilgi fişidir. Mali değeri yoktur.";

function Barcode({ text }: { text: string }) {
    const widths = code128Widths(text);
    let x = 10; // sessiz bölge
    const bars: { x: number; w: number }[] = [];
    widths.forEach((w, i) => {
        if (i % 2 === 0) bars.push({ x, w });
        x += w;
    });
    const total = x + 10;
    return (
        <svg role="img" aria-label="Fiş barkodu" viewBox={`0 0 ${total} 40`} preserveAspectRatio="none" style={{ width: "100%", height: 40, display: "block" }}>
            {bars.map((b, i) => (
                <rect key={i} x={b.x} y={0} width={b.w} height={40} fill="#000" />
            ))}
        </svg>
    );
}

const row = { display: "flex", justifyContent: "space-between", gap: 8 } as const;

/**
 * 80 mm termal fiş. Ekranda gizlidir; window.print() ile yalnızca bu bölüm basılır (@page 80 mm).
 * Yazar kasa modunda "Bilgi fişidir. Mali değeri yoktur." notu zorunludur; e-Arşiv modunda fatura numarası ve iletim notu gelir.
 */
export default function Slip({ slip }: { slip: SlipDto }) {
    const notes = [...slip.notes];
    if (slip.mode === "fiscalReceipt" && !notes.some((n) => n.includes("Bilgi fişidir"))) notes.push(FISCAL_NOTE);

    return (
        <>
            <GlobalStyles
                styles={{
                    "@page": { size: "80mm auto", margin: 0 },
                    "@media screen": { ".pos-slip": { display: "none" } },
                    "@media print": {
                        "body *": { visibility: "hidden" },
                        ".pos-slip, .pos-slip *": { visibility: "visible" },
                        ".pos-slip": { position: "absolute", left: 0, top: 0, width: "80mm" },
                    },
                }}
            />
            <Box className="pos-slip" sx={{ width: "80mm", p: "4mm", fontFamily: "monospace", fontSize: 12, color: "#000", bgcolor: "#fff" }}>
                <div style={{ textAlign: "center", fontWeight: 700, fontSize: 14 }}>{slip.firm}</div>
                <div style={{ textAlign: "center" }}>{slip.branch}</div>
                <div style={{ textAlign: "center" }}>
                    {slip.register} · {slip.cashier}
                </div>
                <div style={{ textAlign: "center" }}>{new Date(slip.at).toLocaleString("tr-TR")}</div>
                <div style={{ textAlign: "center", marginBottom: 6 }}>
                    Fiş No: <span data-testid="slip-number">{slip.number}</span>
                </div>
                <hr />
                {slip.lines.map((l, i) => (
                    <div key={i} style={{ marginBottom: 4 }}>
                        <div>{l.description}</div>
                        <div style={row}>
                            <span>
                                {fmtQty(l.quantity)} {l.unit} x {money(l.unitPrice)} (%{l.vatRate})
                            </span>
                            <span>{money(l.total)}</span>
                        </div>
                        {l.discount > 0 && <div style={row}><span>İskonto</span><span>-{money(l.discount)}</span></div>}
                        {l.campaignDiscount > 0 && <div style={row}><span>Kampanya</span><span>-{money(l.campaignDiscount)}</span></div>}
                    </div>
                ))}
                <hr />
                {slip.discount > 0 && <div style={row}><span>Toplam indirim</span><span>-{money(slip.discount)}</span></div>}
                <div style={{ ...row, fontWeight: 700, fontSize: 14 }}>
                    <span>TOPLAM</span>
                    <span>{money(slip.total)}</span>
                </div>
                {slip.vatSummary.map((v) => (
                    <div key={v.rate} style={row}>
                        <span>KDV %{v.rate} (matrah {money(v.base)})</span>
                        <span>{money(v.vat)}</span>
                    </div>
                ))}
                <hr />
                {slip.payments.map((p, i) => (
                    <div key={i} style={row}>
                        <span>{METHOD_LABELS[p.method] ?? p.method}</span>
                        <span>{money(p.amount)}</span>
                    </div>
                ))}
                {slip.change > 0 && (
                    <div style={{ ...row, fontWeight: 700 }}>
                        <span>Para üstü</span>
                        <span>{money(slip.change)}</span>
                    </div>
                )}
                {slip.invoiceNumber && (
                    <div style={{ marginTop: 6 }}>
                        e-Arşiv Fatura No: <span>{slip.invoiceNumber}</span>
                    </div>
                )}
                <hr />
                {notes.map((n, i) => (
                    <div key={i} style={{ textAlign: "center" }}>
                        {n}
                    </div>
                ))}
                <div style={{ marginTop: 6 }}>
                    <Barcode text={slip.barcode} />
                    <div style={{ textAlign: "center" }}>{slip.barcode}</div>
                </div>
            </Box>
        </>
    );
}
