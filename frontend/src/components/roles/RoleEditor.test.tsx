import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PermissionGroup, RoleDto } from "@/lib/api/identity";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import RoleEditor from "./RoleEditor";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";
const ROLES = "*/api/identity/companies/current/roles";
const ROLE = "*/api/identity/companies/current/roles/:id";
const RESET = "*/api/identity/companies/current/roles/:id/reset";
const BRANCHES = "*/api/identity/companies/current/branches";
const CATALOG = "*/api/identity/permissions";

const catalog: PermissionGroup[] = [
    {
        group: "Cari",
        moduleKey: null,
        permissions: [
            { code: "cari.read", name: "Cari görüntüle", description: "" },
            { code: "cari.write", name: "Cari düzenle", description: "" },
        ],
    },
];

const role = (o: Partial<RoleDto> = {}): RoleDto => ({
    id: "r-1",
    name: "Satış",
    systemKey: null,
    locked: false,
    permissions: ["cari.read"],
    maxDiscountPercent: 10,
    allBranches: true,
    branchIds: [],
    memberCount: 0,
    ...o,
});

const branches = [
    { id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true },
    { id: "b-2", code: "SB2", name: "Kadıköy", isActive: true, isDefault: false },
];

function setup(ui: React.ReactElement, permissions: string[], roleDto?: RoleDto) {
    server.use(
        http.get(SESSION, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get(CATALOG, () => HttpResponse.json({ groups: catalog })),
        http.get(BRANCHES, () => HttpResponse.json(branches)),
        http.get(ROLE, () => HttpResponse.json(roleDto ?? role())),
    );
    return renderWithTheme(<SessionProvider>{ui}</SessionProvider>);
}

beforeEach(() => resetNavigation("/ayarlar/roller/yeni"));

describe("RoleEditor", () => {
    it("RoleEditor_DisablesPermissionsCallerLacks", async () => {
        setup(<RoleEditor />, ["roller.read", "roller.manage", "cari.read"]);

        expect(await screen.findByRole("checkbox", { name: /Cari görüntüle/ })).toBeEnabled();
        expect(screen.getByRole("checkbox", { name: /Cari düzenle/ })).toBeDisabled();
    });

    it("RoleEditor_ServerRejectsGrant_ShowsMessage", async () => {
        server.use(http.post(ROLES, () => HttpResponse.json(problem(403, "Yetkisiz", "permission_not_grantable"), { status: 403 })));
        const { user } = setup(<RoleEditor />, ["*"]);

        await user.type(await screen.findByLabelText("Rol adı"), "Yeni");
        await user.click(screen.getByRole("checkbox", { name: /Cari düzenle/ }));
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Sahip olmadığınız bir izni veremezsiniz.")).toBeInTheDocument();
        expect(router.push).not.toHaveBeenCalled();
    });

    it("RoleEditor_OwnerReadOnly", async () => {
        setup(<RoleEditor roleId="r-owner" />, ["*"], role({ id: "r-owner", name: "Sahip", systemKey: "owner", locked: true, permissions: ["*"] }));

        expect(await screen.findByText("Sahip rolü tüm yetkilere sahiptir ve değiştirilemez.")).toBeInTheDocument();
        expect(screen.getByLabelText("Rol adı")).toBeDisabled();
        expect(screen.getByRole("checkbox", { name: /Cari düzenle/ })).toBeDisabled();
        expect(screen.queryByRole("button", { name: "Kaydet" })).not.toBeInTheDocument();
    });

    it("RoleEditor_WithoutManage_IsReadOnly", async () => {
        setup(<RoleEditor roleId="r-1" />, ["roller.read", "cari.read", "cari.write"]);

        expect(await screen.findByLabelText("Rol adı")).toBeDisabled();
        expect(screen.queryByRole("button", { name: "Kaydet" })).not.toBeInTheDocument();
    });

    it("RoleEditor_SelectedBranchesRequiresAtLeastOne", async () => {
        let posted = false;
        server.use(
            http.post(ROLES, () => {
                posted = true;
                return HttpResponse.json(role());
            }),
        );
        const { user } = setup(<RoleEditor />, ["*"]);

        await user.type(await screen.findByLabelText("Rol adı"), "Yeni");
        await user.click(screen.getByRole("radio", { name: "Seçili şubeler" }));
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("En az bir şube seçin.")).toBeInTheDocument();
        expect(posted).toBe(false);
    });

    it("RoleEditor_CopyPrefillsName", async () => {
        resetNavigation("/ayarlar/roller/yeni", "kopya=r-1");
        setup(<RoleEditor />, ["*"]);

        await waitFor(() => expect(screen.getByLabelText("Rol adı")).toHaveValue("Satış (kopya)"));
        expect(screen.getByRole("checkbox", { name: /Cari görüntüle/ })).toBeChecked();
    });

    it("RoleEditor_CopyOwner_ExpandsStarAndDropsUngrantable", async () => {
        resetNavigation("/ayarlar/roller/yeni", "kopya=r-1");
        setup(<RoleEditor />, ["roller.read", "roller.manage", "cari.read"], role({ name: "Sahip", permissions: ["*"] }));

        expect(await screen.findByText("Sahip olmadığınız izinler kopyaya alınmadı.")).toBeInTheDocument();
        expect(screen.getByRole("checkbox", { name: /Cari görüntüle/ })).toBeChecked();
        expect(screen.getByRole("checkbox", { name: /Cari düzenle/ })).not.toBeChecked();
    });

    it("RoleEditor_CopyOwnerAsOwner_ExpandsStarWithoutAlert", async () => {
        resetNavigation("/ayarlar/roller/yeni", "kopya=r-1");
        let body: { permissions: string[] } | undefined;
        server.use(
            http.post(ROLES, async ({ request }) => {
                body = (await request.json()) as { permissions: string[] };
                return HttpResponse.json(role());
            }),
        );
        const { user } = setup(<RoleEditor />, ["*"], role({ name: "Sahip", permissions: ["*"] }));

        await waitFor(() => expect(screen.getByRole("checkbox", { name: /Cari düzenle/ })).toBeChecked());
        expect(screen.queryByText("Sahip olmadığınız izinler kopyaya alınmadı.")).not.toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "Kaydet" }));
        await waitFor(() => expect(body).toBeDefined());
        expect(body!.permissions).toEqual(["cari.read", "cari.write"]);
    });

    it("RoleEditor_EditKeepsUngrantableChecked_CanUncheck", async () => {
        const { user } = setup(<RoleEditor roleId="r-1" />, ["roller.read", "roller.manage"], role({ permissions: ["cari.read"] }));

        const box = await screen.findByRole("checkbox", { name: /Cari görüntüle/ });
        expect(box).toBeChecked();
        expect(box).toBeEnabled();
        await user.click(box);
        const after = screen.getByRole("checkbox", { name: /Cari görüntüle/ });
        expect(after).not.toBeChecked();
        expect(after).toBeDisabled();
    });

    it("RoleEditor_ResetSystemRole_AsksConfirm", async () => {
        let reset = false;
        server.use(
            http.post(RESET, () => {
                reset = true;
                return HttpResponse.json(role({ systemKey: "admin" }));
            }),
        );
        const { user } = setup(<RoleEditor roleId="r-1" />, ["*"], role({ systemKey: "admin" }));

        await user.click(await screen.findByRole("button", { name: "Varsayılana dön" }));
        expect(reset).toBe(false);
        expect(screen.getByText(/hazır varsayılanlara döner/)).toBeInTheDocument();

        await user.click(screen.getByRole("button", { name: "Evet, döndür" }));
        await waitFor(() => expect(reset).toBe(true));
    });

    it("RoleEditor_DuplicateName_ShowsFieldError", async () => {
        server.use(http.post(ROLES, () => HttpResponse.json(problem(409, "Çakışma", "role_name_taken"), { status: 409 })));
        const { user } = setup(<RoleEditor />, ["*"]);

        await user.type(await screen.findByLabelText("Rol adı"), "Satış");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Bu adda bir rol zaten var.")).toBeInTheDocument();
        expect(screen.getByLabelText("Rol adı")).toHaveAttribute("aria-invalid", "true");
    });
});
