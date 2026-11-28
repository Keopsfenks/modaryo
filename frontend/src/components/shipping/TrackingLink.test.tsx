import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import TrackingLink from "./TrackingLink";

describe("TrackingLink", () => {
    it("TrackingLink_UsesTemplate", () => {
        render(<TrackingLink template="https://kargo.example/q?code={no}" trackingNo="AB 12/3" />);
        const link = screen.getByRole("link", { name: "AB 12/3" });
        expect(link).toHaveAttribute("href", "https://kargo.example/q?code=AB%2012%2F3");
        expect(link).toHaveAttribute("target", "_blank");
        expect(link).toHaveAttribute("rel", expect.stringContaining("noopener"));
    });

    it("TrackingLink_WithoutTemplateIsPlainText", () => {
        render(<TrackingLink template={null} trackingNo="AB123" />);
        expect(screen.queryByRole("link")).not.toBeInTheDocument();
        expect(screen.getByText("AB123")).toBeInTheDocument();
    });
});
