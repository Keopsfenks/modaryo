import Box from "@mui/material/Box";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { IconCircleCheck, IconCircleDashed, IconCircleDot } from "@tabler/icons-react";
import type { TimelineStep } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";

/**
 * Siparişe göre tedarik satırının izlenmesi: Tedarikçiye sipariş verildi, Kargoda (takip/irsaliye notu),
 * Geldi, Müşteriye teslim edildi.
 */
export default function LineTimeline({ steps }: { steps: TimelineStep[] }) {
    if (steps.length === 0) return null;
    return (
        <Stack component="ol" direction="row" spacing={2} sx={{ listStyle: "none", m: 0, p: 0, flexWrap: "wrap", rowGap: 1 }} aria-label="Tedarik zaman çizgisi">
            {steps.map((s) => {
                const color = s.state === "done" ? "success.main" : s.state === "current" ? "warning.main" : "text.disabled";
                const Icon = s.state === "done" ? IconCircleCheck : s.state === "current" ? IconCircleDot : IconCircleDashed;
                return (
                    <Box component="li" key={s.key} sx={{ display: "flex", gap: 0.75, alignItems: "flex-start", minWidth: 150 }} data-state={s.state}>
                        <Box sx={{ color, mt: 0.25, display: "flex" }}>
                            <Icon size={18} />
                        </Box>
                        <Box>
                            <Typography variant="body2" sx={{ fontWeight: s.state === "current" ? 700 : 400, color: s.state === "pending" ? "text.secondary" : "text.primary" }}>
                                {s.label}
                            </Typography>
                            {s.at && (
                                <Typography variant="caption" color="text.secondary" component="div">
                                    {formatDate(s.at)}
                                </Typography>
                            )}
                            {s.note && (
                                <Typography variant="caption" color="text.secondary" component="div">
                                    {s.note}
                                </Typography>
                            )}
                        </Box>
                    </Box>
                );
            })}
        </Stack>
    );
}
