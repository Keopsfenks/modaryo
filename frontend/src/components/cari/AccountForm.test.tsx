import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { CurrentAccountDto } from "@/lib/api/erp";
import { problem } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import AccountForm from "./AccountForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const dto = (overrides: Partial<CurrentAccountDto> = {}): CurrentAccountDto => ({
    id: "acc-1",
    code: "C-0001",
    name: "Akın Tekstil",
    kind: "corporate",
    taxNumber: null,
    taxOffice: null,
    isEInvoiceTaxpayer: false,
    isCustomer: true,
    isSupplier: false,
    phone: null,
    email: null,
    notes: null,
    currency: "TRY",
    defaultDueDays: 0,
    creditLimit: null,
    isActive: true,
    balance: 0,
    overdueAmount: 0,
    hasMovements: false,
    addresses: [],
    ...overrides,
});

function mockNextCode(code = "C-0007") {
    server.use(http.get("*/api/erp/current-accounts/next-code", () => HttpResponse.json({ code })));
}

function mockCreate(sent: { body?: unknown }) {
    server.use(
        http.post("*/api/erp/current-accounts", async ({ request }) => {
            sent.body = await request.json();
            return HttpResponse.json(dto(), { status: 201 });
        }),
    );
}

describe("AccountForm", () => {
    beforeEach(() => resetNavigation("/cari/yeni"));

    it("Form_FillsNextCode", async () => {
        mockNextCode("C-0007");

        renderWithTheme(<AccountForm onSaved={vi.fn()} />);

        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("C-0007"));
    });

    it("Form_NewCardPrefillsThirtyDueDays", async () => {
        mockNextCode();

        renderWithTheme(<AccountForm onSaved={vi.fn()} />);

        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("C-0007"));
        expect(screen.getByLabelText(/vade/i)).toHaveValue("30");
    });

    it("Form_VknValidation", async () => {
        mockNextCode();
        const sent: { body?: unknown } = {};
        mockCreate(sent);
        const onSaved = vi.fn();
        const { user } = renderWithTheme(<AccountForm defaultRole="customer" onSaved={onSaved} />);
        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("C-0007"));
        await user.type(screen.getByLabelText(/ad \/ unvan/i), "Akın Tekstil");

        await user.type(screen.getByLabelText("VKN"), "1234567891");
        await user.click(screen.getByRole("button", { name: /kaydet/i }));

        expect(await screen.findByText(/geçerli bir vkn/i)).toBeInTheDocument();
        expect(sent.body).toBeUndefined();

        await user.clear(screen.getByLabelText("VKN"));
        await user.type(screen.getByLabelText("VKN"), "1234567890");
        await user.click(screen.getByRole("button", { name: /kaydet/i }));

        await waitFor(() => expect(onSaved).toHaveBeenCalled());
        expect((sent.body as { taxNumber: string }).taxNumber).toBe("1234567890");
    });

    it("Form_TcknLabelForIndividual", async () => {
        mockNextCode();
        const { user } = renderWithTheme(<AccountForm onSaved={vi.fn()} />);

        await user.click(screen.getByLabelText("Şahıs"));

        expect(screen.getByLabelText("TCKN")).toBeInTheDocument();
    });

    it("Form_RequiresARole", async () => {
        mockNextCode();
        const sent: { body?: unknown } = {};
        mockCreate(sent);
        const { user } = renderWithTheme(<AccountForm onSaved={vi.fn()} />);
        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("C-0007"));
        await user.type(screen.getByLabelText(/ad \/ unvan/i), "Akın Tekstil");

        await user.click(screen.getByRole("button", { name: /kaydet/i }));

        expect(await screen.findByText(/en az biri seçilmeli/i)).toBeInTheDocument();
        expect(sent.body).toBeUndefined();
    });

    it("Form_OpeningBalanceSent", async () => {
        mockNextCode();
        const sent: { body?: Record<string, unknown> } = {};
        mockCreate(sent);
        const { user } = renderWithTheme(<AccountForm defaultRole="supplier" onSaved={vi.fn()} />);
        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("C-0007"));
        await user.type(screen.getByLabelText(/ad \/ unvan/i), "Sürmene Kumaş");
        await user.type(screen.getByLabelText(/açılış tutarı/i), "1500,50");
        await user.click(screen.getByRole("combobox", { name: /açılış yönü/i }));
        await user.click(await screen.findByRole("option", { name: "Alacak" }));

        await user.click(screen.getByRole("button", { name: /kaydet/i }));

        await waitFor(() => expect(sent.body).toBeDefined());
        expect(sent.body!.openingBalance).toMatchObject({ amount: 1500.5, direction: "credit" });
        expect(sent.body!.isSupplier).toBe(true);
        expect(sent.body!.isCustomer).toBe(false);
        expect(sent.body!.address).toBeNull();
    });

    it("Form_CurrencyLockedWhenHasMovements", () => {
        renderWithTheme(<AccountForm initial={dto({ hasMovements: true })} onSaved={vi.fn()} />);

        expect(screen.getByRole("combobox", { name: /para birimi/i })).toHaveAttribute("aria-disabled", "true");
        expect(screen.queryByLabelText(/açılış tutarı/i)).not.toBeInTheDocument();
    });

    it("Form_ServerCodeTaken_ShowsFieldError", async () => {
        mockNextCode("C-0007");
        server.use(
            http.post("*/api/erp/current-accounts", () =>
                HttpResponse.json(problem(409, "Kod kullanımda.", "code_taken"), { status: 409 })),
        );
        const { user } = renderWithTheme(<AccountForm defaultRole="customer" onSaved={vi.fn()} />);
        await waitFor(() => expect(screen.getByLabelText("Kod")).toHaveValue("C-0007"));
        await user.type(screen.getByLabelText(/ad \/ unvan/i), "Akın Tekstil");
        mockNextCode("C-0008");

        await user.click(screen.getByRole("button", { name: /kaydet/i }));

        const message = await screen.findByText(/bu kod kullanılıyor/i);
        expect(message.id).toBe("code-helper-text");
        expect(message).toHaveTextContent("C-0008");
    });
});
