import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import { IconLock } from "@tabler/icons-react";

export default function NoAccess() {
    return (
        <Box role="alert" sx={{ py: 8, textAlign: "center", color: "text.secondary" }}>
            <IconLock size={40} stroke={1.5} />
            <Typography variant="h6" sx={{ mt: 1 }}>
                Bu sayfayı görüntüleme yetkiniz yok.
            </Typography>
            <Typography variant="body2">Gerekli izin için firma sahibiyle iletişime geçin.</Typography>
        </Box>
    );
}
