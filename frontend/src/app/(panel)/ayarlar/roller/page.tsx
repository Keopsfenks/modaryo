import type { Metadata } from "next";
import RolesTable from "@/components/roles/RolesTable";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Roller ve İzinler" };

export default function Page() {
    return (
        <SettingsPage category="roller" title="Roller ve İzinler" description="Rolleri ve izin matrisini düzenleyin.">
            <RolesTable />
        </SettingsPage>
    );
}
