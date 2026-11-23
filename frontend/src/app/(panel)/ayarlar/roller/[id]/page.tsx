import type { Metadata } from "next";
import RoleEditor from "@/components/roles/RoleEditor";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Rol Düzenle" };

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return (
        <SettingsPage category="roller" title="Rol Düzenle" description="İzinleri, iskonto sınırını ve şube kapsamını düzenleyin.">
            <RoleEditor roleId={id} />
        </SettingsPage>
    );
}
