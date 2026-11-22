import { createTheme } from "@mui/material/styles";

// Renk paleti: modern admin panellerinde yaygın olan yumuşak mavi tonları.
// "light" değerleri açık temada pastel zemin, koyu temada koyu ton zemin olarak kullanılır.
export const brandColors = {
    primary: "#5D87FF",
    secondary: "#49BEFF",
    success: "#13DEB9",
    warning: "#FFAE1F",
    error: "#FA896B",
    info: "#539BFF",
} as const;

export const theme = createTheme({
    cssVariables: {
        colorSchemeSelector: "class",
    },
    colorSchemes: {
        light: {
            palette: {
                primary: { main: brandColors.primary, light: "#ECF2FF", dark: "#4570EA", contrastText: "#fff" },
                secondary: { main: brandColors.secondary, light: "#E8F7FF", dark: "#23AFDB", contrastText: "#fff" },
                success: { main: brandColors.success, light: "#E6FFFA", dark: "#02B3A9", contrastText: "#fff" },
                warning: { main: brandColors.warning, light: "#FEF5E5", dark: "#AE8E59", contrastText: "#fff" },
                error: { main: brandColors.error, light: "#FDEDE8", dark: "#F3704D", contrastText: "#fff" },
                info: { main: brandColors.info, light: "#EBF3FE", dark: "#1682D4", contrastText: "#fff" },
                text: { primary: "#2A3547", secondary: "#5A6A85" },
                background: { default: "#FFFFFF", paper: "#FFFFFF" },
                divider: "#E5EAEF",
                grey: { 100: "#F2F6FA", 200: "#EAEFF4", 300: "#DFE5EF", 400: "#7C8FAC", 500: "#5A6A85", 600: "#2A3547" },
            },
        },
        dark: {
            palette: {
                primary: { main: brandColors.primary, light: "#253662", dark: "#4570EA", contrastText: "#fff" },
                secondary: { main: brandColors.secondary, light: "#1C455D", dark: "#23AFDB", contrastText: "#fff" },
                success: { main: brandColors.success, light: "#1B3C48", dark: "#02B3A9", contrastText: "#fff" },
                warning: { main: brandColors.warning, light: "#4D3A2A", dark: "#AE8E59", contrastText: "#fff" },
                error: { main: brandColors.error, light: "#4B313D", dark: "#F3704D", contrastText: "#fff" },
                info: { main: brandColors.info, light: "#223662", dark: "#1682D4", contrastText: "#fff" },
                text: { primary: "#EAEFF4", secondary: "#7C8FAC" },
                background: { default: "#202936", paper: "#2A3447" },
                divider: "#333F55",
                grey: { 100: "#333F55", 200: "#465670", 300: "#7C8FAC", 400: "#DFE5EF", 500: "#EAEFF4", 600: "#F2F6FA" },
            },
        },
    },
    shape: { borderRadius: 7 },
    typography: {
        fontFamily: "var(--font-jakarta), 'Plus Jakarta Sans', sans-serif",
        h1: { fontWeight: 600, fontSize: "2.25rem", lineHeight: 1.2 },
        h2: { fontWeight: 600, fontSize: "1.875rem", lineHeight: 1.2 },
        h3: { fontWeight: 600, fontSize: "1.5rem", lineHeight: 1.3 },
        h4: { fontWeight: 600, fontSize: "1.3125rem", lineHeight: 1.4 },
        h5: { fontWeight: 600, fontSize: "1.125rem", lineHeight: 1.5 },
        h6: { fontWeight: 600, fontSize: "1rem", lineHeight: 1.5 },
        subtitle1: { fontSize: "0.875rem", fontWeight: 400 },
        subtitle2: { fontSize: "0.875rem", fontWeight: 500 },
        body1: { fontSize: "0.875rem", fontWeight: 400 },
        body2: { fontSize: "0.75rem", fontWeight: 400 },
        button: { textTransform: "none", fontWeight: 500 },
    },
    components: {
        MuiCard: {
            defaultProps: { elevation: 0 },
            styleOverrides: {
                root: ({ theme }) => ({
                    borderRadius: 12,
                    border: `1px solid ${theme.vars.palette.divider}`,
                    boxShadow: "rgba(145, 158, 171, 0.2) 0px 0px 2px 0px, rgba(145, 158, 171, 0.12) 0px 12px 24px -4px",
                    ...theme.applyStyles("dark", { boxShadow: "none" }),
                }),
            },
        },
        MuiCardContent: {
            styleOverrides: {
                root: ({ theme }) => ({
                    padding: 20,
                    "&:last-child": { paddingBottom: 20 },
                    [theme.breakpoints.up("sm")]: { padding: 30, "&:last-child": { paddingBottom: 30 } },
                }),
            },
        },
        MuiButton: {
            defaultProps: { disableElevation: true },
        },
        MuiChip: {
            styleOverrides: { root: { fontWeight: 500, borderRadius: 6 } },
        },
        MuiTableCell: {
            styleOverrides: {
                root: ({ theme }) => ({ borderColor: theme.vars.palette.divider }),
                head: { fontWeight: 600 },
            },
        },
    },
});
