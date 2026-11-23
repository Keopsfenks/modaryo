import Container from "@mui/material/Container";
import { SessionProvider } from "@/session/SessionProvider";

/** Panel kabuğu dışında ama oturum ve aktif firma gerektirir (oturum çerezini proxy.ts, geçerliliğini SessionProvider denetler). */
export default function SetupLayout({ children }: LayoutProps<"/kurulum">) {
    return (
        <SessionProvider>
            <Container maxWidth="md" sx={{ py: 5 }}>
                {children}
            </Container>
        </SessionProvider>
    );
}
