import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { ProductDto } from "@/lib/api/catalog";
import { problem } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { productDto } from "./fixtures";
import VariantsTab from "./VariantsTab";

async function addAttribute(user: ReturnType<typeof renderWithTheme>["user"], index: number, name: string, values: string[]) {
    await user.click(screen.getByRole("button", { name: "Özellik ekle" }));
    await user.type(screen.getByLabelText(`${index}. özellik adı`), name);
    for (const value of values) {
        await user.type(screen.getByLabelText(`${index}. özellik değerleri`), `${value}{Enter}`);
    }
}

describe("VariantsTab", () => {
    it("VariantsTab_PreviewCountAndSave", async () => {
        let body: unknown;
        const saved: ProductDto = productDto({ attributes: [{ id: "a1", name: "Beden", values: ["S", "M"], sort: 0 }] });
        server.use(
            http.put("*/api/erp/catalog/products/p-1/attributes", async ({ request }) => {
                body = await request.json();
                return HttpResponse.json(saved);
            }),
        );
        const onChanged = vi.fn();
        const { user } = renderWithTheme(<VariantsTab product={productDto()} canManage onChanged={onChanged} />);

        await addAttribute(user, 1, "Beden", ["S", "M", "L"]);
        await addAttribute(user, 2, "Renk", ["Kırmızı", "Mavi"]);
        expect(screen.getByText("6 varyant oluşacak")).toBeInTheDocument();

        await user.click(screen.getByRole("button", { name: "Özellikleri kaydet" }));

        await waitFor(() => expect(onChanged).toHaveBeenCalledWith(saved));
        expect(body).toEqual({
            attributes: [
                { name: "Beden", values: ["S", "M", "L"] },
                { name: "Renk", values: ["Kırmızı", "Mavi"] },
            ],
        });
        expect(await screen.findByText("Özellikler kaydedildi.")).toBeInTheDocument();
    });

    it("VariantsTab_RemoveUsedValue_ShowsVariantInUse", async () => {
        server.use(
            http.put("*/api/erp/catalog/products/p-1/attributes", () => HttpResponse.json(problem(409, "Varyant kullanımda.", "variant_in_use"), { status: 409 })),
        );
        const product = productDto({
            attributes: [{ id: "a1", name: "Beden", values: ["S", "M"], sort: 0 }],
            variants: [
                { id: "v1", code: "U00001-1", barcode: null, values: { Beden: "S" }, salePriceOverride: null, purchasePriceOverride: null, isActive: true, inUse: true },
                { id: "v2", code: "U00001-2", barcode: null, values: { Beden: "M" }, salePriceOverride: null, purchasePriceOverride: null, isActive: true, inUse: false },
            ],
        });
        const onChanged = vi.fn();
        const { user } = renderWithTheme(<VariantsTab product={product} canManage onChanged={onChanged} />);

        expect(screen.getByText("2 varyant oluşacak")).toBeInTheDocument();
        const input = screen.getByLabelText("1. özellik değerleri");
        // Son değer (M) geri tuşuyla silinir; kalan S değeri kullanımdaki varyanta aittir ama M kaldırılır.
        await user.click(input);
        await user.keyboard("{Backspace}");
        expect(screen.getByText("1 varyant oluşacak")).toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "Özellikleri kaydet" }));
        // Kaldırılan M varyantı silineceği için önce onay istenir.
        await user.click(await screen.findByRole("button", { name: "Sil ve kaydet" }));

        expect(await screen.findByText("Bu varyant kullanıldığı için kaldırılamaz; pasifleştirebilirsiniz.")).toBeInTheDocument();
        expect(onChanged).not.toHaveBeenCalled();
    });

    const withBarcodes = (): ProductDto =>
        productDto({
            attributes: [{ id: "a1", name: "Beden", values: ["S", "M"], sort: 0 }],
            variants: [
                { id: "v1", code: "U00001-1", barcode: "8690000000011", values: { Beden: "S" }, salePriceOverride: { amount: 12.3456, currency: "TRY", vatIncluded: true }, purchasePriceOverride: null, isActive: true, inUse: false },
                { id: "v2", code: "U00001-2", barcode: "8690000000028", values: { Beden: "M" }, salePriceOverride: null, purchasePriceOverride: null, isActive: true, inUse: false },
            ],
        });

    it("VariantsTab_RemovingValue_ConfirmsVariantCountBarcodesAndPrices", async () => {
        let calls = 0;
        server.use(
            http.put("*/api/erp/catalog/products/p-1/attributes", () => {
                calls++;
                return HttpResponse.json(productDto());
            }),
        );
        const { user } = renderWithTheme(<VariantsTab product={withBarcodes()} canManage onChanged={vi.fn()} />);

        await user.click(screen.getByLabelText("1. özellik değerleri"));
        await user.keyboard("{Backspace}");
        await user.click(screen.getByRole("button", { name: "Özellikleri kaydet" }));

        const dialog = await screen.findByRole("dialog");
        expect(dialog).toHaveTextContent("1 varyant silinecek");
        expect(dialog).toHaveTextContent("U00001-2");
        expect(dialog).toHaveTextContent("8690000000028");
        expect(dialog).not.toHaveTextContent("8690000000011");
        expect(calls).toBe(0);

        await user.click(screen.getByRole("button", { name: "Vazgeç" }));
        expect(calls).toBe(0);
        await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    });

    it("VariantsTab_RenamingAttribute_ListsAllVariantsWithPrices", async () => {
        const body: unknown[] = [];
        server.use(
            http.put("*/api/erp/catalog/products/p-1/attributes", async ({ request }) => {
                body.push(await request.json());
                return HttpResponse.json(productDto());
            }),
        );
        const onChanged = vi.fn();
        const { user } = renderWithTheme(<VariantsTab product={withBarcodes()} canManage onChanged={onChanged} />);

        await user.type(screen.getByLabelText("1. özellik adı"), "lar");
        await user.click(screen.getByRole("button", { name: "Özellikleri kaydet" }));

        const dialog = await screen.findByRole("dialog");
        expect(dialog).toHaveTextContent("2 varyant silinecek");
        expect(dialog).toHaveTextContent("8690000000011");
        expect(dialog).toHaveTextContent("12,3456 TRY");

        await user.click(screen.getByRole("button", { name: "Sil ve kaydet" }));
        await waitFor(() => expect(onChanged).toHaveBeenCalled());
        expect(body).toHaveLength(1);
    });

    it("VariantsTab_AddingValueOnly_SavesWithoutConfirm", async () => {
        server.use(http.put("*/api/erp/catalog/products/p-1/attributes", () => HttpResponse.json(productDto())));
        const onChanged = vi.fn();
        const { user } = renderWithTheme(<VariantsTab product={withBarcodes()} canManage onChanged={onChanged} />);

        await user.type(screen.getByLabelText("1. özellik değerleri"), "L{Enter}");
        await user.click(screen.getByRole("button", { name: "Özellikleri kaydet" }));

        await waitFor(() => expect(onChanged).toHaveBeenCalled());
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
});
