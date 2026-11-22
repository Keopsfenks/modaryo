"use client";

import IconButton from "@mui/material/IconButton";
import Tooltip from "@mui/material/Tooltip";
import { useColorScheme } from "@mui/material/styles";
import { IconMoon, IconSun } from "@tabler/icons-react";

export default function ThemeModeToggle() {
    const { mode, systemMode, setMode } = useColorScheme();
    const resolved = mode === "system" ? systemMode : mode;
    const isDark = resolved === "dark";

    return (
        <Tooltip title={isDark ? "Açık tema" : "Koyu tema"}>
            <IconButton
                color="inherit"
                aria-label="Temayı değiştir"
                onClick={() => setMode(isDark ? "light" : "dark")}
            >
                {isDark ? <IconSun size={21} stroke={1.5} /> : <IconMoon size={21} stroke={1.5} />}
            </IconButton>
        </Tooltip>
    );
}
