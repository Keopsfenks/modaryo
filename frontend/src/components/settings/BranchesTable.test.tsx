import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { problem } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import BranchesTable from "./BranchesTable";

const BRANCHES = "*/api/identity/companies/current/branches";

const branches = [
    { id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true },
    { id: "b-2", code: "KDK", name: "Kadıköy", isActive: true, isDefault: false },
];

describe("BranchesTable", () => {
    it("Branches_DefaultRowCannotBeDeleted", async () => {
        server.use(http.get(BRANCHES, () => HttpResponse.json(branches)));
        renderWithTheme(<BranchesTable />);

        const row = (await screen.findByText("Merkez")).closest("tr")!;
        expect(within(row).getByText("Varsayılan")).toBeInTheDocument();
        expect(within(row).getByRole("button", { name: "Sil" })).toBeDisabled();
        expect(within(row).getByRole("switch", { name: "Merkez aktif" })).toBeDisabled();
        const other = screen.getByText("Kadıköy").closest("tr")!;
        expect(within(other).getByRole("button", { name: "Sil" })).toBeEnabled();
    });

    it("Branches_DuplicateCode_ShowsFieldError", async () => {
        server.use(
            http.get(BRANCHES, () => HttpResponse.json(branches)),
            http.post(BRANCHES, () => HttpResponse.json(problem(409, "Bu şube kodu zaten kullanılıyor.", "branch_code_taken"), { status: 409 })),
        );
        const { user } = renderWithTheme(<BranchesTable />);

        await user.click(await screen.findByRole("button", { name: "Yeni şube" }));
        await user.type(screen.getByLabelText(/Şube kodu/), "KDK");
        await user.type(screen.getByLabelText(/Şube adı/), "Kadıköy 2");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Bu şube kodu kullanılıyor.")).toBeInTheDocument();
        expect(screen.getByLabelText(/Şube kodu/)).toHaveAttribute("aria-invalid", "true");
    });

    it("Branches_InUse_ShowsMessage", async () => {
        server.use(
            http.get(BRANCHES, () => HttpResponse.json(branches)),
            http.delete(`${BRANCHES}/b-2`, () => HttpResponse.json(problem(409, "Şubeye bağlı kayıt var.", "branch_in_use"), { status: 409 })),
        );
        const { user } = renderWithTheme(<BranchesTable />);

        const row = (await screen.findByText("Kadıköy")).closest("tr")!;
        await user.click(within(row).getByRole("button", { name: "Sil" }));
        await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Sil" }));

        expect(await screen.findByText("Bu şubeye bağlı kayıtlar var.")).toBeInTheDocument();
    });
});
