import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import BrandList from "./BrandList";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

describe("BrandList", () => {
    it("DeleteInUse_ShowsMessage", async () => {
        server.use(
            http.get("http://localhost:3000/api/identity/auth/session", () => HttpResponse.json(sessionInfo({ permissions: ["katalog.read", "katalog.manage"] }))),
            http.get("*/api/erp/catalog/brands", () => HttpResponse.json([{ id: "b-1", name: "Modaryo", productCount: 3 }])),
            http.delete("*/api/erp/catalog/brands/b-1", () => HttpResponse.json(problem(409, "x", "brand_in_use"), { status: 409 })),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <BrandList />
            </SessionProvider>,
        );
        await screen.findByText("Modaryo");

        await user.click(screen.getByRole("button", { name: "Modaryo markasını sil" }));
        await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Sil" }));

        expect(await screen.findByText(/markaya bağlı ürünler var/)).toBeInTheDocument();
    });
});
