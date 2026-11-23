import type { Metadata } from "next";
import BranchesTable from "@/components/settings/BranchesTable";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Şubeler" };

export default function Page() {
    return (
        <SettingsPage category="subeler" title="Şubeler" description="Şubeleri tanımlayın.">
            <BranchesTable />
        </SettingsPage>
    );
}
