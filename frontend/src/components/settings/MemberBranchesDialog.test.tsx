import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { BranchDto, MemberDto, RoleDto } from "@/lib/api/identity";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import MemberBranchesDialog from "./MemberBranchesDialog";

const API = "http://localhost:3000/api/identity";

const branches: BranchDto[] = [
    { id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true },
    { id: "b-2", code: "KDK", name: "Kadıköy", isActive: true, isDefault: false },
    { id: "b-3", code: "BKR", name: "Bakırköy", isActive: true, isDefault: false },
];
const role = (over: Partial<RoleDto>): RoleDto => ({
    id: "r-1", name: "Kasiyer", systemKey: null, locked: false, permissions: [], maxDiscountPercent: 0, allBranches: false, branchIds: ["b-1", "b-2"], memberCount: 1, ...over,
});
const member = (over: Partial<MemberDto> = {}): MemberDto => ({
    userId: "veli", email: "veli@ornek.com", fullName: "Veli Kaya", roleId: "r-1", roleName: "Kasiyer", roleSystemKey: null, joinedAt: "2026-11-20T10:00:00Z", branchIds: null, ...over,
});

describe("MemberBranchesDialog", () => {
    it("MemberBranches_LimitedToRoleScope", async () => {
        server.use(http.get(`${API}/companies/current/branches`, () => HttpResponse.json(branches)));
        const { user } = renderWithTheme(<MemberBranchesDialog member={member()} role={role({})} onClose={vi.fn()} onSaved={vi.fn()} />);

        await user.click(await screen.findByLabelText("Seçili şubeler"));

        expect(await screen.findByLabelText(/Merkez/)).toBeInTheDocument();
        expect(screen.getByLabelText(/Kadıköy/)).toBeInTheDocument();
        expect(screen.queryByLabelText(/Bakırköy/)).not.toBeInTheDocument();
    });

    it("MemberBranches_NullMeansRoleScope", async () => {
        server.use(http.get(`${API}/companies/current/branches`, () => HttpResponse.json(branches)));
        let sent: unknown = "none";
        server.use(http.put(`${API}/companies/current/members/veli/branches`, async ({ request }) => {
            sent = await request.json();
            return new HttpResponse(null, { status: 204 });
        }));
        const onSaved = vi.fn();
        const { user } = renderWithTheme(<MemberBranchesDialog member={member({ branchIds: ["b-1"] })} role={role({})} onClose={vi.fn()} onSaved={onSaved} />);

        await user.click(await screen.findByLabelText("Rolün şubeleri"));
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(sent).toEqual({ branchIds: null }));
        expect(onSaved).toHaveBeenCalled();
    });
});
