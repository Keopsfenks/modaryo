import type { Metadata } from "next";
import SettingsIndex from "@/components/settings/SettingsIndex";

export const metadata: Metadata = { title: "Ayarlar" };

export default function SettingsIndexPage() {
    return <SettingsIndex />;
}
