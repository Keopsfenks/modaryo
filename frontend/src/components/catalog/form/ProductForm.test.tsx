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
        http.get("*/api/erp/settings/katalog", () =>
            HttpResponse.json({
                version: 1,
                value: { codeTemplate: "U{00000}", defaultVatRate: 10, units: ["adet", "m2", "m", "saat"], barcodePrefix: "2000000", nextBarcodeSequence: 1, nextCodeSequence: 7 },
            }),
        ),
        http.get("*/api/erp/catalog/categories", () => HttpResponse.json([])),
        http.get("*/api/erp/catalog/brands", () => HttpResponse.json([])),
        http.get("*/api/erp/catalog/custom-fields", () => HttpResponse.json([])),
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
