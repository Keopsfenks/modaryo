import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SaleInput, SalePreview, SalePreviewInput } from "@/lib/api/pos";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { lookup, REGISTER, saleResult, SHIFT, item } from "./posFixtures";
import SellScreen from "./SellScreen";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));
vi.setConfig({ testTimeout: 20000 });

/** Sunucu önizlemesi taklidi: satır başına miktar x birim fiyat; totalFor ile sunucunun farklı tutarı simüle edilir. */
function previewFor(body: SalePreviewInput, prices: Record<string, number>, totalFor?: (estimate: number) => number): SalePreview {
    const lines = body.lines.map((l, i) => {
        const estimate = l.fixedTotal ?? Math.round((l.quantity ?? 0) * (prices[l.productId] ?? 25) * 100) / 100;
        const total = totalFor ? totalFor(estimate) : estimate;
        return { sort: i + 1, productId: l.productId, variantId: null, description: "Kalem", unit: "adet", quantity: l.quantity, unitPrice: 0, vatRate: 20, discount: 0, campaignDiscount: 0, discountGross: 0, campaignDiscountGross: 0, campaignId: null, net: 0, vat: 0, total, listTotal: total };
    });
    const total = Math.round(lines.reduce((s, l) => s + l.total, 0) * 100) / 100;
    return { lines, subtotal: total, discount: 0, vatTotal: 0, total };
}

function setup(lookups: Record<string, ReturnType<typeof lookup>> = { "8690000000001": lookup() }, totalFor?: (estimate: number) => number) {
    const sales: SaleInput[] = [];
    const previews: SalePreviewInput[] = [];
    server.use(
        http.post("*/api/erp/pos/sales/preview", async ({ request }) => {
            const body = (await request.json()) as SalePreviewInput;
            previews.push(body);
            return HttpResponse.json(previewFor(body, Object.fromEntries(Object.values(lookups).map((l) => [l.item.productId, l.unitPrice])), totalFor));
        }),
        http.get("http://localhost:3000/api/identity/auth/session", () => HttpResponse.json(sessionInfo({ permissions: ["*"], modules: ["pos"] }))),
        http.get("*/api/erp/pos/lookup", ({ request }) => {
            const code = new URL(request.url).searchParams.get("code") ?? "";
            const hit = lookups[code];
            return hit ? HttpResponse.json(hit) : HttpResponse.json(problem(404, "Kalem bulunamadı.", "not_found"), { status: 404 });
        }),
    );
    localStorage.setItem("pos.autoPrint", "0");
    const view = renderWithTheme(
        <SessionProvider>
            <SellScreen register={REGISTER} shift={SHIFT} onReturn={vi.fn()} onCloseShift={vi.fn()} onChangeRegister={vi.fn()} />
        </SessionProvider>,
    );
    return { ...view, sales, previews };
}

async function scan(user: ReturnType<typeof setup>["user"], code: string) {
    const input = await screen.findByRole("textbox", { name: "Barkod" });
    await user.type(input, `${code}{Enter}`);
}

afterEach(() => {
    Object.defineProperty(window.navigator, "onLine", { value: true, configurable: true });
    localStorage.clear();
});

describe("SellScreen", () => {
    it("SellScreen_ScanAddsLineAndIncrements", async () => {
        const { user } = setup();

        await scan(user, "8690000000001");
        expect(await screen.findByRole("textbox", { name: "Miktar Kalem" })).toHaveValue("1");
        await scan(user, "8690000000001");

        await waitFor(() => expect(screen.getByRole("textbox", { name: "Miktar Kalem" })).toHaveValue("2"));
        expect(screen.getAllByRole("textbox", { name: "Miktar Kalem" })).toHaveLength(1);
        expect(screen.getByLabelText("Sepet toplamı")).toHaveTextContent("50,00 ₺");
    });

    it("SellScreen_ScaleBarcodeAddsWeight", async () => {
        const scale = lookup({ item: item({ soldByMeasure: true, name: "Domates", code: "12345" }), unit: "kg", quantity: 1.234, unitPrice: 100 });
        const { user } = setup({ "2712345012348": scale });

        await scan(user, "2712345012348");

        expect(await screen.findByRole("textbox", { name: "Miktar Domates" })).toHaveValue("1,234");
        expect(screen.getByLabelText("Sepet toplamı")).toHaveTextContent("123,40 ₺");
    });

    it("SellScreen_UnknownCodeShowsError", async () => {
        const { user } = setup();

        await scan(user, "000");

        expect(await screen.findByText("Kalem bulunamadı.")).toBeInTheDocument();
    });

    it("OfflineBannerBlocksSale", async () => {
        const { user } = setup();
        await scan(user, "8690000000001");
        await screen.findByRole("textbox", { name: "Miktar Kalem" });
        await waitFor(() => expect(screen.getByRole("button", { name: "Tam" })).toBeEnabled());

        act(() => {
            Object.defineProperty(window.navigator, "onLine", { value: false, configurable: true });
            window.dispatchEvent(new Event("offline"));
        });

        expect(await screen.findByText(/Bağlantı yok/)).toBeInTheDocument();
        await user.type(screen.getByRole("textbox", { name: "Nakit" }), "100");
        expect(screen.getByRole("button", { name: "Öde" })).toBeDisabled();
    });

    it("SellScreen_RetryKeepsClientSaleIdUntilSuccess", async () => {
        const { user, sales } = setup();
        let attempts = 0;
        server.use(
            http.post("*/api/erp/pos/sales", async ({ request }) => {
                sales.push((await request.json()) as SaleInput);
                attempts++;
                if (attempts === 1) return HttpResponse.json(problem(500, "Sunucu hatası"), { status: 500 });
                return HttpResponse.json(saleResult(), { status: 201 });
            }),
        );
        await scan(user, "8690000000001");
        await screen.findByRole("textbox", { name: "Miktar Kalem" });

        await waitFor(() => expect(screen.getByRole("button", { name: "Tam" })).toBeEnabled());
        await user.click(screen.getByRole("button", { name: "Tam" }));
        await user.click(screen.getByRole("button", { name: "Öde" }));
        await waitFor(() => expect(sales).toHaveLength(1));
        await screen.findByText("Sunucu hatası");
        await user.click(screen.getByRole("button", { name: "Öde" }));

        const dialog = await screen.findByRole("dialog", { name: "Satış tamamlandı" });
        expect(sales).toHaveLength(2);
        expect(sales[0].clientSaleId).toBe(sales[1].clientSaleId);
        expect(sales[1]).toMatchObject({ registerId: "reg-1", lines: [{ productId: "p1", quantity: 1 }], payments: [{ method: "cash", amount: 25 }] });

        // Yeni satış başka bir kimlikle gider.
        await user.click(within(dialog).getByRole("button", { name: "Yeni satış" }));
        await scan(user, "8690000000001");
        await screen.findByRole("textbox", { name: "Miktar Kalem" });
        await waitFor(() => expect(screen.getByRole("button", { name: "Tam" })).toBeEnabled());
        await user.click(screen.getByRole("button", { name: "Tam" }));
        await user.click(screen.getByRole("button", { name: "Öde" }));
        await waitFor(() => expect(sales).toHaveLength(3));
        expect(sales[2].clientSaleId).not.toBe(sales[0].clientSaleId);
    });

    it("SellScreen_TotalAndPaymentUseServerPreview", async () => {
        // Sunucu 25 TL'lik satırı 22,50 olarak hesaplar (müşteri listesi/kampanya); istemci tahmini 25,00'dır.
        const { user, sales } = setup(undefined, (e) => e * 0.9);
        server.use(
            http.post("*/api/erp/pos/sales", async ({ request }) => {
                sales.push((await request.json()) as SaleInput);
                return HttpResponse.json(saleResult(), { status: 201 });
            }),
        );
        await scan(user, "8690000000001");

        await waitFor(() => expect(screen.getByLabelText("Sepet toplamı")).toHaveTextContent("22,50 ₺"));
        await waitFor(() => expect(screen.getByLabelText("Ödenecek tutar")).toHaveTextContent("22,50 ₺"));
        await user.click(screen.getByRole("button", { name: "Tam" }));
        await user.click(screen.getByRole("button", { name: "Öde" }));

        await waitFor(() => expect(sales).toHaveLength(1));
        expect(sales[0].payments).toEqual([{ method: "cash", amount: 22.5 }]);
        expect(sales[0].lines[0]).not.toHaveProperty("unitPrice");
    });

    it("SellScreen_PreviewDebouncedAndPayDisabledWhilePending", async () => {
        const { user, previews } = setup();
        await scan(user, "8690000000001");
        await screen.findByRole("textbox", { name: "Miktar Kalem" });
        // Önizleme gelmeden ödeme kapalıdır.
        expect(screen.getByRole("button", { name: "Tam" })).toBeDisabled();
        await waitFor(() => expect(screen.getByRole("button", { name: "Tam" })).toBeEnabled());
        expect(previews).toHaveLength(1);

        // Hızlı art arda miktar değişimi tek istek üretir; sırasında ödeme yeniden kapanır.
        const qty = screen.getByRole("textbox", { name: "Miktar Kalem" });
        await user.clear(qty);
        await user.type(qty, "14");
        expect(screen.getByRole("button", { name: "Tam" })).toBeDisabled();
        await waitFor(() => expect(screen.getByRole("button", { name: "Tam" })).toBeEnabled());
        expect(screen.getByLabelText("Sepet toplamı")).toHaveTextContent("350,00 ₺");
        expect(previews).toHaveLength(2);
        expect(previews[1].lines[0].quantity).toBe(14);
        expect(screen.getByRole("button", { name: "Tam" })).toBeEnabled();
    });

    it("SellScreen_PreviewErrorKeepsPayDisabled", async () => {
        const { user } = setup();
        server.use(http.post("*/api/erp/pos/sales/preview", () => HttpResponse.json(problem(400, "x", "price_required"), { status: 400 })));
        await scan(user, "8690000000001");

        expect(await screen.findByText("Fiyatı bulunamayan kalem için birim fiyat girilmelidir.")).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Öde" })).toBeDisabled();
    });
});
