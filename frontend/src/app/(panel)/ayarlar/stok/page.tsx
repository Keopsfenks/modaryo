import type { Metadata } from "next";
import SettingsPage from "@/components/settings/SettingsPage";
import StockSettingsForm from "@/components/settings/StockSettingsForm";

export const metadata: Metadata = { title: "Stok Ayarları" };

export default function Page() {
    return (
        <SettingsPage category="stok" title="Stok" description="Maliyet yöntemi ve eksi stok tercihi.">
            <StockSettingsForm />
        </SettingsPage>
    );
}
