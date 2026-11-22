import type { Metadata } from "next";
import { Plus_Jakarta_Sans } from "next/font/google";
import InitColorSchemeScript from "@mui/material/InitColorSchemeScript";
import { AppRouterCacheProvider } from "@mui/material-nextjs/v16-appRouter";
import ThemeRegistry from "@/theme/ThemeRegistry";

// latin-ext: Türkçe karakterler (ş, ğ, ı, İ) için gerekli
const jakarta = Plus_Jakarta_Sans({
    variable: "--font-jakarta",
    subsets: ["latin", "latin-ext"],
    display: "swap",
});

export const metadata: Metadata = {
    title: {
        default: "Modaryo ERP",
        template: "%s | Modaryo ERP",
    },
    description: "Modaryo kurumsal kaynak planlama paneli",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
    return (
        <html lang="tr" className={jakarta.variable} suppressHydrationWarning>
            <body>
                <InitColorSchemeScript attribute="class" defaultMode="light" />
                <AppRouterCacheProvider>
                    <ThemeRegistry>{children}</ThemeRegistry>
                </AppRouterCacheProvider>
            </body>
        </html>
    );
}
