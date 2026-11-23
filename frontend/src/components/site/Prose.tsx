import type { ReactNode } from "react";
import Box from "@mui/material/Box";

/** MDX içeriği için okunabilir metin biçimi. */
export default function Prose({ children }: { children: ReactNode }) {
    return (
        <Box
            sx={{
                maxWidth: 720,
                lineHeight: 1.7,
                fontSize: "1.0625rem",
                "& h2": { fontSize: "1.5rem", fontWeight: 700, mt: 5, mb: 1.5, letterSpacing: "-0.01em" },
                "& p": { my: 2 },
                "& ul, & ol": { pl: 3, my: 2 },
                "& li": { my: 0.5 },
                "& a": { color: "primary.main", textDecoration: "underline" },
            }}
        >
            {children}
        </Box>
    );
}
