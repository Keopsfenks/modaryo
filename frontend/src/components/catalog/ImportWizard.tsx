"use client";

import { useRef, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Chip from "@mui/material/Chip";
import FormControlLabel from "@mui/material/FormControlLabel";
import LinearProgress from "@mui/material/LinearProgress";
import Stack from "@mui/material/Stack";
import Step from "@mui/material/Step";
import StepLabel from "@mui/material/StepLabel";
import Stepper from "@mui/material/Stepper";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { catalog, type ImportCommitResult, type ImportPreview } from "@/lib/api/catalog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { useSession } from "@/session/SessionProvider";

const STEPS = ["Dosya", "Önizleme", "Sonuç"];
const MAX_BYTES = 5 * 1024 * 1024;
const MAX_ROWS_SHOWN = 300;

type Phase = "idle" | "downloading" | "previewing" | "committing";

const ACTION_LABELS: Record<string, string> = { create: "Yeni", update: "Güncelleme", error: "Hata" };

/** Excel içe aktarma sihirbazı: şablon/dosya, önizleme (satır hataları ve sayılar), uygulama sonucu. */
export default function ImportWizard() {
    const { hasPermission } = useSession();
    const input = useRef<HTMLInputElement>(null);

    const [step, setStep] = useState(0);
    const [phase, setPhase] = useState<Phase>("idle");
    const [file, setFile] = useState<File | null>(null);
    const [autoCode, setAutoCode] = useState(false);
    const [preview, setPreview] = useState<ImportPreview | null>(null);
    const [result, setResult] = useState<ImportCommitResult | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);

    if (!hasPermission("katalog.manage")) {
        return <Alert severity="warning">Ürünleri içe aktarmak için yetkiniz yok.</Alert>;
    }

    const busy = phase !== "idle";

    function restart(message: string | null = null) {
        setStep(0);
        setPreview(null);
        setFile(null);
        setNotice(message);
        if (input.current) input.current.value = "";
    }

    function pick(selected: File | null) {
        setError(null);
        setNotice(null);
        if (selected && !selected.name.toLowerCase().endsWith(".xlsx")) {
            setError("Yalnızca .xlsx dosyaları yüklenebilir.");
            setFile(null);
            return;
        }
        if (selected && selected.size > MAX_BYTES) {
            setError(errorMessage(new ApiError(413, "", "file_too_large")));
            setFile(null);
            return;
        }
        setFile(selected);
    }

    async function downloadTemplate() {
        setPhase("downloading");
        setError(null);
        try {
            await catalog.importTemplate();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setPhase("idle");
        }
    }

    async function runPreview() {
        if (!file) return;
        setPhase("previewing");
        setError(null);
        setNotice(null);
        try {
            setPreview(await catalog.previewImport(file, autoCode));
            setStep(1);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setPhase("idle");
        }
    }

    async function commit() {
        if (!preview) return;
        setPhase("committing");
        setError(null);
        setNotice(null);
        try {
            const outcome = await catalog.commitImport(preview.batchId);
            if (outcome.kind === "conflict") {
                setPreview(outcome.preview);
                setNotice(errorMessage(new ApiError(409, "", "import_conflict")));
            } else {
                setResult(outcome.result);
                setStep(2);
            }
        } catch (e) {
            if (e instanceof ApiError && (e.code === "import_expired" || e.code === "import_has_errors")) {
                restart(null);
                setError(errorMessage(e));
            } else {
                setError(errorMessage(e));
            }
        } finally {
            setPhase("idle");
        }
    }

    function skipErrorRows() {
        const rows = (preview?.rows ?? []).filter((r) => r.errors.length > 0).map((r) => r.rowNumber);
        restart(`Hatalı satırları (${rows.slice(0, 20).join(", ")}${rows.length > 20 ? ", ..." : ""}) dosyadan silip dosyayı yeniden yükleyin. İçe aktarma ya hep ya hiç uygulanır; hatalı satırlar atlanarak kısmen yazılmaz.`);
    }

    const sortedRows = preview ? [...preview.rows].sort((a, b) => Number(b.errors.length > 0) - Number(a.errors.length > 0) || a.rowNumber - b.rowNumber) : [];

    return (
        <DashboardCard>
            <Stepper activeStep={step} sx={{ mb: 3 }}>
                {STEPS.map((label) => (
                    <Step key={label}>
                        <StepLabel>{label}</StepLabel>
                    </Step>
                ))}
            </Stepper>

            {error && (
                <Alert severity="error" sx={{ mb: 2 }}>
                    {error}
                </Alert>
            )}
            {notice && (
                <Alert severity="warning" sx={{ mb: 2 }}>
                    {notice}
                </Alert>
            )}

            {step === 0 && (
                <Stack spacing={2} sx={{ maxWidth: 560 }}>
                    <Typography color="text.secondary">
                        Önce şablonu indirip doldurun. Kodu aynı olan kartlar güncellenir, olmayanlar oluşturulur. En fazla 5.000 satır ve 5 MB.
                    </Typography>
                    <div>
                        <Button variant="outlined" onClick={() => void downloadTemplate()} disabled={busy}>
                            Şablonu indir
                        </Button>
                    </div>
                    <div>
                        <input
                            ref={input}
                            type="file"
                            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                            aria-label="Excel dosyası"
                            disabled={busy}
                            onChange={(e) => pick(e.target.files?.[0] ?? null)}
                        />
                    </div>
                    <FormControlLabel control={<Checkbox checked={autoCode} onChange={(e) => setAutoCode(e.target.checked)} disabled={busy} />} label="Kodu boş satırlara otomatik kod ver" />
                    {phase === "previewing" && (
                        <Box role="status">
                            <Typography sx={{ mb: 1 }}>Dosya inceleniyor... Büyük dosyalarda bu işlem birkaç dakika sürebilir.</Typography>
                            <LinearProgress />
                        </Box>
                    )}
                    <div>
                        <Button variant="contained" disabled={!file || busy} onClick={() => void runPreview()}>
                            Önizle
                        </Button>
                    </div>
                </Stack>
            )}

            {step === 1 && preview && (
                <Stack spacing={2}>
                    <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap" }}>
                        <Chip label={`${preview.total} satır`} />
                        <Chip color="success" variant="outlined" label={`${preview.creates} yeni`} />
                        <Chip color="info" variant="outlined" label={`${preview.updates} güncelleme`} />
                        <Chip color={preview.errors > 0 ? "error" : "default"} variant="outlined" label={`${preview.errors} hatalı`} />
                    </Stack>
                    {preview.errors > 0 && (
                        <Alert
                            severity="error"
                            action={
                                <Button color="inherit" size="small" onClick={skipErrorRows}>
                                    Hatalı satırları atla
                                </Button>
                            }
                        >
                            Hatalı satırlar varken içe aktarılamaz. Satırları düzeltin veya dosyadan çıkarıp yeniden yükleyin.
                        </Alert>
                    )}
                    <TableContainer sx={{ maxHeight: 420 }}>
                        <Table size="small" stickyHeader>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Satır</TableCell>
                                    <TableCell>İşlem</TableCell>
                                    <TableCell>Kod</TableCell>
                                    <TableCell>Ad</TableCell>
                                    <TableCell>Hatalar</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {sortedRows.slice(0, MAX_ROWS_SHOWN).map((row) => (
                                    <TableRow key={row.rowNumber} sx={row.errors.length > 0 ? { bgcolor: "error.light", "& td": { color: "error.contrastText" } } : undefined}>
                                        <TableCell>{row.rowNumber}</TableCell>
                                        <TableCell>{row.errors.length > 0 ? ACTION_LABELS.error : (ACTION_LABELS[row.action] ?? row.action)}</TableCell>
                                        <TableCell>{row.code ?? "—"}</TableCell>
                                        <TableCell>{row.name ?? "—"}</TableCell>
                                        <TableCell>{row.errors.join(" ")}</TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                    {sortedRows.length > MAX_ROWS_SHOWN && (
                        <Typography variant="caption" color="text.secondary">
                            İlk {MAX_ROWS_SHOWN} satır gösteriliyor (hatalı satırlar önce).
                        </Typography>
                    )}
                    {phase === "committing" && (
                        <Box role="status">
                            <Typography sx={{ mb: 1 }}>İçe aktarılıyor... Lütfen sayfadan ayrılmayın.</Typography>
                            <LinearProgress />
                        </Box>
                    )}
                    <Stack direction="row" spacing={1}>
                        <Button variant="outlined" disabled={busy} onClick={() => restart()}>
                            Geri
                        </Button>
                        <Button variant="contained" disabled={busy || preview.errors > 0 || preview.total === 0} onClick={() => void commit()}>
                            İçe aktar
                        </Button>
                    </Stack>
                </Stack>
            )}

            {step === 2 && result && (
                <Stack spacing={2}>
                    <Alert severity="success">
                        İçe aktarma tamamlandı: {result.created} kart oluşturuldu, {result.updated} kart güncellendi.
                    </Alert>
                    <Stack direction="row" spacing={1}>
                        <Button component={Link} href="/stok/urunler" variant="contained">
                            Ürünlere git
                        </Button>
                        <Button
                            variant="outlined"
                            onClick={() => {
                                setResult(null);
                                restart();
                            }}
                        >
                            Yeni dosya yükle
                        </Button>
                    </Stack>
                </Stack>
            )}
        </DashboardCard>
    );
}
