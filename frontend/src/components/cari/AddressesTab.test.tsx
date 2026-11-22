import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import type { CurrentAccountDto } from "@/lib/api/erp";
import { accountDto, addressDto } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import AddressesTab from "./AddressesTab";

function Harness({ initial, canManage = true }: { initial: CurrentAccountDto; canManage?: boolean }) {
    const [account, setAccount] = useState(initial);
    return <AddressesTab account={account} canManage={canManage} onChanged={setAccount} />;
}

const dto = (overrides: Record<string, unknown> = {}) => accountDto(overrides) as unknown as CurrentAccountDto;

describe("AddressesTab", () => {
    it("Addresses_AddDefaultBilling_ShowsBadge", async () => {
        const created = addressDto({ id: "addr-2", title: "Depo", line: "Sanayi Sk. 5", isBilling: true, isShipping: false, isDefaultBilling: true });
        let posted: Record<string, unknown> | null = null;
        server.use(
            http.post("*/api/erp/current-accounts/acc-1/addresses", async ({ request }) => {
                posted = (await request.json()) as Record<string, unknown>;
                return HttpResponse.json(created, { status: 201 });
            }),
            http.get("*/api/erp/current-accounts/acc-1", () => HttpResponse.json(accountDto({ addresses: [created] }))),
        );
        const { user } = renderWithTheme(<Harness initial={dto()} />);

        await user.click(screen.getByRole("button", { name: "Adres ekle" }));
        const dialog = screen.getByRole("dialog");
        await user.type(within(dialog).getByLabelText("Başlık"), "Depo");
        await user.type(within(dialog).getByLabelText("Adres"), "Sanayi Sk. 5");
        await user.click(within(dialog).getByLabelText("Fatura adresi"));
        await user.click(within(dialog).getByLabelText("Varsayılan fatura adresi"));
        await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Sanayi Sk. 5")).toBeInTheDocument();
        expect(screen.getByText("Varsayılan fatura")).toBeInTheDocument();
        expect(posted).toMatchObject({ title: "Depo", line: "Sanayi Sk. 5", isBilling: true, isShipping: false, isDefaultBilling: true });
    });

    it("Addresses_Dialog_RequiresTypeAndFields", async () => {
        const { user } = renderWithTheme(<Harness initial={dto()} />);

        await user.click(screen.getByRole("button", { name: "Adres ekle" }));
        const dialog = screen.getByRole("dialog");
        await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

        expect(within(dialog).getByText("Başlık 1 ile 100 karakter arasında olmalı.")).toBeInTheDocument();
        expect(within(dialog).getByText("Adres 1 ile 500 karakter arasında olmalı.")).toBeInTheDocument();
        expect(within(dialog).getByText("Fatura veya teslimat adresinden en az biri seçilmeli.")).toBeInTheDocument();
    });

    it("Addresses_DeleteRemovesCard", async () => {
        const a1 = addressDto();
        const a2 = addressDto({ id: "addr-2", title: "Depo", line: "Sanayi Sk. 5", isDefaultBilling: false });
        server.use(
            http.delete("*/api/erp/current-accounts/acc-1/addresses/addr-2", () => new HttpResponse(null, { status: 204 })),
            http.get("*/api/erp/current-accounts/acc-1", () => HttpResponse.json(accountDto({ addresses: [a1] }))),
        );
        const { user } = renderWithTheme(<Harness initial={dto({ addresses: [a1, a2] })} />);
        expect(screen.getByText("Sanayi Sk. 5")).toBeInTheDocument();

        await user.click(screen.getByRole("button", { name: "Depo adresini sil" }));

        await waitFor(() => expect(screen.queryByText("Sanayi Sk. 5")).not.toBeInTheDocument());
        expect(screen.getByText("Atatürk Cad. 1")).toBeInTheDocument();
    });

    it("Addresses_ReadOnlyWithoutManage", () => {
        renderWithTheme(<Harness initial={dto({ addresses: [addressDto()] })} canManage={false} />);

        expect(screen.getByText("Atatürk Cad. 1")).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Adres ekle" })).not.toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Merkez adresini sil" })).not.toBeInTheDocument();
    });

    it("Addresses_EmptyShowsMessage", () => {
        renderWithTheme(<Harness initial={dto()} />);

        expect(screen.getByText("Henüz adres yok")).toBeInTheDocument();
    });
});
