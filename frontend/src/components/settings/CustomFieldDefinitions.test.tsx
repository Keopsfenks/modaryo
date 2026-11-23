import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CustomFieldDefinitions from "./CustomFieldDefinitions";

describe("CustomFieldDefinitions", () => {
    it("CustomFields_SelectRequiresOptions", async () => {
        const posts: Record<string, unknown>[] = [];
        let items: Record<string, unknown>[] = [];
        server.use(
            http.get("*/api/erp/catalog/custom-fields", () => HttpResponse.json(items)),
            http.post("*/api/erp/catalog/custom-fields", async ({ request }) => {
                const body = (await request.json()) as Record<string, unknown>;
                posts.push(body);
                items = [{ id: "f-1", ...body, sort: 1 }];
                return HttpResponse.json(items[0], { status: 201 });
            }),
        );
        const { user } = renderWithTheme(<CustomFieldDefinitions />);

        await user.click(await screen.findByRole("button", { name: "Alan ekle" }));
        const dialog = await screen.findByRole("dialog");
        await user.type(within(dialog).getByLabelText("Anahtar"), "kumas");
        await user.type(within(dialog).getByLabelText("Alan adı"), "Kumaş");
        await user.click(within(dialog).getByLabelText("Tür"));
        await user.click(await screen.findByRole("option", { name: "Liste" }));
        await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

        expect(within(dialog).getByText("Liste alanı için en az bir seçenek girin.")).toBeInTheDocument();
        expect(posts).toHaveLength(0);

        await user.type(within(dialog).getByLabelText("Seçenekler"), "Pamuk{Enter}Keten{Enter}");
        await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(posts).toHaveLength(1));
        expect(posts[0]).toMatchObject({ key: "kumas", label: "Kumaş", type: "select", options: ["Pamuk", "Keten"], appliesToProduct: true, appliesToService: true });
        expect(await screen.findByText("kumas")).toBeInTheDocument();
    });

    it("CustomFields_KeyTakenShowsFieldError", async () => {
        server.use(
            http.get("*/api/erp/catalog/custom-fields", () => HttpResponse.json([])),
            http.post("*/api/erp/catalog/custom-fields", () => HttpResponse.json({ status: 409, title: "Anahtar kullanımda.", code: "key_taken" }, { status: 409 })),
        );
        const { user } = renderWithTheme(<CustomFieldDefinitions />);

        await user.click(await screen.findByRole("button", { name: "Alan ekle" }));
        const dialog = await screen.findByRole("dialog");
        await user.type(within(dialog).getByLabelText("Anahtar"), "renk");
        await user.type(within(dialog).getByLabelText("Alan adı"), "Renk");
        await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

        expect(await within(dialog).findByText("Bu anahtarla bir özel alan zaten tanımlı.")).toBeInTheDocument();
    });
});
