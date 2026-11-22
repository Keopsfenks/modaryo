import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import SidebarContent from "@/components/layout/SidebarContent";
import { SessionProvider } from "@/session/SessionProvider";
import { demoCompany, problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CompanyCard from "./CompanyCard";
import InvitationsCard from "./InvitationsCard";
import MembersTable from "./MembersTable";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const API = "http://localhost:3000/api/identity";
const ME = "0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90";
const roles = [
    { id: "r-owner", name: "Sahip", systemKey: "owner" },
    { id: "r-admin", name: "Yönetici", systemKey: "admin" },
    { id: "r-member", name: "Kullanıcı", systemKey: "member" },
];
const member = (userId: string, fullName: string, roleKey: "owner" | "admin" | "member") => {
    const role = roles.find((r) => r.systemKey === roleKey)!;
    return { userId, email: `${userId}@ornek.com`, fullName, roleId: role.id, roleName: role.name, roleSystemKey: roleKey, joinedAt: "2026-11-20T10:00:00Z" };
};

function asUser(permissions: string[], myRole: "owner" | "admin" | "member", others = [member("veli", "Veli Kaya", "member")]) {
    server.use(
        http.get(`${API}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get(`${API}/companies/current/roles`, () => HttpResponse.json(roles)),
        http.get(`${API}/companies/current/members`, () => HttpResponse.json([member(ME, "Ali Veli", myRole), ...others])),
        http.get(`${API}/companies/current/invitations`, () => HttpResponse.json([])),
        http.get(`${API}/companies/current`, () => HttpResponse.json({ id: demoCompany.id, name: "Demo Firma", createdAt: "2026-11-20T10:00:00Z" })),
    );
}

const render = (ui: React.ReactElement) => renderWithTheme(<SessionProvider>{ui}</SessionProvider>);
const rowOf = async (name: string | RegExp) => (await screen.findByText(name)).closest("tr")!;

describe("Firma üyeleri", () => {
    beforeEach(() => resetNavigation("/firma/uyeler"));

    it("Members_HidesManageControlsWithoutPermission", async () => {
        asUser(["members.read"], "member");

        render(<><MembersTable /><InvitationsCard /></>);

        const row = await rowOf("Veli Kaya");
        expect(within(row).queryByRole("combobox")).not.toBeInTheDocument();
        expect(within(row).queryByRole("button", { name: /çıkar/i })).not.toBeInTheDocument();
        expect(within(row).getByText("Kullanıcı")).toBeInTheDocument();
        expect(screen.queryByText(/davet gönder/i)).not.toBeInTheDocument();
    });

    it("Members_OwnerOptionOnlyForOwners", async () => {
        asUser(["company.update", "members.read", "members.invite", "members.manage"], "admin");
        const { user } = render(<MembersTable />);

        await user.click(within(await rowOf("Veli Kaya")).getByRole("combobox"));

        const options = within(await screen.findByRole("listbox")).getAllByRole("option").map((o) => o.textContent);
        expect(options).toEqual(["Yönetici", "Kullanıcı"]);
    });

    it("Members_OwnRowHasNoRemoveButton", async () => {
        asUser(["*"], "owner");

        render(<MembersTable />);

        expect(within(await rowOf(/^Ali Veli/)).queryByRole("button", { name: /çıkar/i })).not.toBeInTheDocument();
        expect(within(await rowOf("Veli Kaya")).getByRole("button", { name: /çıkar/i })).toBeInTheDocument();
    });

    it("Members_ChangeRole_CallsApi", async () => {
        asUser(["*"], "owner");
        let changed: unknown;
        server.use(http.patch(`${API}/companies/current/members/:userId`, async ({ request, params }) => {
            changed = { userId: params.userId, ...(await request.json() as object) };
            return new HttpResponse(null, { status: 204 });
        }));
        const { user } = render(<MembersTable />);

        await user.click(within(await rowOf("Veli Kaya")).getByRole("combobox"));
        await user.click(within(await screen.findByRole("listbox")).getByRole("option", { name: "Yönetici" }));

        await waitFor(() => expect(changed).toEqual({ userId: "veli", roleId: "r-admin" }));
    });

    it("Members_LastOwner_ShowsServerMessage", async () => {
        asUser(["*"], "owner", []);
        server.use(http.patch(`${API}/companies/current/members/:userId`, () =>
            HttpResponse.json(problem(409, "Firmanın son sahibi bu işlemi yapamaz.", "last_owner"), { status: 409 })));
        const { user } = render(<MembersTable />);

        await user.click(within(await rowOf(/^Ali Veli/)).getByRole("combobox"));
        await user.click(within(await screen.findByRole("listbox")).getByRole("option", { name: "Kullanıcı" }));

        expect(await screen.findByText("Firmanın son sahibi bu işlemi yapamaz.")).toBeInTheDocument();
    });

    it("Members_Remove_ConfirmsThenRemovesRow", async () => {
        asUser(["*"], "owner");
        let removed: string | undefined;
        server.use(http.delete(`${API}/companies/current/members/:userId`, ({ params }) => {
            removed = params.userId as string;
            return new HttpResponse(null, { status: 204 });
        }));
        const { user } = render(<MembersTable />);

        await user.click(within(await rowOf("Veli Kaya")).getByRole("button", { name: /çıkar/i }));
        await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: /çıkar/i }));

        await waitFor(() => expect(screen.queryByText("Veli Kaya")).not.toBeInTheDocument());
        expect(removed).toBe("veli");
    });

    it("Invitations_Create_AddsToListAndClearsForm", async () => {
        asUser(["*"], "owner");
        let sent: unknown;
        server.use(http.post(`${API}/companies/current/invitations`, async ({ request }) => {
            sent = await request.json();
            return HttpResponse.json(
                { id: "inv-1", email: "yeni@ornek.com", roleId: "r-member", roleName: "Kullanıcı", expiresAt: "2026-11-29T10:00:00Z", invitedBy: "Ali Veli" },
                { status: 201 },
            );
        }));
        const { user } = render(<InvitationsCard />);

        const email = await screen.findByLabelText(/e-posta/i);
        await user.type(email, "yeni@ornek.com");
        await user.click(screen.getByRole("button", { name: /davet gönder/i }));

        expect(await screen.findByText("yeni@ornek.com")).toBeInTheDocument();
        expect(email).toHaveValue("");
        expect(sent).toEqual({ email: "yeni@ornek.com", roleId: "r-member" });
    });

    it("Invitations_AlreadyMember_ShowsFieldError", async () => {
        asUser(["*"], "owner");
        server.use(http.post(`${API}/companies/current/invitations`, () =>
            HttpResponse.json(problem(409, "Bu kişi zaten firmanın üyesi.", "already_member"), { status: 409 })));
        const { user } = render(<InvitationsCard />);

        await user.type(await screen.findByLabelText(/e-posta/i), "veli@ornek.com");
        await user.click(screen.getByRole("button", { name: /davet gönder/i }));

        const message = await screen.findByText("Bu kişi zaten firmanın üyesi.");
        expect(message.id).toBe("inviteEmail-helper-text");
    });

    it("Invitations_Revoke_RemovesRow", async () => {
        asUser(["*"], "owner");
        let revoked: string | undefined;
        server.use(
            http.get(`${API}/companies/current/invitations`, () => HttpResponse.json([
                { id: "inv-9", email: "bekleyen@ornek.com", roleId: "r-member", roleName: "Kullanıcı", expiresAt: "2026-11-29T10:00:00Z", invitedBy: "Ali Veli" },
            ])),
            http.delete(`${API}/companies/current/invitations/:id`, ({ params }) => {
                revoked = params.id as string;
                return new HttpResponse(null, { status: 204 });
            }),
        );
        const { user } = render(<InvitationsCard />);

        await user.click(within(await rowOf("bekleyen@ornek.com")).getByRole("button", { name: /İptal/ }));

        await waitFor(() => expect(screen.queryByText("bekleyen@ornek.com")).not.toBeInTheDocument());
        expect(revoked).toBe("inv-9");
    });

    it("Company_Leave_ConfirmsThenRedirectsToBaslangic", async () => {
        asUser(["members.read"], "member");
        let left = false;
        server.use(http.post(`${API}/companies/current/leave`, () => {
            left = true;
            return new HttpResponse(null, { status: 204 });
        }));
        const { user } = render(<CompanyCard />);

        expect(await screen.findByLabelText(/firma adı/i)).toHaveAttribute("readonly");
        await user.click(screen.getByRole("button", { name: /firmadan ayrıl/i }));
        await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: /ayrıl/i }));

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/baslangic"));
        expect(left).toBe(true);
    });

    it("Sidebar_HidesMembersItemWithoutPermission", async () => {
        server.use(http.get(`${API}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["company.update"] }))));

        render(<SidebarContent />);

        await screen.findByText("Genel Bakış");
        expect(screen.queryByText("Firma Üyeleri")).not.toBeInTheDocument();
    });

    it("Sidebar_ShowsMembersItemWithPermission", async () => {
        server.use(http.get(`${API}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["members.read"] }))));

        render(<SidebarContent />);

        expect(await screen.findByText("Firma Üyeleri")).toBeInTheDocument();
    });
});
