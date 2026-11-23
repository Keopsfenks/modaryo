import { screen, within } from "@testing-library/react";
import { axe } from "vitest-axe";
import { afterEach, describe, expect, it, vi } from "vitest";
import Testimonials from "@/components/site/home/Testimonials";
import { renderWithTheme } from "@/test/render";
import HomePage from "./page";

function mockReducedMotion(reduce: boolean) {
    vi.stubGlobal("matchMedia", (query: string) => ({
        matches: reduce && query.includes("prefers-reduced-motion"),
        media: query,
        addEventListener: () => {},
        removeEventListener: () => {},
        addListener: () => {},
        removeListener: () => {},
        onchange: null,
        dispatchEvent: () => false,
    }));
}

afterEach(() => vi.unstubAllGlobals());

describe("home page", () => {
    it("Home_RendersAllSectionsInOrder", () => {
        renderWithTheme(<HomePage />);

        const headings = screen.getAllByRole("heading", { level: 1 }).concat(screen.getAllByRole("heading", { level: 2 }));
        const names = headings.map((h) => h.textContent ?? "");
        const order = [/metrekareden sat/, /bir arada/, /Sektörünüze göre/, /tek akış/, /Bağlanacağınız/, /Verileriniz/, /Sık sorulan/, /ücretsiz başlayın/];
        const indexes = order.map((re) => names.findIndex((n) => re.test(n)));

        expect(indexes.every((i) => i >= 0)).toBe(true);
        expect([...indexes].sort((a, b) => a - b)).toEqual(indexes);
        expect(screen.getAllByRole("region").length).toBeGreaterThanOrEqual(7);
    });

    it("Home_NoTestimonialsSection_WhenEmpty", () => {
        renderWithTheme(<HomePage />);

        expect(screen.queryByRole("region", { name: /Müşteri yorumları/ })).toBeNull();
        const { container } = renderWithTheme(<Testimonials items={[]} />);
        expect(container).toBeEmptyDOMElement();
    });

    it("Testimonials_RendersWhenItemsExist", () => {
        renderWithTheme(<Testimonials items={[{ quote: "Deneme yorum", name: "Ad Soyad", role: "Sahibi", company: "Örnek Ltd." }]} />);

        expect(screen.getByRole("region", { name: /Müşteri yorumları/ })).toBeInTheDocument();
    });

    it("Home_FaqHasJsonLd", () => {
        const { container } = renderWithTheme(<HomePage />);

        const types = [...container.querySelectorAll('script[type="application/ld+json"]')].map((s) => JSON.parse(s.textContent ?? "{}"));
        expect(types.map((d) => d["@type"])).toEqual(["SoftwareApplication", "FAQPage"]);
        const data = types[1];
        expect(data.mainEntity.length).toBeGreaterThan(3);
        expect(data.mainEntity[0].acceptedAnswer.text).toMatch(/ücretsiz/);
    });

    it("Home_ReducedMotion_DisablesFlowAnimation", () => {
        mockReducedMotion(true);
        renderWithTheme(<HomePage />);
        const flow = screen.getByRole("region", { name: /tek akış/ });
        expect(within(flow).getByRole("list")).toHaveAttribute("data-animated", "false");
    });

    it("Home_FlowAnimates_WhenMotionAllowed", () => {
        mockReducedMotion(false);
        renderWithTheme(<HomePage />);
        const flow = screen.getByRole("region", { name: /tek akış/ });
        expect(within(flow).getByRole("list")).toHaveAttribute("data-animated", "true");
    });

    it("Home_SectorTabs_SwitchStory", async () => {
        const { user } = renderWithTheme(<HomePage />);

        await user.click(screen.getByRole("tab", { name: /Halı ve ev tekstili/ }));

        expect(screen.getByRole("tabpanel")).toHaveTextContent(/3 taksit/);
    });

    it("Home_A11y_NoAxeViolations", async () => {
        const { container } = renderWithTheme(<HomePage />);

        const results = await axe(container, { rules: { "color-contrast": { enabled: false }, region: { enabled: false } } });

        expect(results.violations).toEqual([]);
    });
});
