import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import AccountList from "./AccountList";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const item = (overrides: Record<string, unknown> = {}) => ({
    id: "acc-1",
    code: "C-0001",
    name: "Akın Tekstil",
    isCustomer: true,
    isSupplier: false,
    currency: "TRY",
    balance: 0,
    overLimit: false,
    overdueAmount: 0,
    isActive: true,
    ...overrides,
});

function setup(items: unknown[], permissions = ["cari.read", "cari.manage"]) {
    const queries: URLSearchParams[] = [];
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get("*/api/erp/current-accounts", ({ request }) => {
            queries.push(new URL(request.url).searchParams);
            return HttpResponse.json({ items, total: items.length });
        }),
    );
    return queries;
}

const render = (ui: React.ReactElement) => renderWithTheme(<SessionProvider>{ui}</SessionProvider>);

describe("AccountList", () => {
    beforeEach(() => resetNavigation("/finans/cari-hesaplar"));

    it("List_ShowsBalanceWithBA", async () => {
        setup([item({ id: "s1", code: "T-0001", name: "Sürmene Kumaş", isCustomer: false, isSupplier: true, balance: -500, overdueAmount: 120 })]);

        render(<AccountList title="Cari Hesaplar" />);

        expect(await screen.findByText("500,00 ₺ (A)")).toBeInTheDocument();
        expect(screen.getByText("120,00 ₺")).toBeInTheDocument();
        expect(screen.getByText("Tedarikçi")).toBeInTheDocument();
    });

    it("List_RoleFilterSentToApi", async () => {
        const queries = setup([item()]);

        render(<AccountList title="Müşteriler" role="customer" />);

        await screen.findByText("Akın Tekstil");
        expect(queries[0].get("role")).toBe("customer");
        expect(queries[0].get("page")).toBe("1");
        expect(queries[0].get("pageSize")).toBe("25");
    });

    it("List_SearchIsDebouncedAndResetsPage", async () => {
        const queries = setup([item()]);
        const { user } = render(<AccountList title="Cari Hesaplar" />);
        await screen.findByText("Akın Tekstil");

        await user.type(screen.getByLabelText(/ara/i), "akın");

        await waitFor(() => expect(queries[queries.length - 1].get("search")).toBe("akın"));
        expect(queries.length).toBe(2);
        expect(queries[1].get("page")).toBe("1");
    });

    it("List_OverLimitBadge", async () => {
        setup([item({ overLimit: true }), item({ id: "p", name: "Pasif Ltd", isActive: false })]);

        render(<AccountList title="Cari Hesaplar" />);

        expect(await screen.findByText("Limit aşıldı")).toBeInTheDocument();
        expect(screen.getByText("Pasif")).toBeInTheDocument();
    });

    it("List_HidesNewButtonWithoutManage", async () => {
        setup([item()], ["cari.read"]);

        render(<AccountList title="Cari Hesaplar" />);

        await screen.findByText("Akın Tekstil");
        expect(screen.queryByRole("link", { name: /yeni cari/i })).not.toBeInTheDocument();
    });

    it("List_ShowsNewButtonWithManage", async () => {
        setup([item()]);

        render(<AccountList title="Tedarikçiler" role="supplier" />);

        const link = await screen.findByRole("link", { name: /yeni cari/i });
        expect(link).toHaveAttribute("href", "/cari/yeni?rol=tedarikci");
    });

    it("List_EmptyShowsMessage", async () => {
        setup([]);

        render(<AccountList title="Cari Hesaplar" />);

        expect(await screen.findByText("Henüz cari yok")).toBeInTheDocument();
        expect(screen.getAllByRole("link", { name: /yeni cari/i })).toHaveLength(1);
    });

    it("List_RowClickNavigates", async () => {
        setup([item({ id: "abc" })]);
        const { user } = render(<AccountList title="Cari Hesaplar" />);

        await user.click(within((await screen.findByText("Akın Tekstil")).closest("tr")!).getByText("C-0001"));

        expect(router.push).toHaveBeenCalledWith("/cari/abc");
    });
});
