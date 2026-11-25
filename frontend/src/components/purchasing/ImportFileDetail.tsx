"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Autocomplete from "@mui/material/Autocomplete";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Grid from "@mui/material/Grid";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { useDebounced } from "@/components/pricing/useDebounced";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { erp } from "@/lib/api/erp";
import { formatQuantity } from "@/lib/api/inventory";
import { invoicing } from "@/lib/api/invoicing";
import { COST_TYPE_LABELS, IMPORT_LINK_LABELS, purchasing, type CostType, type ImportFileDto, type ImportLinkDto, type ImportLinkKind, type ImportSummary } from "@/lib/api/purchasing";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import { IMPORT_STATUS_LABELS, ImportFileDialog, ModuleOff, incotermText } from "./ImportFiles";

const KIND_ORDER: ImportLinkKind[] = ["purchaseOrder", "goodsReceipt", "purchaseInvoice", "landedCost", "attachment"];

function linkHref(l: ImportLinkDto): string {
    switch (l.kind) {
        case "purchaseOrder":
            return `/satin-alma/siparisler/${l.refId}`;
        case "goodsReceipt":
            return "/satin-alma/mal-kabul";
        case "purchaseInvoice":
            return `/satin-alma/faturalar/${l.refId}`;
        case "landedCost":
            return `/satin-alma/masraf-dagitimi/${l.refId}`;
        default:
            return erp.files.url(l.refId);
    }
}

const costLabel = (type: string) => COST_TYPE_LABELS[type as CostType] ?? type;

/** İthalat dosyası ayrıntısı: başlık (Incoterm, menşei, para birimi), bağlı belgeler zaman çizelgesi, ekler ve maliyet özeti. */
export default function ImportFileDetail({ id }: { id: string }) {
    const { hasPermission, hasModule } = useSession();
    const enabled = hasModule("ithalat") && hasPermission("satinalma.read");
    const showCosts = hasPermission("fiyat.alis");
    const canManage = hasPermission("satinalma.manage");
    const [file, setFile] = useState<ImportFileDto | null>(null);
    const [summary, setSummary] = useState<ImportSummary | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [version, setVersion] = useState(0);
    const [dialog, setDialog] = useState<"edit" | "link" | "close" | null>(null);

    useEffect(() => {
        if (!enabled) return;
        let cancelled = false;
        purchasing.importFiles.get(id).then(
            (dto) => !cancelled && setFile(dto),
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(errorMessage(e));
            },
        );
        if (showCosts) {
            purchasing.importFiles.summary(id).then(
                (s) => !cancelled && setSummary(s),
                () => !cancelled && setSummary(null),
            );
        }
        return () => {
            cancelled = true;
        };
    }, [id, enabled, showCosts, version]);

    if (!hasModule("ithalat")) return <ModuleOff />;
    if (!hasPermission("satinalma.read")) return <Alert severity="warning">İthalat dosyalarını görüntülemek için yetkiniz yok.</Alert>;
    if (!file) return error ? <FormAlert error={error} /> : <Skeleton variant="rounded" height={240} />;

    const open = file.status === "open";
    const links = [...file.links].sort((a, b) => KIND_ORDER.indexOf(a.kind) - KIND_ORDER.indexOf(b.kind));
    const reload = () => setVersion((v) => v + 1);

    return (
        <>
            <PageHeader title={file.number} subtitle={`İthalat dosyası · ${file.supplierName}`} />
            <Stack spacing={3}>
                <FormAlert error={error} />
                <DashboardCard
                    title="Dosya bilgileri"
                    action={<Chip size="small" label={IMPORT_STATUS_LABELS[file.status]} color={open ? "primary" : "default"} />}
                >
                    <Grid container spacing={2}>
                        {[
                            ["Tedarikçi", file.supplierName],
                            ["Menşei ülke", file.originCountry],
                            ["Incoterm", incotermText(file.incoterm)],
                            ["Para birimi", file.currency],
                        ].map(([label, value]) => (
                            <Grid key={label} size={{ xs: 6, md: 3 }}>
                                <Typography variant="caption" color="text.secondary">
                                    {label}
                                </Typography>
                                <Typography>{value}</Typography>
                            </Grid>
                        ))}
                        {file.notes && (
                            <Grid size={12}>
                                <Typography variant="caption" color="text.secondary">
                                    Not
                                </Typography>
                                <Typography>{file.notes}</Typography>
                            </Grid>
                        )}
                    </Grid>
                    {canManage && (
                        <Stack direction="row" spacing={1} sx={{ mt: 2 }}>
                            {open && (
                                <Button variant="outlined" onClick={() => setDialog("edit")}>
                                    Düzenle
                                </Button>
                            )}
                            {open && (
                                <Button variant="outlined" onClick={() => setDialog("link")}>
                                    Belge bağla
                                </Button>
                            )}
                            {open && showCosts && (
                                <Button component={Link} variant="outlined" href={`/satin-alma/masraf-dagitimi/yeni?importFile=${file.id}`}>
                                    Masraf dağıt
                                </Button>
                            )}
                            {open && (
                                <Button color="warning" onClick={() => setDialog("close")}>
                                    Dosyayı kapat
                                </Button>
                            )}
                        </Stack>
                    )}
                </DashboardCard>

                <DashboardCard title="Bağlı belgeler" subtitle="Sipariş, mal kabul, fatura, masraf dağıtımı ve ekler">
                    {links.length === 0 ? (
                        <Typography color="text.secondary">Henüz belge bağlanmamış.</Typography>
                    ) : (
                        <Stack component="ol" spacing={1.5} sx={{ listStyle: "none", m: 0, p: 0, borderLeft: 2, borderColor: "divider", pl: 2 }}>
                            {links.map((l) => (
                                <Box component="li" key={l.id} sx={{ position: "relative" }}>
                                    <Box sx={{ position: "absolute", left: -21, top: 8, width: 10, height: 10, borderRadius: "50%", bgcolor: "primary.main" }} />
                                    <Typography variant="caption" color="text.secondary">
                                        {IMPORT_LINK_LABELS[l.kind]}
                                    </Typography>
                                    <Typography
                                        component={Link}
                                        href={linkHref(l)}
                                        target={l.kind === "attachment" ? "_blank" : undefined}
                                        sx={{ display: "block", color: "primary.main" }}
                                    >
                                        {l.label ?? l.refId}
                                    </Typography>
                                </Box>
                            ))}
                        </Stack>
                    )}
                </DashboardCard>

                {showCosts && summary && (
                    <DashboardCard title="Maliyet özeti">
                        <Stack spacing={0.5} sx={{ mb: 2 }}>
                            <Typography>Mal değeri: {formatMoney(summary.goodsValueTl, "TRY")}</Typography>
                            {summary.costsByType.map((c) => (
                                <Typography key={c.costType}>
                                    {costLabel(c.costType)}: {formatMoney(c.amountTl, "TRY")}
                                </Typography>
                            ))}
                            <Typography>Toplam masraf: {formatMoney(summary.totalCostTl, "TRY")}</Typography>
                            <Typography variant="subtitle1">Toplam maliyet: {formatMoney(summary.landedTotalTl, "TRY")}</Typography>
                        </Stack>
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 560 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kalem</TableCell>
                                        <TableCell align="right">Miktar</TableCell>
                                        <TableCell align="right">Mal değeri</TableCell>
                                        <TableCell align="right">Dağıtılan masraf</TableCell>
                                        <TableCell align="right">Birim maliyet</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {summary.items.map((i) => (
                                        <TableRow key={`${i.productId}-${i.variantId ?? ""}`}>
                                            <TableCell>
                                                {i.productCode} · {i.productName}
                                            </TableCell>
                                            <TableCell align="right">{formatQuantity(i.baseQuantity)}</TableCell>
                                            <TableCell align="right">{formatMoney(i.goodsValueTl, "TRY")}</TableCell>
                                            <TableCell align="right">{formatMoney(i.allocatedCostTl, "TRY")}</TableCell>
                                            <TableCell align="right">{formatMoney(i.landedUnitCostTl, "TRY")}</TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    </DashboardCard>
                )}
            </Stack>

            {dialog === "edit" && (
                <ImportFileDialog
                    file={file}
                    onClose={() => setDialog(null)}
                    onSaved={(f) => {
                        setFile(f);
                        setDialog(null);
                    }}
                />
            )}
            {dialog === "link" && (
                <LinkDialog
                    fileId={file.id}
                    canLanded={showCosts}
                    onClose={() => setDialog(null)}
                    onLinked={(f) => {
                        setFile(f);
                        setDialog(null);
                        reload();
                    }}
                />
            )}
            {dialog === "close" && (
                <Dialog open onClose={() => setDialog(null)}>
                    <DialogTitle>Dosya kapatılsın mı?</DialogTitle>
                    <DialogContent>
                        <DialogContentText>Kapalı dosyaya belge bağlanamaz ve masraf dağıtılamaz.</DialogContentText>
                    </DialogContent>
                    <DialogActions>
                        <Button onClick={() => setDialog(null)}>Vazgeç</Button>
                        <Button
                            color="warning"
                            onClick={() =>
                                void purchasing.importFiles.close(file.id).then(
                                    (f) => {
                                        setFile(f);
                                        setDialog(null);
                                    },
                                    (e: unknown) => {
                                        if (!(e instanceof ApiError)) throw e;
                                        setError(errorMessage(e));
                                        setDialog(null);
                                    },
                                )
                            }
                        >
                            Kapat
                        </Button>
                    </DialogActions>
                </Dialog>
            )}
        </>
    );
}

type LinkOption = { id: string; label: string };
type LinkKindChoice = "po" | "receipt" | "invoice" | "landedCost" | "attachment";

const LINK_CHOICES: { value: LinkKindChoice; label: string }[] = [
    { value: "po", label: "Sipariş" },
    { value: "receipt", label: "Mal kabul" },
    { value: "invoice", label: "Alış faturası" },
    { value: "landedCost", label: "Masraf dağıtımı" },
    { value: "attachment", label: "Ek dosya" },
];

function LinkDialog({ fileId, canLanded, onClose, onLinked }: { fileId: string; canLanded: boolean; onClose(): void; onLinked(f: ImportFileDto): void }) {
    const [kind, setKind] = useState<LinkKindChoice>("po");
    const [picked, setPicked] = useState<LinkOption | null>(null);
    const [upload, setUpload] = useState<File | null>(null);
    const [input, setInput] = useState("");
    const search = useDebounced(input, 250);
    const [options, setOptions] = useState<LinkOption[]>([]);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (kind === "attachment") return;
        let cancelled = false;
        const done = (items: LinkOption[]) => !cancelled && setOptions(items);
        const fail = () => !cancelled && setOptions([]);
        const q = search || undefined;
        if (kind === "po") purchasing.orders.list({ search: q, pageSize: 20 }).then((r) => done(r.items.map((o) => ({ id: o.id, label: `${o.number} · ${o.supplierName}` }))), fail);
        else if (kind === "receipt") purchasing.receipts.list({ search: q, status: "posted", pageSize: 20 }).then((r) => done(r.items.map((o) => ({ id: o.id, label: `${o.number} · ${o.supplierName}` }))), fail);
        else if (kind === "invoice")
            invoicing.list({ kind: "purchase", status: "issued", search: q, pageSize: 20 }).then((r) => done(r.items.map((o) => ({ id: o.id, label: `${o.number ?? o.externalNumber ?? "Fatura"} · ${o.currentAccountName}` }))), fail);
        else purchasing.landedCosts.list({}).then((r) => done(r.filter((o) => !q || o.number.includes(q)).map((o) => ({ id: o.id, label: o.number }))), fail);
        return () => {
            cancelled = true;
        };
    }, [kind, search]);

    async function submit() {
        setBusy(true);
        setError(null);
        try {
            if (kind === "attachment") {
                if (!upload) return setError("Bir dosya seçin.");
                const uploaded = await erp.files.upload(upload, "attachment");
                onLinked(await purchasing.importFiles.link(fileId, "attachment", uploaded.id));
            } else {
                if (!picked) return setError("Bağlanacak belgeyi seçin.");
                onLinked(await purchasing.importFiles.link(fileId, kind, picked.id));
            }
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>Belge bağla</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    <TextField
                        select
                        size="small"
                        label="Belge türü"
                        value={kind}
                        onChange={(e) => {
                            setKind(e.target.value as LinkKindChoice);
                            setPicked(null);
                            setOptions([]);
                        }}
                        slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
                    >
                        {LINK_CHOICES.filter((c) => c.value !== "landedCost" || canLanded).map((c) => (
                            <option key={c.value} value={c.value}>
                                {c.label}
                            </option>
                        ))}
                    </TextField>
                    {kind === "attachment" ? (
                        <Button component="label" variant="outlined">
                            {upload ? upload.name : "Dosya seç"}
                            <input hidden type="file" aria-label="Ek dosya" onChange={(e) => setUpload(e.target.files?.[0] ?? null)} />
                        </Button>
                    ) : (
                        <Autocomplete
                            size="small"
                            options={options}
                            value={picked}
                            filterOptions={(x) => x}
                            getOptionLabel={(o) => o.label}
                            isOptionEqualToValue={(a, b) => a.id === b.id}
                            onInputChange={(_, v) => setInput(v)}
                            onChange={(_, next) => setPicked(next)}
                            renderInput={(params) => <TextField {...params} label="Belge" placeholder="Numara ya da tedarikçi ara" />}
                        />
                    )}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                    {busy ? "Bağlanıyor..." : "Bağla"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
