import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CompanyProfileForm from "./CompanyProfileForm";

const profile = (over: Record<string, unknown> = {}) => ({
    legalName: "Örnek A.Ş.",
    taxNumber: null,
    taxOffice: null,
    mersisNo: null,
    tradeRegistryNo: null,
    address: null,
    city: null,
    district: null,
    phone: null,
    email: null,
    website: null,
    logoFileId: null,
    ibans: [],
    ...over,
});

describe("CompanyProfileForm", () => {
    it("CompanyProfile_InvalidIban_ShowsFieldError", async () => {
        let put = false;
        server.use(
            http.get("*/api/erp/settings/firma", () =>
                HttpResponse.json({ version: 1, value: profile({ ibans: [{ bankName: "Banka", iban: "TR330006100519786457841327", currency: "TRY" }] }) }),
            ),
            http.put("*/api/erp/settings/firma", () => {
                put = true;
                return HttpResponse.json({ version: 2, value: profile() });
            }),
        );
        const { user } = renderWithTheme(<CompanyProfileForm />);

        await user.click(await screen.findByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText(/IBAN geçerli değil/)).toBeInTheDocument();
        expect(put).toBe(false);
    });

    it("CompanyProfile_ServerFieldError_ShownOnField", async () => {
        server.use(
            http.get("*/api/erp/settings/firma", () => HttpResponse.json({ version: 1, value: profile() })),
            http.put("*/api/erp/settings/firma", () =>
                HttpResponse.json({ status: 400, title: "x", code: "validation_failed", errors: { Email: ["E-posta adresi geçerli değil."] } }, { status: 400 }),
            ),
        );
        const { user } = renderWithTheme(<CompanyProfileForm />);

        await user.click(await screen.findByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("E-posta adresi geçerli değil.")).toBeInTheDocument();
    });

    it("CompanyProfile_LogoUpload_SetsLogoFileId", async () => {
        let body: { value: { logoFileId: string | null } } | undefined;
        let kind: string | null = null;
        server.use(
            http.get("*/api/erp/settings/firma", () => HttpResponse.json({ version: 1, value: profile() })),
            http.post("*/api/erp/files", ({ request }) => {
                kind = new URL(request.url).searchParams.get("kind");
                return HttpResponse.json({ id: "file-1", fileName: "logo.png", contentType: "image/png", size: 3 }, { status: 201 });
            }),
            http.put("*/api/erp/settings/firma", async ({ request }) => {
                body = (await request.json()) as typeof body;
                return HttpResponse.json({ version: 2, value: profile({ logoFileId: "file-1" }) });
            }),
        );
        const { user } = renderWithTheme(<CompanyProfileForm />);

        const input = await screen.findByLabelText("Logo dosyası");
        await user.upload(input, new File(["abc"], "logo.png", { type: "image/png" }));
        await waitFor(() => expect(screen.getByAltText("Firma logosu")).toBeInTheDocument());
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(body?.value.logoFileId).toBe("file-1"));
        expect(kind).toBe("logo");
    });
});
