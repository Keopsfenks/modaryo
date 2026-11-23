import { screen } from "@testing-library/react";
import type { ReactElement } from "react";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import CariListShot from "./CariListShot";
import DashboardShot from "./DashboardShot";
import DueTrackingShot from "./DueTrackingShot";
import InvoiceShot from "./InvoiceShot";
import PosShot from "./PosShot";
import ShotFrame from "./ShotFrame";
import StockShot from "./StockShot";

const shots: [string, ReactElement][] = [
    ["CariListShot", <CariListShot key="cari" />],
    ["InvoiceShot", <InvoiceShot key="invoice" />],
    ["PosShot", <PosShot key="pos" />],
    ["StockShot", <StockShot key="stock" />],
    ["DueTrackingShot", <DueTrackingShot key="due" />],
    ["DashboardShot", <DashboardShot key="dashboard" />],
];

describe("site shots", () => {
    it.each(shots)("EveryShot_HasCaptionOrnekVeri %s", (_name, shot) => {
        renderWithTheme(shot);

        expect(screen.getByText("Örnek veri")).toBeInTheDocument();
    });

    it.each(shots)("Shots_HaveAccessibleLabel %s", (_name, shot) => {
        renderWithTheme(shot);

        const frame = screen.getByRole("img");
        expect(frame).toHaveAccessibleName(/\S{3,}/);
        expect(frame.querySelector("[aria-hidden='true']")).not.toBeNull();
    });

    it("CariListShot_UsesFormatBalance", () => {
        renderWithTheme(<CariListShot />);

        expect(screen.getAllByText("(B)").length).toBeGreaterThan(0);
        expect(screen.getAllByText("(A)").length).toBeGreaterThan(0);
        expect(screen.getByText("Örnek Gıda A.Ş.")).toBeInTheDocument();
        for (const column of ["Kod", "Ad", "Rol", "Bakiye", "Vadesi geçmiş"]) {
            expect(screen.getByText(column)).toBeInTheDocument();
        }
    });

    it("ShotFrame_CustomCaption", () => {
        renderWithTheme(<ShotFrame title="Deneme">içerik</ShotFrame>);
        expect(screen.getByText("Örnek veri")).toBeInTheDocument();

        renderWithTheme(<ShotFrame title="Başka" caption="Özel not">içerik</ShotFrame>);
        expect(screen.getByText("Özel not")).toBeInTheDocument();
    });
});
