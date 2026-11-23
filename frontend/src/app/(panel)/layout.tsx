import PanelShell from "@/components/layout/PanelShell";
import SetupBanner from "@/components/setup/SetupBanner";
import { SessionProvider } from "@/session/SessionProvider";

/** Panel sayfaları oturum ve aktif firma gerektirir (yoksa /giris veya /baslangic). */
export default function PanelLayout({ children }: LayoutProps<"/">) {
    return (
        <SessionProvider>
            <PanelShell>
                <SetupBanner />
                {children}
            </PanelShell>
        </SessionProvider>
    );
}
