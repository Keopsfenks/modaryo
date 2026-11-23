"use client";

import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import type { PermissionGroup } from "@/lib/api/identity";

export const NOT_GRANTABLE_HINT = "Bu izne sahip olmadığınız için veremezsiniz.";

type PermissionMatrixProps = {
    groups: PermissionGroup[];
    selected: Set<string>;
    /** Çağıranın verebileceği izin mi? Değilse onay kutusu devre dışıdır. */
    grantable: (code: string) => boolean;
    onChange(next: Set<string>): void;
    readOnly?: boolean;
};

/** İzin matrisi: grup başına hızlı seçim (Görüntüle / Düzenle / Tümü) ve izin başına onay kutusu. */
export default function PermissionMatrix({ groups, selected, grantable, onChange, readOnly }: PermissionMatrixProps) {
    const toggle = (code: string, checked: boolean) => {
        const next = new Set(selected);
        if (checked) next.add(code);
        else next.delete(code);
        onChange(next);
    };

    const quickSelect = (group: PermissionGroup, pick: (code: string) => boolean) => {
        const next = new Set(selected);
        for (const p of group.permissions) {
            if (pick(p.code) && grantable(p.code)) next.add(p.code);
        }
        onChange(next);
    };

    return (
        <Stack spacing={2}>
            {groups.map((group) => (
                <Box key={group.group} role="group" aria-label={group.group} sx={{ border: 1, borderColor: "divider", borderRadius: 1, p: 2 }}>
                    <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", mb: 1 }}>
                        <Typography variant="subtitle1" sx={{ flexGrow: 1, fontWeight: 600 }}>
                            {group.group}
                        </Typography>
                        {!readOnly && (
                            <>
                                <Button size="small" onClick={() => quickSelect(group, (c) => c.endsWith(".read"))}>
                                    Görüntüle
                                </Button>
                                <Button size="small" onClick={() => quickSelect(group, (c) => !c.endsWith(".read"))}>
                                    Düzenle
                                </Button>
                                <Button size="small" onClick={() => quickSelect(group, () => true)}>
                                    Tümü
                                </Button>
                            </>
                        )}
                    </Stack>
                    <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" } }}>
                        {group.permissions.map((p) => {
                            const allowed = grantable(p.code);
                            // Verilemeyen ama zaten işaretli izin kaldırılabilir; sunucu yalnızca yeni eklenenleri denetler.
                            const disabled = readOnly || (!allowed && !selected.has(p.code));
                            const control = (
                                <FormControlLabel
                                    control={
                                        <Checkbox
                                            size="small"
                                            checked={selected.has(p.code)}
                                            disabled={disabled}
                                            onChange={(e) => toggle(p.code, e.target.checked)}
                                        />
                                    }
                                    label={p.name}
                                />
                            );
                            return !readOnly && !allowed && !selected.has(p.code) ? (
                                <Tooltip key={p.code} title={NOT_GRANTABLE_HINT}>
                                    <span>{control}</span>
                                </Tooltip>
                            ) : (
                                <span key={p.code}>{control}</span>
                            );
                        })}
                    </Box>
                </Box>
            ))}
        </Stack>
    );
}
