import type { Metadata } from "next";
import FinanceSettingsForm from "@/components/settings/FinanceSettingsForm";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Finans" };

export default function Page() {
    return (
        <SettingsPage category="finans" title="Finans" description="Dönem kilidi.">
            <FinanceSettingsForm />
        </SettingsPage>
    );
}
