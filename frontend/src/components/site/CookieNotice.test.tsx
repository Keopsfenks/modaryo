import { screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import CookieNotice from "./CookieNotice";

describe("CookieNotice", () => {
    afterEach(() => localStorage.clear());

    it("CookieNotice_DismissPersists", async () => {
        const first = renderWithTheme(<CookieNotice />);
        expect(await screen.findByText(/zorunlu çerez/)).toBeInTheDocument();
        await first.user.click(screen.getByRole("button", { name: "Anladım" }));
        expect(screen.queryByText(/zorunlu çerez/)).toBeNull();
        first.unmount();

        renderWithTheme(<CookieNotice />);
        await Promise.resolve();
        expect(screen.queryByText(/zorunlu çerez/)).toBeNull();
    });

    it("CookieNotice_StorageThrows_StillRenders", async () => {
        vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("blocked"); });
        vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("blocked"); });

        const { user } = renderWithTheme(<CookieNotice />);
        expect(await screen.findByText(/zorunlu çerez/)).toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "Anladım" }));
        expect(screen.queryByText(/zorunlu çerez/)).toBeNull();
    });
});
