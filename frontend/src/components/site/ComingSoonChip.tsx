import Chip from "@mui/material/Chip";

/** Henüz yayında olmayan modüller için küçük "Yakında" etiketi. */
export default function ComingSoonChip() {
    return <Chip size="small" label="Yakında" variant="outlined" sx={{ height: 20, fontSize: "0.6875rem", fontWeight: 600 }} />;
}
