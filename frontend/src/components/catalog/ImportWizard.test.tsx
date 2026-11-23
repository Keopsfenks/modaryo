import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { catalog, type ImportPreview } from "@/lib/api/catalog";
import ImportWizard from "./ImportWizard";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";
const XLSX = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

const preview = (overrides: Record<string, unknown> = {}) => ({
    batchId: "b-1",
    total: 2,
    creates: 1,
    updates: 1,
    errors: 0,
    rows: [
        { rowNumber: 2, action: "create", code: "U1", name: "Çorap", errors: [] },
        { rowNumber: 3, action: "update", code: "U2", name: "Tişört", errors: [] },
    ],
    ...overrides,
});

const render = () => renderWithTheme(<SessionProvider><ImportWizard /></SessionProvider>);

async function upload(user: ReturnType<typeof renderWithTheme>["user"]) {
    const file = new File(["x"], "urunler.xlsx", { type: XLSX });
    await user.upload(await screen.findByLabelText("Excel dosyası"), file);
    await user.click(screen.getByRole("button", { name: "Önizle" }));
}

describe("ImportWizard", () => {
    beforeEach(() => {
        resetNavigation("/stok/ice-aktar");
        server.use(http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo())));
    });

    it("ImportWizard_ErrorsBlockCommit", async () => {
        // jsdom + msw, bekleyen çok parçalı isteği yarıda kestiği için bekleyen yanıt doğrudan istemciden taklit edilir.
        let release: (value: ImportPreview) => void = () => undefined;
        vi.spyOn(catalog, "previewImport").mockReturnValue(
            new Promise<ImportPreview>((resolve) => {
                release = resolve;
            }),
        );
        const errorPreview = preview({
            errors: 1,
            creates: 0,
            rows: [
                { rowNumber: 2, action: "update", code: "U2", name: "Tişört", errors: [] },
                { rowNumber: 3, action: "error", code: "U3", name: "Hatalı", errors: ["KDV oranı geçersiz."] },
            ],
        }) as ImportPreview;
        const { user } = render();

        await upload(user);
        expect(await screen.findByText(/Dosya inceleniyor/)).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Önizle" })).toBeDisabled();
        release(errorPreview);

        expect(await screen.findByText("KDV oranı geçersiz.")).toBeInTheDocument();
        expect(screen.getByText("1 hatalı")).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "İçe aktar" })).toBeDisabled();

        await user.click(screen.getByRole("button", { name: "Hatalı satırları atla" }));
        expect(screen.getByText(/dosyadan silip dosyayı yeniden yükleyin/)).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Önizle" })).toBeDisabled();
    });

    it("ImportWizard_CommitShowsResult", async () => {
        server.use(
            http.post("*/api/erp/catalog/products/import/preview", () => HttpResponse.json(preview())),
            http.post("*/api/erp/catalog/products/import/b-1/commit", () => HttpResponse.json({ created: 1, updated: 1 })),
        );
        const { user } = render();

        await upload(user);
        await user.click(await screen.findByRole("button", { name: "İçe aktar" }));

        expect(await screen.findByText(/1 kart oluşturuldu, 1 kart güncellendi/)).toBeInTheDocument();
        expect(screen.getByRole("link", { name: "Ürünlere git" })).toHaveAttribute("href", "/stok/urunler");
    });

    it("ImportWizard_ConflictShowsFreshPreview", async () => {
        const committed: string[] = [];
        server.use(
            http.post("*/api/erp/catalog/products/import/preview", () => HttpResponse.json(preview())),
            http.post("*/api/erp/catalog/products/import/b-1/commit", () => {
                committed.push("b-1");
                return HttpResponse.json(
                    {
                        status: 409,
                        code: "import_conflict",
                        title: "Önizlemeden sonra veriler değişti.",
                        preview: preview({ batchId: "b-2", total: 1, creates: 0, updates: 1, rows: [{ rowNumber: 2, action: "update", code: "U1", name: "Çorap", errors: [] }] }),
                    },
                    { status: 409, headers: { "Content-Type": "application/problem+json" } },
                );
            }),
            http.post("*/api/erp/catalog/products/import/b-2/commit", () => {
                committed.push("b-2");
                return HttpResponse.json({ created: 0, updated: 1 });
            }),
        );
        const { user } = render();

        await upload(user);
        await user.click(await screen.findByRole("button", { name: "İçe aktar" }));

        expect(await screen.findByText(/hiçbir şey yazılmadı\. Yeni önizlemeyi kontrol edin/)).toBeInTheDocument();
        expect(screen.getByText("0 yeni")).toBeInTheDocument();
        expect(screen.getByText("1 satır")).toBeInTheDocument();

        await user.click(screen.getByRole("button", { name: "İçe aktar" }));

        expect(await screen.findByText(/0 kart oluşturuldu, 1 kart güncellendi/)).toBeInTheDocument();
        expect(committed).toEqual(["b-1", "b-2"]);
    });

    it("ImportWizard_ExpiredAsksReupload", async () => {
        server.use(
            http.post("*/api/erp/catalog/products/import/preview", () => HttpResponse.json(preview())),
            http.post("*/api/erp/catalog/products/import/b-1/commit", () => HttpResponse.json(problem(410, "Süre doldu.", "import_expired"), { status: 410 })),
        );
        const { user } = render();

        await upload(user);
        await user.click(await screen.findByRole("button", { name: "İçe aktar" }));

        expect(await screen.findByText(/Önizlemenin süresi doldu/)).toBeInTheDocument();
        expect(screen.getByLabelText("Excel dosyası")).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Önizle" })).toBeDisabled();
    });

    it("ImportWizard_HasErrorsFromServerReturnsToUpload", async () => {
        server.use(
            http.post("*/api/erp/catalog/products/import/preview", () => HttpResponse.json(preview())),
            http.post("*/api/erp/catalog/products/import/b-1/commit", () => HttpResponse.json(problem(400, "Hatalı satırlar var.", "import_has_errors"), { status: 400 })),
        );
        const { user } = render();

        await upload(user);
        await user.click(await screen.findByRole("button", { name: "İçe aktar" }));

        await waitFor(() => expect(screen.getByText(/Dosyada hatalı satırlar var; hiçbir şey yazılmadı/)).toBeInTheDocument());
        expect(screen.getByRole("button", { name: "Önizle" })).toBeInTheDocument();
    });
});
