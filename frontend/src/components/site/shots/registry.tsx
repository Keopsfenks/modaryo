import type { ComponentType } from "react";
import type { ShotKey } from "@/content/site/shots";
import CariListShot from "./CariListShot";
import DashboardShot from "./DashboardShot";
import DueTrackingShot from "./DueTrackingShot";
import InvoiceShot from "./InvoiceShot";
import PosShot from "./PosShot";
import StockShot from "./StockShot";

export const shotComponents: Record<ShotKey, ComponentType> = {
    cari: CariListShot,
    invoice: InvoiceShot,
    pos: PosShot,
    stock: StockShot,
    due: DueTrackingShot,
    dashboard: DashboardShot,
};

export function Shot({ name }: { name: ShotKey }) {
    const Component = shotComponents[name];
    return <Component />;
}
