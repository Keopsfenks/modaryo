import type { Metadata } from "next";
import RoleEditor from "@/components/roles/RoleEditor";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Yeni Rol" };

export default function Page() {
    return (
        <SettingsPage category="roller" title="Yeni Rol" description="İzinleri, iskonto sınırını ve şube kapsamını belirleyin.">
            <RoleEditor />
        </SettingsPage>
    );
}
