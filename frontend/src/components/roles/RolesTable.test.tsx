import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { RoleDto } from "@/lib/api/identity";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import RolesTable from "./RolesTable";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";
const ROLES = "*/api/identity/companies/current/roles";

const role = (overrides: Partial<RoleDto>): RoleDto => ({
    id: "r-1",
    name: "Rol",
    systemKey: null,
    locked: false,
    permissions: [],
    maxDiscountPercent: 0,
    allBranches: true,
    branchIds: [],
    memberCount: 0,
    ...overrides,
});

const roles = [
    role({ id: "r-owner", name: "Sahip", systemKey: "owner", locked: true, permissions: ["*"], maxDiscountPercent: 100, memberCount: 1 }),
    role({ id: "r-sales", name: "Satış", permissions: ["cari.read", "cari.write"], maxDiscountPercent: 10, allBranches: false, branchIds: ["b-1", "b-2"], memberCount: 3 }),
    role({ id: "r-empty", name: "Boş rol", permissions: ["cari.read"] }),
];

function renderTable(permissions: string[]) {
    server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo({ permissions }))), http.get(ROLES, () => HttpResponse.json(roles)));
    return renderWithTheme(
        <SessionProvider>
            <RolesTable />
        </SessionProvider>,
    );
}

const rowOf = async (name: string) => (await screen.findByText(name)).closest("tr")!;

describe("RolesTable", () => {
    it("RolesTable_ShowsOwnerAsAllPermissions", async () => {
        renderTable(["roller.read", "roller.manage"]);

        const row = await rowOf("Sahip");
        expect(within(row).getByText("Tüm yetkiler")).toBeInTheDocument();
        expect(within(row).getByText("Hazır rol")).toBeInTheDocument();
        const sales = await rowOf("Satış");
        expect(within(sales).getAllByText("2")).toHaveLength(2);
        expect(within(sales).getByText("%10")).toBeInTheDocument();
    });

    it("RolesTable_DeleteDisabledWhenMembers", async () => {
        renderTable(["roller.read", "roller.manage"]);

        expect(within(await rowOf("Satış")).getByRole("button", { name: "Sil" })).toBeDisabled();
        expect(within(await rowOf("Boş rol")).getByRole("button", { name: "Sil" })).toBeEnabled();
        expect(within(await rowOf("Boş rol")).getByRole("link", { name: "Kopyala" })).toHaveAttribute("href", "/ayarlar/roller/yeni?kopya=r-empty");
    });

    it("RolesTable_NoManage_HidesWriteActions", async () => {
        renderTable(["roller.read"]);

        const row = await rowOf("Boş rol");
        expect(within(row).getByRole("link", { name: "Görüntüle" })).toBeInTheDocument();
        expect(within(row).queryByRole("button", { name: "Sil" })).not.toBeInTheDocument();
        expect(within(row).queryByRole("link", { name: "Kopyala" })).not.toBeInTheDocument();
        expect(screen.queryByRole("link", { name: "Yeni rol" })).not.toBeInTheDocument();
    });

    it("RolesTable_Delete_RoleInUse_ShowsMessage", async () => {
        server.use(http.delete(`${ROLES}/r-empty`, () => HttpResponse.json(problem(409, "Rol kullanımda.", "role_in_use"), { status: 409 })));
        const { user } = renderTable(["roller.read", "roller.manage"]);

        await user.click(within(await rowOf("Boş rol")).getByRole("button", { name: "Sil" }));
        await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Sil" }));

        expect(await screen.findByText("Bu role atanmış üyeler var; önce başka role taşıyın.")).toBeInTheDocument();
    });
});
