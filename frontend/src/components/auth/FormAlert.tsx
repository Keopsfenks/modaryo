import Alert, { type AlertColor } from "@mui/material/Alert";
import { ApiError } from "@/lib/api/client";

type FormAlertProps = {
    error?: ApiError | string | null;
    severity?: AlertColor;
    children?: React.ReactNode;
};

/** Formun üstündeki genel mesaj. ApiError ise sunucunun (Türkçe) başlığını gösterir. */
export default function FormAlert({ error, severity = "error", children }: FormAlertProps) {
    const message = error instanceof ApiError ? error.message : error;
    if (!message && !children) {
        return null;
    }

    return (
        <Alert severity={severity} sx={{ mb: 2 }}>
            {message}
            {children}
        </Alert>
    );
}
