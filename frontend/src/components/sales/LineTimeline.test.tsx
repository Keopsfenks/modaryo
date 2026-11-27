import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import LineTimeline from "./LineTimeline";

describe("LineTimeline", () => {
    it("LineTimeline_ShowsCargoNote", () => {
        renderWithTheme(
            <LineTimeline
                steps={[
                    { key: "ordered", label: "Tedarikçiye sipariş verildi", state: "done", at: "2026-11-23T08:00:00Z", note: "SA2026000004" },
                    { key: "in-transit", label: "Kargoda", state: "current", at: null, note: "İrsaliye 778" },
                    { key: "arrived", label: "Geldi", state: "pending", at: null, note: null },
                    { key: "delivered", label: "Müşteriye teslim edildi", state: "pending", at: null, note: null },
                ]}
            />,
        );

        expect(screen.getByText("İrsaliye 778")).toBeInTheDocument();
        expect(screen.getByText("Kargoda").closest("li")).toHaveAttribute("data-state", "current");
        expect(screen.getAllByRole("listitem")).toHaveLength(4);
    });

    it("LineTimeline_EmptyRendersNothing", () => {
        const { container } = renderWithTheme(<LineTimeline steps={[]} />);
        expect(container.querySelector("ol")).toBeNull();
    });
});
