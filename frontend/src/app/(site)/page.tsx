import type { Metadata } from "next";
import ClosingCta from "@/components/site/home/ClosingCta";
import Faq from "@/components/site/home/Faq";
import FlowStory from "@/components/site/home/FlowStory";
import Hero from "@/components/site/home/Hero";
import Integrations from "@/components/site/home/Integrations";
import ModuleHighlights from "@/components/site/home/ModuleHighlights";
import SectorPicker from "@/components/site/home/SectorPicker";
import Security from "@/components/site/home/Security";
import JsonLd, { softwareApplicationJsonLd } from "@/components/site/JsonLd";
import Testimonials from "@/components/site/home/Testimonials";
import { homeFaq } from "@/content/site/faq";
import { siteUrl } from "@/content/site/seo";

export const metadata: Metadata = {
    title: { absolute: "Modaryo ERP: cari, stok, satış ve muhasebe tek yerde" },
    description: "Modaryo, işletmenizin cari, stok, satış ve muhasebe işlerini tek panelde toplar. Şu an tüm modüller ücretsiz.",
};

export default function HomePage() {
    return (
        <>
            <JsonLd data={softwareApplicationJsonLd(siteUrl())} />
            <Hero />
            <ModuleHighlights />
            <SectorPicker />
            <FlowStory />
            <Integrations />
            <Security />
            <Faq items={homeFaq} />
            <Testimonials />
            <ClosingCta />
        </>
    );
}
