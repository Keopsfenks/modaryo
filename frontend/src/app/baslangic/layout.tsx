import AuthShell from "@/components/auth/AuthShell";
import { SessionProvider } from "@/session/SessionProvider";

/** Oturum gerekir ama firma gerekmez; panel dışı ortalanmış düzen. */
export default function OnboardingLayout({ children }: LayoutProps<"/baslangic">) {
    return (
        <SessionProvider requireCompany={false}>
            <AuthShell>{children}</AuthShell>
        </SessionProvider>
    );
}
