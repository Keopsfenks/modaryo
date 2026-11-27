"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import CircularProgress from "@mui/material/CircularProgress";
import Dialog from "@mui/material/Dialog";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import PdfPreview from "@/components/invoicing/PdfPreview";
import TotalsPanel from "@/components/invoicing/TotalsPanel";
import ReasonDialog from "@/components/purchasing/ReasonDialog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { QUOTE_STATUS_LABELS, sales, type QuoteDto, type QuoteInput, type QuoteStatus } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";
import { useSession } from "@/session/SessionProvider";
import RevisionSwitcher from "./RevisionSwitcher";
import SalesLines from "./SalesLines";
import { SalesHeaderFields, useSalesDoc, type SalesDocInitial } from "./salesDoc";
import { hydrateLines, lineFromDto } from "./salesModel";

export const QUOTE_BASE = "/satis/teklifler";

export const QUOTE_STATUS_COLOR: Record<QuoteStatus, "default" | "info" | "success" | "error" | "warning"> = {
    draft: "default",
    sent: "info",
    accepted: "success",
    rejected: "error",
    expired: "warning",
};

function initialOf(q: QuoteDto): SalesDocInitial {
    return {
        customer: { id: q.currentAccountId, name: q.customerName },
        branchId: q.branchId,
        date: q.date,
        currency: q.currency,
        salesRepUserId: q.salesRepUserId,
        docDiscount: q.docDiscount,
        notes: q.notes,
        lines: q.lines.map((l) => lineFromDto(l)),
    };
}

type EditorProps = { id?: string };

/** Teklif sayfası: yeni teklif, taslak/gönderilmiş teklifi düzenleme (gönderilmişte yeni revizyon) ya da salt okunur görünüm. */
export default function QuoteEditor({ id }: EditorProps) {
    const { hasPermission } = useSession();
    const allowed = hasPermission("satis.read");
    const [quote, setQuote] = useState<QuoteDto | null>(null);
    const [revision, setRevision] = useState<number | undefined>(undefined);
    const [error, setError] = useState<ApiError | null>(null);
    const [version, setVersion] = useState(0);

    useEffect(() => {
        if (!id || !allowed) return;
        let cancelled = false;
        sales.quotes.get(id, revision).then(
            (dto) => !cancelled && setQuote(dto),
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [id, allowed, revision, version]);

    if (!allowed) return <Alert severity="warning">Teklifi görüntülemek için yetkiniz yok.</Alert>;
    if (error) return <FormAlert error={error} />;

    const canManage = hasPermission("satis.manage");
    if (!id) return canManage ? <QuoteForm initial={null} quote={null} onChanged={() => undefined} /> : <Alert severity="warning">Teklif girmek için satış yönetimi yetkisi gerekir.</Alert>;
    if (!quote) {
        return (
            <Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
                <CircularProgress />
            </Box>
        );
    }

    const viewingOld = quote.revision !== quote.currentRevision;
    const editable = canManage && !viewingOld && (quote.status === "draft" || quote.status === "sent");
    return (
        <Stack spacing={2}>
            <RevisionSwitcher revisions={quote.revisions} current={quote.currentRevision} selected={quote.revision} currency={quote.currency} onSelect={(r) => setRevision(r === quote.currentRevision ? undefined : r)} />
            {viewingOld && <Alert severity="info">Rev. {quote.revision} salt okunurdur; düzenlemek için güncel revizyona dönün.</Alert>}
            <QuoteForm key={`${quote.id}-${quote.version}-${quote.revision}`} initial={initialOf(quote)} quote={quote} readOnly={!editable} onChanged={() => setVersion((v) => v + 1)} />
        </Stack>
    );
}

type FormProps = { initial: SalesDocInitial | null; quote: QuoteDto | null; readOnly?: boolean; onChanged(): void };

function QuoteForm({ initial, quote, readOnly, onChanged }: FormProps) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const doc = useSalesDoc(initial);
    const [validUntil, setValidUntil] = useState(quote?.validUntil ?? "");
    const [error, setError] = useState<ApiError | string | null>(null);
    const [saved, setSaved] = useState(false);
    const [busy, setBusy] = useState(false);
    const [dialog, setDialog] = useState<"reject" | "pdf" | null>(null);
    const canManage = hasPermission("satis.manage");

    // Kayıtlı teklifte birim listesi ve ölçü bilgisi katalogdan tamamlanır.
    useEffect(() => {
        if (!initial) return;
        let cancelled = false;
        hydrateLines(initial.lines).then((lines) => {
            if (!cancelled) doc.setLines(lines);
        });
        return () => {
            cancelled = true;
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    function fail(message: string): null {
        setError(message);
        return null;
    }

    async function save(): Promise<QuoteDto | null> {
        setError(null);
        setSaved(false);
        const problem = doc.validate();
        if (problem) return fail(problem);
        const input: QuoteInput = { ...doc.body(), validUntil: validUntil || null, version: quote?.version ?? null };
        setBusy(true);
        try {
            const dto = quote ? await sales.quotes.update(quote.id, input) : await sales.quotes.create(input);
            setSaved(true);
            if (!quote) router.replace(`${QUOTE_BASE}/${dto.id}`);
            else onChanged();
            return dto;
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(Object.keys(e.fieldErrors).length === 0 ? errorMessage(e) : e);
            return null;
        } finally {
            setBusy(false);
        }
    }

    async function run(action: () => Promise<void>) {
        setBusy(true);
        setError(null);
        try {
            await action();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const sent = quote?.status === "sent";
    const title = quote ? `Teklif ${quote.number}` : "Yeni teklif";

    return (
        <>
            <PageHeader title={title} subtitle={quote ? `Revizyon ${quote.revision} · ${formatDate(quote.date)}` : "Taslak olarak kaydedilir; müşteriye gönderilince yayınlanır"} />
            <Stack spacing={3}>
                <FormAlert error={error} />
                {saved && <Alert severity="success">Teklif kaydedildi.</Alert>}
                {quote && (
                    <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1 }}>
                        <Chip color={QUOTE_STATUS_COLOR[quote.status]} label={QUOTE_STATUS_LABELS[quote.status]} />
                        {quote.validUntil && <Chip variant="outlined" label={`Geçerlilik: ${formatDate(quote.validUntil)}`} />}
                        {quote.rejectReason && <Chip variant="outlined" color="error" label={`Red gerekçesi: ${quote.rejectReason}`} />}
                        {quote.salesOrderId && (
                            <Button component={Link} href={`/satis/siparisler/${quote.salesOrderId}`} size="small">
                                Siparişi aç
                            </Button>
                        )}
                        <Box sx={{ flexGrow: 1 }} />
                        <Button variant="outlined" onClick={() => setDialog("pdf")}>
                            PDF
                        </Button>
                        {canManage && quote.status === "draft" && !readOnly && (
                            <Button variant="outlined" disabled={busy} onClick={() => void run(async () => { await sales.quotes.send(quote.id); onChanged(); })}>
                                Gönder
                            </Button>
                        )}
                        {canManage && sent && !readOnly && (
                            <>
                                <Button
                                    variant="contained"
                                    color="success"
                                    disabled={busy}
                                    onClick={() =>
                                        void run(async () => {
                                            const result = await sales.quotes.accept(quote.id);
                                            router.push(`/satis/siparisler/${result.orderId}`);
                                        })
                                    }
                                >
                                    Kabul et (siparişe çevir)
                                </Button>
                                <Button color="error" onClick={() => setDialog("reject")}>
                                    Reddet
                                </Button>
                            </>
                        )}
                    </Stack>
                )}
                {sent && !readOnly && (
                    <Alert severity="info">
                        Gönderilmiş teklifi kaydederseniz yeni revizyon (Rev. {quote.currentRevision + 1}) açılır; mevcut revizyon salt okunur kalır ve teklif taslağa döner.
                    </Alert>
                )}

                <DashboardCard title="Başlık">
                    <SalesHeaderFields doc={doc} disabled={readOnly}>
                        <TextField size="small" type="date" label="Geçerlilik" value={validUntil} disabled={readOnly} onChange={(e) => setValidUntil(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                    </SalesHeaderFields>
                </DashboardCard>

                <DashboardCard title="Satırlar">
                    <SalesLines lines={doc.lines} onChange={doc.setLines} currentAccountId={doc.customer?.id ?? ""} date={doc.date} currency={doc.currency} disabled={readOnly} />
                </DashboardCard>

                <DashboardCard>
                    <Stack direction={{ xs: "column", md: "row" }} spacing={3} sx={{ justifyContent: "space-between" }}>
                        <Box sx={{ minWidth: 320, maxWidth: 420, flexGrow: 1 }}>
                            {readOnly && quote ? (
                                <TotalsPanel
                                    input={null}
                                    currency={quote.currency}
                                    fixed={{
                                        totals: { gross: quote.gross, discount: quote.discount, net: quote.net, otv: 0, vat: quote.vat, withheld: 0, payable: quote.total },
                                        totalsTl: { gross: quote.gross, discount: quote.discount, net: quote.net, otv: 0, vat: quote.vat, withheld: 0, payable: quote.total },
                                        vatSummary: [],
                                    }}
                                />
                            ) : (
                                <TotalsPanel input={doc.previewInput} currency={doc.currency} />
                            )}
                        </Box>
                        {!readOnly && (
                            <Stack direction="row" spacing={1} sx={{ alignItems: "flex-end", flexWrap: "wrap", gap: 1 }}>
                                <Button variant="contained" disabled={busy} onClick={() => void save()}>
                                    {busy ? "Kaydediliyor..." : sent ? "Yeni revizyon olarak kaydet" : "Kaydet"}
                                </Button>
                            </Stack>
                        )}
                    </Stack>
                </DashboardCard>
            </Stack>

            {quote && (
                <>
                    <Dialog open={dialog === "pdf"} onClose={() => setDialog(null)} fullWidth maxWidth="lg">
                        <DialogTitle>Teklif PDF</DialogTitle>
                        <DialogContent>{dialog === "pdf" && <PdfPreview title="Teklif PDF" load={() => sales.quotes.pdf(quote.id, quote.revision)} />}</DialogContent>
                    </Dialog>
                    <ReasonDialog
                        open={dialog === "reject"}
                        title="Teklifi reddet"
                        text="Teklifin neden reddedildiğini yazın."
                        confirmLabel="Reddet"
                        onClose={() => setDialog(null)}
                        onConfirm={async (reason) => {
                            await sales.quotes.reject(quote.id, reason);
                            setDialog(null);
                            onChanged();
                        }}
                    />
                </>
            )}
        </>
    );
}
