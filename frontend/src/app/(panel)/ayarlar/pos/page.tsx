import type { Metadata } from "next";
import Divider from "@mui/material/Divider";
import Stack from "@mui/material/Stack";
import PosSettingsForm from "@/components/pos/settings/PosSettingsForm";
import Registers from "@/components/pos/settings/Registers";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Kasa (POS) Ayarları" };

export default function Page() {
    return (
        <SettingsPage category="pos" title="Kasa (POS)" description="Yasal belge modu, terazi barkodları, kasa noktaları ve hızlı tuşlar.">
            <Stack spacing={4}>
                <PosSettingsForm />
                <Divider />
                <Registers />
            </Stack>
        </SettingsPage>
    );
}
