import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { productDto } from "./fixtures";
import ProductForm from "./ProductForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

function setup(modules: string[] = []) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ modules }))),
        // Seçenekler katalog.read ile okunur; ayarlar.katalog ucu (settings/katalog) forma gerekmez.
        http.get("*/api/erp/catalog/options", () => HttpResponse.json({ units: ["adet", "m2", "m", "saat"], defaultVatRate: 10, customFields: [] })),
        http.get("*/api/erp/catalog/categories", () => HttpResponse.json([])),
        http.get("*/api/erp/catalog/brands", () => HttpResponse.json([])),
        http.get("*/api/erp/catalog/products/next-code", () => HttpResponse.json({ code: "U00007" })),
    );
}

const render = (ui: React.ReactElement) => renderWithTheme(<SessionProvider>{ui}</SessionProvider>);

describe("ProductForm", () => {
    beforeEach(() => resetNavigation("/stok/urunler/yeni"));

    it("ProductForm_NewFillsNextCode", async () => {
        setup();
        render(<ProductForm onSaved={vi.fn()} />);

        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("U00007"));
        expect(screen.getByLabelText("KDV oranı")).toHaveTextContent("%10");
        expect(screen.getByRole("tab", { name: "Varyantlar" })).toBeInTheDocument();
    });

    it("ProductForm_ServiceHasNoVariantsTab", async () => {
        setup();
        render(<ProductForm defaultKind="service" onSaved={vi.fn()} />);

        await screen.findByLabelText("Kod");
        expect(screen.queryByRole("tab", { name: "Varyantlar" })).not.toBeInTheDocument();
        expect(screen.queryByLabelText("Ölçüyle satılır")).not.toBeInTheDocument();
        expect(screen.getByLabelText("Temel birim")).toHaveTextContent("saat");
    });

    it("ProductForm_MeasureForcesUnitAndShowsHint", async () => {
        setup();
        const { user } = render(<ProductForm onSaved={vi.fn()} />);

        await user.click(await screen.findByLabelText("Ölçüyle satılır"));

        expect(screen.getByText(/Satışta en × boy girilir\./)).toBeInTheDocument();
        const unit = screen.getByLabelText("Temel birim");
        expect(unit).toHaveAttribute("aria-disabled", "true");
        expect(unit).toHaveTextContent("m2");
    });

    it("ProductForm_SeriLotHiddenWithoutModule", async () => {
        setup([]);
        const first = render(<ProductForm onSaved={vi.fn()} />);
        await screen.findByLabelText("Kod");
        expect(screen.queryByLabelText("Seri/lot takibi")).not.toBeInTheDocument();
        first.unmount();

        setup(["seri-lot"]);
        render(<ProductForm onSaved={vi.fn()} />);
        expect(await screen.findByLabelText("Seri/lot takibi")).toBeInTheDocument();
    });

    it("ProductForm_BarcodeTaken_ShowsFieldError", async () => {
        setup();
        server.use(http.post("*/api/erp/catalog/products", () => HttpResponse.json(problem(409, "Barkod kullanılıyor.", "barcode_taken"), { status: 409 })));
        const onSaved = vi.fn();
        const { user } = render(<ProductForm onSaved={onSaved} />);

        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("U00007"));
        await user.type(screen.getByLabelText("Ad"), "Çorap");
        await user.type(screen.getByLabelText("Barkod"), "869000");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Bu barkod başka bir kalemde kullanılıyor.")).toBeInTheDocument();
        expect(onSaved).not.toHaveBeenCalled();
    });

    it("ProductForm_OptionsFailure_ShowsErrorAndDoesNotGuessDefaults", async () => {
        setup();
        server.use(http.get("*/api/erp/catalog/options", () => HttpResponse.json(problem(403, "Yetkiniz yok.", "forbidden"), { status: 403 })));
        render(<ProductForm onSaved={vi.fn()} />);

        expect(await screen.findByRole("button", { name: "Yeniden dene" })).toBeInTheDocument();
        expect(screen.queryByLabelText("Kod")).not.toBeInTheDocument();
        expect(screen.queryByLabelText("KDV oranı")).not.toBeInTheDocument();
    });

    it("ProductForm_FourDecimalPriceIsAccepted", async () => {
        setup();
        let body: Record<string, unknown> | undefined;
        server.use(
            http.post("*/api/erp/catalog/products", async ({ request }) => {
                body = (await request.json()) as Record<string, unknown>;
                return HttpResponse.json(productDto({ id: "p-9" }), { status: 201 });
            }),
        );
        const onSaved = vi.fn();
        const { user } = render(<ProductForm onSaved={onSaved} />);

        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("U00007"));
        await user.type(screen.getByLabelText("Ad"), "Çorap");
        await user.type(screen.getByLabelText("Satış fiyatı"), "12,3456");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(onSaved).toHaveBeenCalled());
        expect(body).toMatchObject({ defaultSalePrice: { amount: 12.3456 } });
    });

    it("ProductForm_FiveDecimalPriceIsRejected", async () => {
        setup();
        const { user } = render(<ProductForm onSaved={vi.fn()} />);

        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("U00007"));
        await user.type(screen.getByLabelText("Ad"), "Çorap");
        await user.type(screen.getByLabelText("Satış fiyatı"), "12,34567");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText(/en çok 4 ondalık/)).toBeInTheDocument();
    });

    it("ProductForm_SavesAndSendsBody", async () => {
        setup();
        let body: Record<string, unknown> | undefined;
        server.use(
            http.post("*/api/erp/catalog/products", async ({ request }) => {
                body = (await request.json()) as Record<string, unknown>;
                return HttpResponse.json(productDto({ id: "p-9" }), { status: 201 });
            }),
        );
        const onSaved = vi.fn();
        const { user } = render(<ProductForm onSaved={onSaved} />);

        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("U00007"));
        await user.type(screen.getByLabelText("Ad"), "Çorap");
        await user.type(screen.getByLabelText("Satış fiyatı"), "149,90");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(onSaved).toHaveBeenCalled());
        expect(body).toMatchObject({
            kind: "product",
            code: "U00007",
            name: "Çorap",
            baseUnit: "adet",
            vatRate: 10,
            defaultSalePrice: { amount: 149.9, currency: "TRY", vatIncluded: true },
        });
    });
});
