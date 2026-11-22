import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import { IconArrowDownRight, IconArrowUpLeft } from "@tabler/icons-react";

export default function TrendBadge({ percent, caption }: { percent: number; caption: string }) {
    const up = percent >= 0;
    const color = up ? "success" : "error";
    return (
        <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
            <Box
                sx={{
                    width: 21,
                    height: 21,
                    borderRadius: "50%",
                    bgcolor: `${color}.light`,
                    color: `${color}.main`,
                    display: "grid",
                    placeItems: "center",
                }}
            >
                {up ? <IconArrowUpLeft size={16} /> : <IconArrowDownRight size={16} />}
            </Box>
            <Typography variant="subtitle2" color="text.primary">
                {up ? "+" : ""}
                {percent}%
            </Typography>
            <Typography variant="subtitle2" color="text.secondary">
                {caption}
            </Typography>
        </Box>
    );
}
