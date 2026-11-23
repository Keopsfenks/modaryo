import { screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { PermissionGroup } from "@/lib/api/identity";
import { renderWithTheme } from "@/test/render";
import PermissionMatrix from "./PermissionMatrix";

const groups: PermissionGroup[] = [
    {
        group: "Cari",
        moduleKey: null,
        permissions: [
            { code: "cari.read", name: "Cari görüntüle", description: "" },
            { code: "cari.write", name: "Cari düzenle", description: "" },
            { code: "cari.delete", name: "Cari sil", description: "" },
        ],
    },
    { group: "Stok", moduleKey: null, permissions: [{ code: "stok.read", name: "Stok görüntüle", description: "" }] },
];

const grpOf = (name: string) => within(screen.getByRole("group", { name }));

describe("PermissionMatrix", () => {
    it("PermissionMatrix_QuickSelectRead_SelectsDotReadCodes", async () => {
        const onChange = vi.fn();
        const { user } = renderWithTheme(<PermissionMatrix groups={groups} selected={new Set()} grantable={() => true} onChange={onChange} />);

        await user.click(grpOf("Cari").getByRole("button", { name: "Görüntüle" }));

        expect([...onChange.mock.calls[0][0]]).toEqual(["cari.read"]);
    });

    it("PermissionMatrix_QuickSelectAll", async () => {
        const onChange = vi.fn();
        const { user } = renderWithTheme(<PermissionMatrix groups={groups} selected={new Set(["stok.read"])} grantable={() => true} onChange={onChange} />);

        await user.click(grpOf("Cari").getByRole("button", { name: "Tümü" }));

        expect([...onChange.mock.calls[0][0]].sort()).toEqual(["cari.delete", "cari.read", "cari.write", "stok.read"]);
    });

    it("PermissionMatrix_QuickSelectEdit_SelectsNonReadCodes", async () => {
        const onChange = vi.fn();
        const { user } = renderWithTheme(<PermissionMatrix groups={groups} selected={new Set()} grantable={() => true} onChange={onChange} />);

        await user.click(grpOf("Cari").getByRole("button", { name: "Düzenle" }));

        expect([...onChange.mock.calls[0][0]].sort()).toEqual(["cari.delete", "cari.write"]);
    });

    it("PermissionMatrix_QuickSelect_SkipsUngrantable", async () => {
        const onChange = vi.fn();
        const { user } = renderWithTheme(
            <PermissionMatrix groups={groups} selected={new Set()} grantable={(c) => c !== "cari.delete"} onChange={onChange} />,
        );

        await user.click(grpOf("Cari").getByRole("button", { name: "Tümü" }));

        expect([...onChange.mock.calls[0][0]].sort()).toEqual(["cari.read", "cari.write"]);
    });
});
