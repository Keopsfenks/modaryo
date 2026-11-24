"use client";

import { useRef, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import LinearProgress from "@mui/material/LinearProgress";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { inventory, type OpeningCommitResult, type OpeningPreview } from "@/lib/api/inventory";
import { useSession } from "@/session/SessionProvider";

const MAX_BYTES = 5 * 1024 * 1024;
const MAX_ROWS_SHOWN = 200;

type Phase = "idle" | "downloading" | "previewing" | "committing";

/** Açılış stoğu Excel aktarımı: şablon, önizleme (satır hataları) ve hep-ya-hiç uygulama. */
export default function OpeningImport({ onDone }: { onDone?: (result: OpeningCommitResult) => void }) {
    const { hasPermission } = useSession();
    const input = useRef<HTMLInputElement>(null);
    const [phase, setPhase] = useState<Phase>("idle");
    const [file, setFile] = useState<File | null>(null);
    const [preview, setPreview] = useState<OpeningPreview | null>(null);
    const [result, setResult] = useState<OpeningCommitResult | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);

    if (!hasPermission("stok.manage")) return <Alert severity="warning">Açılış stoğu yüklemek için yetkiniz yok.</Alert>;

    const busy = phase !== "idle";

    function reset(message: string | null = null) {
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
        } else if (selected && selected.size > MAX_BYTES) {
            setError(errorMessage(new ApiError(413, "", "file_too_large")));
            setFile(null);
        } else {
            setFile(selected);
        }
    }

    async function template() {
        setPhase("downloading");
        setError(null);
        try {
            await inventory.openingTemplate();
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
        try {
            setPreview(await inventory.previewOpening(file));
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
        try {
            const outcome = await inventory.commitOpening(preview.batchId);
            if (outcome.kind === "conflict") {
                setPreview(outcome.preview);
                setNotice(errorMessage(new ApiError(409, "", "import_conflict")));
            } else {
                setResult(outcome.result);
                onDone?.(outcome.result);
            }
        } catch (e) {
            if (e instanceof ApiError && (e.code === "import_expired" || e.code === "import_has_errors")) reset(null);
            setError(errorMessage(e));
        } finally {
            setPhase("idle");
        }
    }

    if (result) {
        return (
            <Stack spacing={2}>
                <Alert severity="success">
                    Açılış stokları yüklendi: {result.documents} fiş, {result.lines} satır.
                </Alert>
                <div>
                    <Button
                        variant="outlined"
                        onClick={() => {
                            setResult(null);
                            reset();
                        }}
                    >
                        Yeni dosya yükle
                    </Button>
                </div>
            </Stack>
        );
    }

    const rows = preview ? [...preview.rows].sort((a, b) => Number(b.errors.length > 0) - Number(a.errors.length > 0) || a.rowNumber - b.rowNumber) : [];

    return (
        <Stack spacing={2}>
            {error && <Alert severity="error">{error}</Alert>}
            {notice && <Alert severity="warning">{notice}</Alert>}
            {!preview ? (
                <Stack spacing={2} sx={{ maxWidth: 560 }}>
                    <Typography color="text.secondary">
                        Şablonu indirip depo kodu, ürün kodu, miktar ve birim maliyetle doldurun. Her depo için tek açılış fişi, bugünün tarihiyle yazılır. En fazla 5.000 satır ve 5 MB.
                    </Typography>
                    <div>
                        <Button variant="outlined" disabled={busy} onClick={() => void template()}>
                            Şablonu indir
                        </Button>
                    </div>
                    <input
                        ref={input}
                        type="file"
                        accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                        aria-label="Excel dosyası"
                        disabled={busy}
                        onChange={(e) => pick(e.target.files?.[0] ?? null)}
                    />
                    {phase === "previewing" && <LinearProgress aria-label="İnceleniyor" />}
                    <div>
                        <Button variant="contained" disabled={!file || busy} onClick={() => void runPreview()}>
                            Önizle
                        </Button>
                    </div>
                </Stack>
            ) : (
                <Stack spacing={2}>
                    <Stack direction="row" spacing={1}>
                        <Chip label={`${preview.total} satır`} />
                        <Chip color="success" variant="outlined" label={`${preview.creates} yeni`} />
                        <Chip color={preview.errors > 0 ? "error" : "default"} variant="outlined" label={`${preview.errors} hatalı`} />
                    </Stack>
                    {preview.errors > 0 && <Alert severity="error">Hatalı satırlar varken içe aktarılamaz. Dosyayı düzeltip yeniden yükleyin; işlem ya hep ya hiç uygulanır.</Alert>}
                    <TableContainer sx={{ maxHeight: 360 }}>
                        <Table size="small" stickyHeader>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Satır</TableCell>
                                    <TableCell>Depo</TableCell>
                                    <TableCell>Ürün</TableCell>
                                    <TableCell>Hatalar</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {rows.slice(0, MAX_ROWS_SHOWN).map((r) => (
                                    <TableRow key={r.rowNumber} sx={r.errors.length > 0 ? { bgcolor: "error.light", "& td": { color: "error.contrastText" } } : undefined}>
                                        <TableCell>{r.rowNumber}</TableCell>
                                        <TableCell>{r.warehouseCode ?? "—"}</TableCell>
                                        <TableCell>{r.productCode ?? "—"}</TableCell>
                                        <TableCell>{r.errors.join(" ")}</TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                    {phase === "committing" && <LinearProgress aria-label="Yükleniyor" />}
                    <Stack direction="row" spacing={1}>
                        <Button variant="outlined" disabled={busy} onClick={() => reset()}>
                            Geri
                        </Button>
                        <Button variant="contained" disabled={busy || preview.errors > 0 || preview.total === 0} onClick={() => void commit()}>
                            İçe aktar
                        </Button>
                    </Stack>
                </Stack>
            )}
        </Stack>
    );
}
