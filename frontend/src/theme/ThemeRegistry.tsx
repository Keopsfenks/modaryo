"use client";

import CssBaseline from "@mui/material/CssBaseline";
import GlobalStyles from "@mui/material/GlobalStyles";
import { ThemeProvider } from "@mui/material/styles";
import { theme } from "./theme";

export default function ThemeRegistry({ children }: { children: React.ReactNode }) {
    return (
        <ThemeProvider theme={theme} defaultMode="light" disableTransitionOnChange>
            <CssBaseline />
            <GlobalStyles styles={{ "@media print": { ".no-print": { display: "none !important" } } }} />
            {children}
        </ThemeProvider>
    );
}
