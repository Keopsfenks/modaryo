import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import SeriesTable, { validatePrefix } from "./SeriesTable";

const IDENTITY = "http://localhost:3000/api/identity";

describe("SeriesTable", () => {
    it("SeriesTable_EBelgePrefixValidation", async () => {
        const posts: unknown[] = [];
        server.use(
            http.get(`${IDENTITY}/companies/current/branches`, () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }])),
            http.get("*/api/erp/document-series", () => HttpResponse.json([])),
            http.post("*/api/erp/document-series", async ({ request }) => {
                posts.push(await request.json());
                return HttpResponse.json({ id: "s-1" }, { status: 201 });
            }),
        );
        const { user } = renderWithTheme(<SeriesTable />);

        await user.click(await screen.findByRole("button", { name: "Yeni seri" }));
        const prefix = await screen.findByLabelText("Önek");
        const save = screen.getByRole("button", { name: "Kaydet" });

        // Varsayılan mod e-Fatura: 3 karakter şart.
        await user.type(prefix, "ab");
        expect(prefix).toHaveValue("AB");
        expect(screen.getByText("e-Belge serisinin öneki 3 karakter (A-Z, 0-9) olmalıdır.")).toBeInTheDocument();
        expect(save).toBeDisabled();

        await user.type(prefix, "1");
        expect(prefix).toHaveValue("AB1");
        expect(save).toBeEnabled();
        await user.click(save);

        await screen.findByText("Bu şubede seri yok. Fatura kesmek için en az bir seri tanımlayın.");
        expect(posts[0]).toMatchObject({ branchId: "b-1", kindGroup: "sales", mode: "eInvoice", prefix: "AB1" });
    });

    it("validatePrefix_PaperAllowsFreePrefix", () => {
        expect(validatePrefix("paper", "F")).toBeNull();
        expect(validatePrefix("paper", "ABCDEFGHIJK")).not.toBeNull();
        expect(validatePrefix("paper", "A B")).not.toBeNull();
        expect(validatePrefix("eArchive", "abc")).not.toBeNull();
        expect(validatePrefix("eArchive", "A1C")).toBeNull();
    });
});
