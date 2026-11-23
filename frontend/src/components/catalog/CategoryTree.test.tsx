import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CategoryTree from "./CategoryTree";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const node = (id: string, name: string, sort: number, parentId: string | null = null, children: unknown[] = []) => ({
    id,
    name,
    parentId,
    sort,
    productCount: 0,
    children,
});

const tree = () => [node("c-1", "Giyim", 0, null, [node("c-2", "Tişört", 0, "c-1")]), node("c-3", "Ayakkabı", 1)];

function setup(permissions = ["katalog.read", "katalog.manage"]) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get("*/api/erp/catalog/categories", () => HttpResponse.json(tree())),
    );
}

const render = (ui: React.ReactElement) => renderWithTheme(<SessionProvider>{ui}</SessionProvider>);

describe("CategoryTree", () => {
    beforeEach(() => resetNavigation("/stok/kategoriler"));

    it("CategoryTree_AddChildAndRename", async () => {
        setup();
        const calls: { method: string; body: Record<string, unknown> }[] = [];
        server.use(
            http.post("*/api/erp/catalog/categories", async ({ request }) => {
                const body = (await request.json()) as Record<string, unknown>;
                calls.push({ method: "POST", body });
                return HttpResponse.json(node("c-9", String(body.name), 0, String(body.parentId)), { status: 201 });
            }),
            http.put("*/api/erp/catalog/categories/c-3", async ({ request }) => {
                const body = (await request.json()) as Record<string, unknown>;
                calls.push({ method: "PUT", body });
                return HttpResponse.json(node("c-3", String(body.name), 1));
            }),
        );
        const { user } = render(<CategoryTree />);
        await screen.findByText("Giyim");

        await user.click(screen.getByRole("button", { name: "Giyim altına ekle" }));
        await user.type(screen.getByLabelText("Alt kategori adı"), "Elbise");
        await user.click(screen.getByRole("button", { name: "Ekle" }));
        await waitFor(() => expect(calls).toContainEqual({ method: "POST", body: { name: "Elbise", parentId: "c-1" } }));

        await user.click(screen.getByRole("button", { name: "Ayakkabı adını değiştir" }));
        const input = screen.getByLabelText("Kategori adı");
        await user.clear(input);
        await user.type(input, "Bot");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));
        await waitFor(() => expect(calls).toContainEqual({ method: "PUT", body: { name: "Bot", parentId: null, sort: 1 } }));
    });

    it("CategoryTree_DeleteWithChildren_ShowsMessage", async () => {
        setup();
        server.use(
            http.delete("*/api/erp/catalog/categories/c-1", () =>
                HttpResponse.json(problem(409, "Sunucu başlığı", "category_has_children"), { status: 409 }),
            ),
        );
        const { user } = render(<CategoryTree />);
        await screen.findByText("Giyim");

        await user.click(screen.getByRole("button", { name: "Giyim kategorisini sil" }));
        await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Sil" }));

        expect(await screen.findByText(/Alt kategorisi olan kategori silinemez/)).toBeInTheDocument();
    });

    it("MoveDown_RenumbersSiblings", async () => {
        setup();
        const puts: Record<string, Record<string, unknown>> = {};
        server.use(
            http.put("*/api/erp/catalog/categories/:id", async ({ request, params }) => {
                const body = (await request.json()) as Record<string, unknown>;
                puts[String(params.id)] = body;
                return HttpResponse.json(node(String(params.id), String(body.name), Number(body.sort)));
            }),
        );
        const { user } = render(<CategoryTree />);
        await screen.findByText("Giyim");

        await user.click(screen.getByRole("button", { name: "Giyim aşağı taşı" }));

        await waitFor(() => expect(Object.keys(puts).sort()).toEqual(["c-1", "c-3"]));
        expect(puts["c-3"].sort).toBe(0);
        expect(puts["c-1"].sort).toBe(1);
    });

    it("HidesWriteControlsWithoutManage", async () => {
        setup(["katalog.read"]);

        render(<CategoryTree />);

        await screen.findByText("Giyim");
        expect(screen.queryByRole("button", { name: /sil$/ })).not.toBeInTheDocument();
        expect(screen.queryByLabelText("Yeni kategori")).not.toBeInTheDocument();
    });
});
