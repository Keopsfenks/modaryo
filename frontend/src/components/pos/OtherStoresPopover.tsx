"use client";

import { useState } from "react";
import { IconBuildingStore } from "@tabler/icons-react";
import Button from "@mui/material/Button";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import ListItemText from "@mui/material/ListItemText";
import Popover from "@mui/material/Popover";
import type { OtherStore } from "@/lib/api/pos";
import { fmtQty } from "./format";

/** Kalemin diğer şubelerdeki kullanılabilir miktarı (yalnızca miktar). */
export default function OtherStoresPopover({ stores, name }: { stores: OtherStore[]; name: string }) {
    const [anchor, setAnchor] = useState<HTMLElement | null>(null);
    return (
        <>
            <Button size="small" startIcon={<IconBuildingStore size={16} />} aria-label={`Diğer şubeler ${name}`} onClick={(e) => setAnchor(e.currentTarget)}>
                Diğer şubeler
            </Button>
            <Popover open={!!anchor} anchorEl={anchor} onClose={() => setAnchor(null)} anchorOrigin={{ vertical: "bottom", horizontal: "left" }}>
                <List dense sx={{ minWidth: 220 }}>
                    {stores.map((s) => (
                        <ListItem key={s.branchId}>
                            <ListItemText primary={s.branchName} secondary={`${fmtQty(s.available)} ${s.unit}`} />
                        </ListItem>
                    ))}
                </List>
            </Popover>
        </>
    );
}
