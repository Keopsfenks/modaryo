import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SaleInput } from "@/lib/api/pos";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { lookup, REGISTER, saleResult, SHIFT, item } from "./posFixtures";
import SellScreen from "./SellScreen";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));
vi.setConfig({ testTimeout: 20000 });

function setup(lookups: Record<string, ReturnType<typeof lookup>> = { "8690000000001": lookup() }) {
    const sales: SaleInput[] = [];
    server.use(
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
    return { ...view, sales };
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
        expect(screen.getByRole("button", { name: "Tam" })).toBeEnabled();

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
        await user.click(screen.getByRole("button", { name: "Tam" }));
        await user.click(screen.getByRole("button", { name: "Öde" }));
        await waitFor(() => expect(sales).toHaveLength(3));
        expect(sales[2].clientSaleId).not.toBe(sales[0].clientSaleId);
    });
});
