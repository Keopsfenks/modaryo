import type { ComponentType } from "react";
import CompanyStep from "@/components/setup/steps/CompanyStep";
import DoneStep from "@/components/setup/steps/DoneStep";
import EBelgeStep from "@/components/setup/steps/EBelgeStep";
import SectorStep from "@/components/setup/steps/SectorStep";
import WarehouseStep from "@/components/setup/steps/WarehouseStep";

export type SetupStepProps = { onNext(): void; onBack(): void };

export type SetupStep = {
    key: string;
    title: string;
    order: number;
    /** Verilirse adım yalnızca bu modül açıkken gösterilir. */
    module?: string;
    /** true ise adım kendi ilerleme düğmelerini sunar; sihirbaz genel Geri/İleri düğmelerini göstermez. */
    ownNavigation?: boolean;
    Component: ComponentType<SetupStepProps>;
};

export type Sector = "perakende" | "toptan" | "uretim" | "hizmet" | "eticaret" | "diger";

/** Sektör seçimi → açılacak modüller. Dalga 2 modülleri sonraki planlarda eklenir. */
export const sectorModules: Record<Sector, string[]> = {
    perakende: ["pos", "seri-lot"],
    toptan: ["siparise-gore-tedarik", "teslimat-plani", "cek-senet"],
    uretim: ["seri-lot", "cek-senet"],
    hizmet: [],
    eticaret: ["siparise-gore-tedarik"],
    diger: [],
};

export const sectorLabels: Record<Sector, string> = {
    perakende: "Perakende / Mağaza",
    toptan: "Toptan / Dağıtım",
    uretim: "Üretim",
    hizmet: "Hizmet",
    eticaret: "E-ticaret",
    diger: "Diğer",
};

/**
 * Sihirbaz adımları (order'a göre sıralanır). Sonraki planlar buraya adım ekler:
 * stok (P3) depo 30, ön muhasebe (P5) kasa 35, fatura (P4) e-belge/kağıt modu 40.
 */
export const setupSteps: SetupStep[] = [
    { key: "company", title: "Firma bilgileri", order: 10, ownNavigation: true, Component: CompanyStep },
    { key: "sector", title: "Sektör", order: 20, ownNavigation: true, Component: SectorStep },
    { key: "warehouse", title: "İlk depo", order: 30, ownNavigation: true, Component: WarehouseStep },
    { key: "ebelge", title: "e-Fatura", order: 40, ownNavigation: true, Component: EBelgeStep },
    { key: "done", title: "Tamam", order: 1000, ownNavigation: true, Component: DoneStep },
];
