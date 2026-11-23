import type { Metadata } from "next";
import CompanyProfileForm from "@/components/settings/CompanyProfileForm";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Firma Bilgileri" };

export default function Page() {
    return (
        <SettingsPage category="firma" title="Firma Bilgileri" description="Unvan, vergi, iletişim, logo ve banka hesapları.">
            <CompanyProfileForm />
        </SettingsPage>
    );
}
