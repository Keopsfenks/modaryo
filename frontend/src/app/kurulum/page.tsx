import type { Metadata } from "next";
import SetupWizard from "@/components/setup/SetupWizard";

export const metadata: Metadata = { title: "Firma kurulumu" };

export default function SetupPage() {
    return <SetupWizard />;
}
