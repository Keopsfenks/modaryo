import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import AuditLog from "./AuditLog";

const IDENTITY = "*/api/identity/companies/current/audit";
const ERP = "*/api/erp/audit";

describe("AuditLog", () => {
    it("AuditLog_ShowsBeforeAfterDiff", async () => {
        server.use(http.get(IDENTITY, () => HttpResponse.json({
            total: 1,
            items: [{
                id: "a1", at: "2026-11-22T10:00:00Z", actorLabel: "Ali Veli", action: "role.updated", entityType: "role", entityId: "r-1",
                before: { name: "Kasiyer", maxDiscountPercent: 5 }, after: { name: "Kasiyer", maxDiscountPercent: 10 },
            }],
        })));
        const { user } = renderWithTheme(<AuditLog />);

        const row = (await screen.findByText("Rol güncellendi")).closest("tr")!;
        expect(within(row).getByText("Ali Veli")).toBeInTheDocument();
        await user.click(within(row).getByRole("button", { name: "Değişiklik" }));

        expect(await screen.findByText("maxDiscountPercent")).toBeInTheDocument();
        expect(screen.getByText("5")).toBeInTheDocument();
        expect(screen.getByText("10")).toBeInTheDocument();
        expect(screen.queryByText("name")).not.toBeInTheDocument();
    });

    it("AuditLog_TabsSwitchSource", async () => {
        server.use(
            http.get(IDENTITY, () => HttpResponse.json({ total: 0, items: [] })),
            http.get(ERP, () => HttpResponse.json({
                total: 1,
                items: [{
                    id: "e1", at: "2026-11-22T11:00:00Z", actorUserId: null, actorLabel: "Ayşe", action: "settings.updated", entityType: "settings", entityId: "finans",
                    before: "{\"periodLockDate\":null}", after: "{\"periodLockDate\":\"2026-10-31\"}",
                }],
            })),
        );
        const { user } = renderWithTheme(<AuditLog />);

        await user.click(await screen.findByRole("tab", { name: "Ayarlar" }));

        expect(await screen.findByText("Ayarlar güncellendi")).toBeInTheDocument();
        expect(screen.getByText("Ayşe")).toBeInTheDocument();
    });
});
