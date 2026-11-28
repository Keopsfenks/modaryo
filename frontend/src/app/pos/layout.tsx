import type { Metadata } from "next";
import { SessionProvider } from "@/session/SessionProvider";

export const metadata: Metadata = { title: "Kasa" };

/** Kasa ekranı tam ekrandır: panel kabuğu (menü, üst çubuk) yok; yalnızca oturum ve aktif firma gerekir. */
export default function PosLayout({ children }: { children: React.ReactNode }) {
    return <SessionProvider>{children}</SessionProvider>;
}
