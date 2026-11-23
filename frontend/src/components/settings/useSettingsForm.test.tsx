import { act, renderHook, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { problem } from "@/test/fixtures";
import { server } from "@/test/server";
import { useSettingsForm } from "./useSettingsForm";

type Value = { periodLockDate: string | null };

const getHandler = (version: number, value: Value) => http.get("*/api/erp/settings/finans", () => HttpResponse.json({ version, value }));

describe("useSettingsForm", () => {
    it("SettingsForm_LoadsValueAndVersion", async () => {
        server.use(getHandler(3, { periodLockDate: "2026-01-31" }));

        const { result } = renderHook(() => useSettingsForm<Value>("finans"));

        expect(result.current.loading).toBe(true);
        await waitFor(() => expect(result.current.loading).toBe(false));
        expect(result.current.value).toEqual({ periodLockDate: "2026-01-31" });
        expect(result.current.version).toBe(3);
    });

    it("SettingsForm_SaveSendsVersion", async () => {
        let body: unknown;
        server.use(
            getHandler(3, { periodLockDate: null }),
            http.put("*/api/erp/settings/finans", async ({ request }) => {
                body = await request.json();
                return HttpResponse.json({ version: 4, value: { periodLockDate: "2026-02-28" } });
            }),
        );
        const { result } = renderHook(() => useSettingsForm<Value>("finans"));
        await waitFor(() => expect(result.current.loading).toBe(false));

        act(() => result.current.setValue({ periodLockDate: "2026-02-28" }));
        let ok = false;
        await act(async () => {
            ok = await result.current.save();
        });

        expect(ok).toBe(true);
        expect(body).toEqual({ version: 3, value: { periodLockDate: "2026-02-28" } });
        expect(result.current.version).toBe(4);
    });

    it("SettingsForm_VersionConflict_KeepsInputAndShowsMessage", async () => {
        server.use(
            getHandler(3, { periodLockDate: null }),
            http.put("*/api/erp/settings/finans", () =>
                HttpResponse.json(problem(409, "Bu ayar siz düzenlerken değişti. Sayfayı yenileyip tekrar deneyin.", "version_conflict"), { status: 409 }),
            ),
        );
        const { result } = renderHook(() => useSettingsForm<Value>("finans"));
        await waitFor(() => expect(result.current.loading).toBe(false));

        act(() => result.current.setValue({ periodLockDate: "2026-03-31" }));
        let ok = true;
        await act(async () => {
            ok = await result.current.save();
        });

        expect(ok).toBe(false);
        expect(result.current.conflict).toBe(true);
        expect(result.current.error).toBe("Bu ayar siz düzenlerken değişti. Sayfayı yenileyip tekrar deneyin.");
        expect(result.current.value).toEqual({ periodLockDate: "2026-03-31" });
        expect(result.current.version).toBe(3);
    });
});
