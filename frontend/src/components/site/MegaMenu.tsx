"use client";

import { useId, useState } from "react";
import Link from "next/link";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Popover from "@mui/material/Popover";
import Typography from "@mui/material/Typography";
import { IconChevronDown } from "@tabler/icons-react";
import type { MenuGroup } from "@/content/site/navigation";

type MegaMenuProps = {
    label: string;
    groups: MenuGroup[];
};

/**
 * Açılır menü. Tetikleyici bir düğmedir (Enter/Boşluk açar); panel MUI Popover olduğu için
 * Escape kapatır ve odak açıkken panelin içinde tutulur.
 */
export default function MegaMenu({ label, groups }: MegaMenuProps) {
    const [anchor, setAnchor] = useState<HTMLElement | null>(null);
    const panelId = useId();
    const open = Boolean(anchor);
    const close = () => setAnchor(null);
    const columns = Math.min(groups.length, 4);

    return (
        <>
            <Button
                color="inherit"
                aria-haspopup="dialog"
                aria-expanded={open}
                aria-controls={open ? panelId : undefined}
                onClick={(event) => setAnchor(event.currentTarget)}
                endIcon={<IconChevronDown size={16} aria-hidden />}
                sx={{ color: "text.primary", fontWeight: 500, px: 1.5 }}
            >
                {label}
            </Button>
            <Popover
                open={open}
                anchorEl={anchor}
                onClose={close}
                anchorOrigin={{ vertical: "bottom", horizontal: "left" }}
                slotProps={{
                    paper: {
                        id: panelId,
                        role: "dialog",
                        "aria-label": label,
                        sx: { mt: 1, p: 3, maxWidth: "min(960px, calc(100vw - 32px))" },
                    },
                }}
            >
                <Box sx={{ display: "grid", gridTemplateColumns: `repeat(${columns}, minmax(200px, 1fr))`, gap: 4 }}>
                    {groups.map((group, index) => (
                        <Box key={group.title ?? index}>
                            {group.title && (
                                <Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 1, color: "text.secondary" }}>
                                    {group.title}
                                </Typography>
                            )}
                            <Box component="ul" sx={{ listStyle: "none", m: 0, p: 0, display: "grid", gap: 0.5 }}>
                                {group.items.map((item) => (
                                    <li key={item.href}>
                                        <Box
                                            component={Link}
                                            href={item.href}
                                            onClick={close}
                                            sx={{
                                                display: "block",
                                                p: 1,
                                                borderRadius: 1,
                                                textDecoration: "none",
                                                color: "text.primary",
                                                "&:hover, &:focus-visible": { bgcolor: "action.hover" },
                                            }}
                                        >
                                            <Typography component="span" sx={{ fontWeight: 600, fontSize: "0.9rem" }}>
                                                {item.label}
                                            </Typography>
                                            {item.soon && <Chip label="Yakında" size="small" sx={{ ml: 1, height: 18, fontSize: "0.65rem" }} />}
                                            {item.summary && (
                                                <Typography component="span" color="text.secondary" sx={{ display: "block", fontSize: "0.78rem", lineHeight: 1.45 }}>
                                                    {item.summary}
                                                </Typography>
                                            )}
                                        </Box>
                                    </li>
                                ))}
                            </Box>
                        </Box>
                    ))}
                </Box>
            </Popover>
        </>
    );
}
