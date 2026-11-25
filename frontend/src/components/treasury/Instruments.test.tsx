import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { instrumentDto } from "./fixtures";
import Instruments from "./Instruments";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

function setup(modules: string[], requested: string[] = []) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ modules, permissions: ["finans.read", "ceksenet.manage"] }))),
        http.get("*/api/erp/treasury/instruments/portfolio", () => HttpResponse.json([{ direction: "received", status: "inPortfolio", currency: "TRY", count: 1, total: 5000 }])),
        http.get("*/api/erp/treasury/instruments", ({ request }) => {
            requested.push(request.url);
            return HttpResponse.json([instrumentDto()]);
        }),
    );
}

const render = () =>
    renderWithTheme(
        <SessionProvider>
            <Instruments />
        </SessionProvider>,
    );

describe("Instruments", () => {
    beforeEach(() => resetNavigation("/finans/cek-senet"));

    it("Instruments_HiddenWithoutModule", async () => {
        const requested: string[] = [];
        setup([], requested);
        render();

        expect(await screen.findByText(/Çek\/senet modülü firmanızda kapalı/)).toBeInTheDocument();
        expect(screen.queryByText("CK-100")).not.toBeInTheDocument();
        expect(requested).toHaveLength(0);
    });

    it("Instruments_ListsRowsWhenModuleOpen", async () => {
        setup(["cek-senet"]);
        render();

        expect(await screen.findByText("CK-100")).toBeInTheDocument();
        expect(screen.getByText("Akın Tekstil")).toBeInTheDocument();
        expect(screen.getAllByText("Portföyde").length).toBeGreaterThan(0);
    });
});
