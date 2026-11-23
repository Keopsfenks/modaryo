"use client";

import PageHeader from "@/components/common/PageHeader";
import { visibleCategories } from "@/config/settings";
import { useSession } from "@/session/SessionProvider";
import NoAccess from "./NoAccess";

type SettingsPageProps = {
    /** settingsCategories içindeki anahtar. */
    category: string;
    title: string;
    description?: string;
    children: React.ReactNode;
};

/** Ayar sayfası çerçevesi: kategori oturum için görünür değilse içerik yerine NoAccess gösterir (URL ile erişim dahil). */
export default function SettingsPage({ category, title, description, children }: SettingsPageProps) {
    const session = useSession();
    const allowed = visibleCategories(session).some((c) => c.key === category);

    if (!allowed) {
        return <NoAccess />;
    }

    return (
        <>
            <PageHeader title={title} subtitle={description} />
            {children}
        </>
    );
}
