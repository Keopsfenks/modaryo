"use client";

import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { MODE_LABELS, type EBelgeSettings, type InvoiceMode, type SeriesDto, type SeriesGroup } from "@/lib/api/invoicing";

type IssueDialogProps = {
    open: boolean;
    onClose(): void;
    onConfirm(): void;
    busy?: boolean;
    kindGroup: SeriesGroup;
    branchId: string;
    /** Fatura tarihinin yılı. */
    year: number;
    /** Carinin e-Fatura mükellefi işareti. */
    taxpayer: boolean;
    /** e-Belge ayarı; okunamadıysa (yetki yok) null: mod kesim sırasında belirlenir. */
    ebelge: EBelgeSettings | null;
    series: SeriesDto[];
};

/** Kesimde sunucunun uygulayacağı kuralın aynısı: kapalı → kâğıt, açık → mükellefse e-Fatura, değilse e-Arşiv. */
export function predictMode(ebelge: EBelgeSettings | null, taxpayer: boolean): InvoiceMode | null {
    if (!ebelge) return null;
    if (!ebelge.enabled) return "paper";
    return taxpayer ? "eInvoice" : "eArchive";
}

/** Kesim onayı: numara serisi, belge modu (Kâğıt / e-Fatura / e-Arşiv) ve geri alınamaz uyarısı. */
export default function IssueDialog({ open, onClose, onConfirm, busy, kindGroup, branchId, year, taxpayer, ebelge, series }: IssueDialogProps) {
    const mode = predictMode(ebelge, taxpayer);
    const candidates = series.filter((s) => s.isActive && s.kindGroup === kindGroup && s.branchId === branchId && (mode === null || s.mode === mode));
    const chosen = candidates.find((s) => s.isDefault && s.year === year) ?? candidates.find((s) => s.year === year) ?? null;

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
            <DialogTitle>Fatura kesilsin mi?</DialogTitle>
            <DialogContent>
                <Stack spacing={1.5}>
                    <Typography>
                        Belge modu: <strong>{mode ? MODE_LABELS[mode] : "Kesim sırasında belirlenir"}</strong>
                    </Typography>
                    {mode === "eArchive" && <Typography variant="body2" color="text.secondary">Alıcı e-Fatura mükellefi olmadığı için e-Arşiv olarak kesilir.</Typography>}
                    {mode && (
                        chosen ? (
                            <Typography>
                                Seri: <strong>{chosen.prefix}</strong> · sıradaki numara <strong>{chosen.nextPreview}</strong>
                            </Typography>
                        ) : (
                            <Alert severity="warning">Bu şube ve mod için {year} yılına ait varsayılan seri bulunamadı; kesim seri eksikliğiyle reddedilebilir.</Alert>
                        )
                    )}
                    <Alert severity="info">Kesilen faturanın satırları kilitlenir; cari hesaba ve (seçiliyse) stoğa işlenir. Değişiklik için iptal gerekir.</Alert>
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" onClick={onConfirm} disabled={busy}>
                    {busy ? "Kesiliyor..." : "Faturayı kes"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
