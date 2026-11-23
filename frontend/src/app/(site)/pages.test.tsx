import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { siteModules } from "@/content/site/modules";
import { siteSectors } from "@/content/site/sectors";
import { renderWithTheme } from "@/test/render";
import ModulePage, { generateMetadata as moduleMetadata, generateStaticParams as moduleParams } from "./ozellikler/[modul]/page";
import SectorPage, { generateMetadata as sectorMetadata, generateStaticParams as sectorParams } from "./cozumler/[sektor]/page";
import PricingPage from "./fiyatlar/page";
import sitemap from "../sitemap";

const moduleProps = (modul: string) => ({ params: Promise.resolve({ modul }) }) as unknown as Parameters<typeof ModulePage>[0];
const sectorProps = (sektor: string) => ({ params: Promise.resolve({ sektor }) }) as unknown as Parameters<typeof SectorPage>[0];

describe("site pages", () => {
    it("EverySlug_HasPageMetadataAndSitemapEntry", async () => {
        const modulePaths = await moduleParams();
        const sectorPaths = await sectorParams();
        expect(modulePaths.map((p) => p.modul)).toEqual(siteModules.map((m) => m.slug));
        expect(sectorPaths.map((p) => p.sektor)).toEqual(siteSectors.map((s) => s.slug));

        for (const m of siteModules) {
            const meta = await moduleMetadata(moduleProps(m.slug));
            expect(meta.title).toEqual({ absolute: `${m.name} | Modaryo` });
            expect(meta.description).toBe(m.summary);
            expect(m.problem && m.solution && m.features.length >= 3 && m.shot).toBeTruthy();
        }
        for (const s of siteSectors) {
            const meta = await sectorMetadata(sectorProps(s.slug));
            expect(meta.title).toEqual({ absolute: `${s.name} | Modaryo` });
            expect(meta.description).toBe(s.summary);
            expect(s.story.length).toBeGreaterThanOrEqual(3);
        }

        const urls = sitemap().map((e) => e.url);
        for (const m of siteModules) {
            expect(urls).toContain(`http://localhost:3000/ozellikler/${m.slug}`);
        }
        for (const s of siteSectors) {
            expect(urls).toContain(`http://localhost:3000/cozumler/${s.slug}`);
        }
    });

    it("ComingModule_ShowsYakindaAndHaberVerLink", async () => {
        renderWithTheme(await ModulePage(moduleProps("satis")));

        expect(screen.getByText("Yakında")).toBeInTheDocument();
        expect(screen.getByRole("link", { name: "Haber ver" })).toHaveAttribute("href", "/demo?ilgi=satis");
    });

    it("AvailableModule_HasNoYakindaChip", async () => {
        renderWithTheme(await ModulePage(moduleProps("cari-hesaplar")));

        expect(screen.queryByText("Yakında")).toBeNull();
        expect(screen.getByRole("heading", { level: 1, name: "Cari hesaplar" })).toBeInTheDocument();
        expect(screen.getAllByRole("link", { name: "Ücretsiz başla" })[0]).toHaveAttribute("href", "/kayit");
    });

    it("UnknownSlug_NotFound", async () => {
        await expect(ModulePage(moduleProps("yok-boyle-bir-sey"))).rejects.toThrow();
        await expect(SectorPage(sectorProps("yok-boyle-bir-sey"))).rejects.toThrow();
        expect(await moduleMetadata(moduleProps("yok-boyle-bir-sey"))).toEqual({});
    });

    it("HaliSector_TellsMetrekareAndTaksitStory", async () => {
        renderWithTheme(await SectorPage(sectorProps("hali-ev-tekstili")));

        expect(screen.getAllByText(/3,68 m²/)[0]).toBeInTheDocument();
        expect(screen.getByText(/7\.360 ₺/)).toBeInTheDocument();
        expect(screen.getAllByText(/3 taksit/).length).toBeGreaterThan(0);
    });

    it("Pricing_ListsModulesWithStatus", () => {
        renderWithTheme(<PricingPage />);

        expect(screen.getByRole("heading", { level: 1, name: /tüm modüller ücretsiz/ })).toBeInTheDocument();
        expect(screen.getByText(/mevcut firmalara önceden haber verilir/)).toBeInTheDocument();
        const items = screen.getAllByRole("listitem").filter((li) => li.getAttribute("data-module"));
        expect(items).toHaveLength(siteModules.length);
        expect(screen.getAllByText("Hazır")).toHaveLength(siteModules.filter((m) => m.status === "available").length);
        expect(screen.getAllByText("Yakında")).toHaveLength(siteModules.filter((m) => m.status === "coming").length);
    });
});
