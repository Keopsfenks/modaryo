import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PickListDto } from "@/lib/api/shipping";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import PickScreen from "./PickScreen";
import { pickLine, pickList } from "./shippingFixtures";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

beforeEach(() => resetNavigation("/sevkiyat/toplama/pk-1"));

function setup(initial: PickListDto) {
    let list = initial;
    const puts: { lineId: string; body: { pickedQty: number; serials?: string[] } }[] = [];
    const completes: { force: boolean }[] = [];
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["sevkiyat.read", "sevkiyat.pick"] }))),
        http.get("*/api/erp/shipping/pick-lists/pk-1", () => HttpResponse.json(list)),
        http.put("*/api/erp/shipping/pick-lists/pk-1/lines/:lineId", async ({ request, params }) => {
            const body = (await request.json()) as { pickedQty: number; serials?: string[] };
            puts.push({ lineId: String(params.lineId), body });
            list = { ...list, lines: list.lines.map((l) => (l.id === params.lineId ? { ...l, pickedQuantity: body.pickedQty, pickedSerials: body.serials ?? l.pickedSerials } : l)) };
            return HttpResponse.json(list);
        }),
        http.post("*/api/erp/shipping/pick-lists/pk-1/complete", async ({ request }) => {
            completes.push((await request.json()) as { force: boolean });
            return HttpResponse.json({ ...list, status: "completed" });
        }),
    );
    const view = renderWithTheme(
        <SessionProvider>
            <PickScreen pickListId="pk-1" />
        </SessionProvider>,
    );
    return { ...view, puts, completes };
}

describe("PickScreen", () => {
    it("PickScreen_ScanIncrementsWrongItemErrors", async () => {
        const { user, puts } = setup(pickList());

        const scan = await screen.findByRole("textbox", { name: "Barkod / kod okut" });
        // Okutma alanı her zaman odaktadır.
        await waitFor(() => expect(scan).toHaveFocus());
        await user.type(scan, "hali01{Enter}");

        await waitFor(() => expect(puts).toEqual([{ lineId: "pl-1", body: expect.objectContaining({ pickedQty: 1 }) }]));
        expect(await screen.findByText("1 / 3")).toBeInTheDocument();
        await waitFor(() => expect(scan).toHaveFocus());

        // Listede olmayan kalem hata verir ve hiçbir şey yazılmaz.
        await user.type(scan, "YOK99{Enter}");
        expect(await screen.findByRole("alert")).toHaveTextContent("YOK99");
        expect(puts).toHaveLength(1);
    });

    it("PickScreen_ShortPickConfirms", async () => {
        const list = pickList({
            lines: [pickLine({ pickedQuantity: 2 }), pickLine({ id: "pl-2", sort: 2, code: "KIL01", description: "Kilim", quantity: 1, pickedQuantity: 1 })],
        });
        const { user, completes } = setup(list);

        await user.click(await screen.findByRole("button", { name: "Toplamayı tamamla" }));
        // Eksik toplama onay ister; onaylanmadan tamamlanmaz.
        const dialog = await screen.findByRole("dialog", { name: "Eksik toplanan satırlar var" });
        expect(dialog).toHaveTextContent("Yün halı");
        expect(completes).toHaveLength(0);
        await user.click(screen.getByRole("button", { name: "Yine de tamamla" }));

        await waitFor(() => expect(completes).toEqual([{ force: true }]));
        await waitFor(() => expect(router.push).toHaveBeenCalled());
    });

    it("PickScreen_FullPickCompletesWithoutConfirm", async () => {
        const { user, completes } = setup(pickList({ lines: [pickLine({ pickedQuantity: 3 })] }));

        await user.click(await screen.findByRole("button", { name: "Toplamayı tamamla" }));

        await waitFor(() => expect(completes).toEqual([{ force: false }]));
    });

    it("PickScreen_ScanMatchesBarcodesAndUnitFactor", async () => {
        const line = pickLine({
            quantity: 24,
            factor: 1,
            barcodes: [
                { value: "8690000000017", kind: "product", variantId: null, unitFactor: 1 },
                { value: "8690000000024", kind: "unit", variantId: null, unitFactor: 12 },
            ],
        });
        const { user, puts } = setup(pickList({ lines: [line] }));

        const scan = await screen.findByRole("textbox", { name: "Barkod / kod okut" });
        await user.type(scan, "8690000000017{Enter}");
        await waitFor(() => expect(puts).toHaveLength(1));
        expect(puts[0].body.pickedQty).toBe(1);
        // Koli (birim) barkodu birim çarpanı kadar ekler.
        await user.type(scan, "8690000000024{Enter}");
        await waitFor(() => expect(puts).toHaveLength(2));
        expect(puts[1].body.pickedQty).toBe(13);
    });

    it("PickScreen_SerialLineCollectsSerialsAndSendsThem", async () => {
        const line = pickLine({ quantity: 2, trackingMode: "serial", barcodes: [{ value: "869", kind: "product", variantId: null, unitFactor: 1 }] });
        const { user, puts } = setup(pickList({ lines: [line] }));

        await user.type(await screen.findByRole("textbox", { name: "Yün halı seri no okut" }), "SN-1{Enter}");
        await waitFor(() => expect(puts).toHaveLength(1));
        expect(puts[0].body).toEqual({ pickedQty: 1, serials: ["SN-1"] });
        await user.type(screen.getByRole("textbox", { name: "Yün halı seri no okut" }), "SN-2{Enter}");
        await waitFor(() => expect(puts).toHaveLength(2));
        expect(puts[1].body).toEqual({ pickedQty: 2, serials: ["SN-1", "SN-2"] });
        // Aynı seri ikinci kez okutulamaz.
        expect(await screen.findByText("2 / 2")).toBeInTheDocument();
    });

    it("PickScreen_BarcodeScanOfSerialLineAsksForSerialInstead", async () => {
        const line = pickLine({ quantity: 2, trackingMode: "serial", barcodes: [{ value: "869", kind: "product", variantId: null, unitFactor: 1 }] });
        const { user, puts } = setup(pickList({ lines: [line] }));

        await user.type(await screen.findByRole("textbox", { name: "Barkod / kod okut" }), "869{Enter}");

        expect(await screen.findByRole("alert")).toHaveTextContent("seri numarasını okutun");
        expect(puts).toHaveLength(0);
    });
});
