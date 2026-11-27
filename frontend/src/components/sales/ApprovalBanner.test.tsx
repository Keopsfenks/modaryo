import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ApprovalBanner from "./ApprovalBanner";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const base = { currency: "TRY", prepaymentRequired: 0, advanceTotal: 0, warnings: [] as string[] };

function renderBanner(permissions: string[], order: Parameters<typeof ApprovalBanner>[0]["order"], handlers = { onApproveLimit: vi.fn(), onCollectAdvance: vi.fn() }) {
    server.use(http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))));
    const result = renderWithTheme(
        <SessionProvider>
            <ApprovalBanner order={order} {...handlers} />
        </SessionProvider>,
    );
    return { ...result, ...handlers };
}

beforeEach(() => resetNavigation("/satis/siparisler/so-1"));

describe("ApprovalBanner", () => {
    it("ApprovalBanner_LimitApprovalButtonNeedsPermission", async () => {
        const without = renderBanner(["satis.read", "satis.approve"], { ...base, status: "awaitingLimitApproval" });
        expect(await screen.findByText(/kredi limiti aşılıyor/)).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Limit aşımını onayla" })).not.toBeInTheDocument();
        without.unmount();

        const { user, onApproveLimit } = renderBanner(["satis.limit-asimi"], { ...base, status: "awaitingLimitApproval" });
        await user.click(await screen.findByRole("button", { name: "Limit aşımını onayla" }));
        expect(onApproveLimit).toHaveBeenCalledTimes(1);
    });

    it("ApprovalBanner_AwaitingPaymentShowsAmountsAndAdvanceButton", async () => {
        const { user, onCollectAdvance } = renderBanner(["satis.read", "tahsilat.manage"], { ...base, status: "awaitingPayment", prepaymentRequired: 2000, advanceTotal: 500 });

        expect(await screen.findByText(/tahsil edilen 500,00 ₺ \/ gereken 2\.000,00 ₺/)).toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "Peşinat al" }));
        expect(onCollectAdvance).toHaveBeenCalledTimes(1);
    });
});
