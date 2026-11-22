"use client";

import Link from "next/link";
import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";

export default function Logo() {
    return (
        <Box
            component={Link}
            href="/"
            aria-label="Modaryo ERP ana sayfa"
            sx={{ display: "flex", alignItems: "center", gap: 1.25, textDecoration: "none", color: "text.primary" }}
        >
            <Box
                aria-hidden
                sx={{
                    width: 34,
                    height: 34,
                    borderRadius: "10px",
                    display: "grid",
                    placeItems: "center",
                    color: "#fff",
                    fontWeight: 800,
                    fontSize: "1.1rem",
                    background: "linear-gradient(135deg, #5D87FF 0%, #49BEFF 100%)",
                }}
            >
                M
            </Box>
            <Typography component="span" sx={{ fontWeight: 700, fontSize: "1.35rem", letterSpacing: "-0.02em" }}>
                Modaryo
            </Typography>
        </Box>
    );
}
