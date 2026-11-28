"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { IconArrowDown, IconArrowUp, IconGripVertical } from "@tabler/icons-react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import IconButton from "@mui/material/IconButton";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import PdfPreview from "@/components/invoicing/PdfPreview";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type MemberDto } from "@/lib/api/identity";
import { sales, type ShipmentDto } from "@/lib/api/sales";
import { OWN_VEHICLE, PLAN_STATUS_LABELS, STOP_STATUS_LABELS, shipping, type PlanDto, type PlanStopDto, type StopStatus } from "@/lib/api/shipping";
import { formatDate } from "@/lib/date";
import { useSession } from "@/session/SessionProvider";
import DeliveryDialog from "./DeliveryDialog";
import FailDialog from "./FailDialog";

const STOP_COLOR: Record<StopStatus, "default" | "success" | "error"> = { pending: "default", delivered: "success", failed: "error" };

const pad = (n: number) => String(n).padStart(2, "0");
const today = () => {
    const d = new Date();
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
};

type Row = { id: string; number: string; title: string; subtitle?: string; status?: StopStatus; actions?: React.ReactNode };

/** Sürükle-bırak ve ↑/↓ düğmeleriyle sıralanabilen liste. */
function Reorderable({ rows, disabled, onMove }: { rows: Row[]; disabled?: boolean; onMove(from: number, to: number): void }) {
    const [dragging, setDragging] = useState<number | null>(null);
    return (
        <List disablePadding>
            {rows.map((r, i) => (
                <ListItem
                    key={r.id}
                    divider
                    draggable={!disabled}
                    onDragStart={() => setDragging(i)}
                    onDragOver={(e) => !disabled && e.preventDefault()}
                    onDrop={(e) => {
                        e.preventDefault();
                        if (!disabled && dragging !== null && dragging !== i) onMove(dragging, i);
                        setDragging(null);
                    }}
                    sx={{ gap: 1, alignItems: "center", flexWrap: "wrap" }}
                >
                    <IconGripVertical size={18} aria-hidden />
                    <Typography sx={{ width: 24 }} color="text.secondary">
                        {i + 1}
                    </Typography>
                    <Box sx={{ flexGrow: 1, minWidth: 160 }}>
                        <Typography variant="subtitle2">{r.number}</Typography>
                        <Typography variant="body2" color="text.secondary">
                            {r.title}
                            {r.subtitle ? ` · ${r.subtitle}` : ""}
                        </Typography>
                    </Box>
                    {r.status && <Chip size="small" color={STOP_COLOR[r.status]} label={STOP_STATUS_LABELS[r.status]} />}
                    {r.actions}
                    <IconButton size="small" aria-label={`${r.number} yukarı taşı`} disabled={disabled || i === 0} onClick={() => onMove(i, i - 1)}>
                        <IconArrowUp size={18} />
                    </IconButton>
                    <IconButton size="small" aria-label={`${r.number} aşağı taşı`} disabled={disabled || i === rows.length - 1} onClick={() => onMove(i, i + 1)}>
                        <IconArrowDown size={18} />
                    </IconButton>
                </ListItem>
            ))}
        </List>
    );
}

const move = <T,>(items: T[], from: number, to: number): T[] => {
    const next = [...items];
    const [item] = next.splice(from, 1);
    next.splice(to, 0, item);
    return next;
};

/**
 * Teslimat planı: yeni planda tarih, araç, sürücü seçilir ve yalnızca "Kendi aracımız" ile sevk edilmiş sevkler eklenir;
 * mevcut planda duraklar sürükle-bırak ya da ↑/↓ ile yeniden sıralanır, her durak teslim edildi / edilemedi olarak işaretlenir, rota föyü yazdırılır.
 */
export default function PlanEditor({ planId }: { planId?: string }) {
    const router = useRouter();
    const { hasPermission, hasModule } = useSession();
    const allowed = hasPermission("sevkiyat.read") && hasModule("teslimat-plani");
    const canPlan = hasPermission("sevkiyat.plan");
    const canDeliver = hasPermission("sevkiyat.deliver");
    const [plan, setPlan] = useState<PlanDto | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);
    const [stopDialog, setStopDialog] = useState<{ stop: PlanStopDto; kind: "deliver" | "fail" } | null>(null);
    const [pdfOpen, setPdfOpen] = useState(false);
    // Yeni plan
    const [candidates, setCandidates] = useState<ShipmentDto[] | null>(null);
    const [picked, setPicked] = useState<string[]>([]);
    const [date, setDate] = useState(today);
    const [vehicle, setVehicle] = useState("");
    const [driverUserId, setDriverUserId] = useState("");
    const [driverName, setDriverName] = useState("");
    const [members, setMembers] = useState<MemberDto[]>([]);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        if (planId) {
            shipping.plans.get(planId).then(
                (p) => !cancelled && setPlan(p),
                (e: unknown) => {
                    if (cancelled) return;
                    if (!(e instanceof ApiError)) throw e;
                    setError(e);
                },
            );
        } else {
            sales.shipments
                .list({ status: "shipped", pageSize: 200 })
                .then(async ({ items }) => {
                    const details = await Promise.all(items.filter((r) => r.status === "shipped").map((r) => sales.shipments.get(r.id).catch(() => null)));
                    if (!cancelled) setCandidates(details.filter((d): d is ShipmentDto => d !== null && d.status === "shipped" && d.carrierCode === OWN_VEHICLE));
                })
                .catch((e: unknown) => {
                    if (cancelled) return;
                    if (!(e instanceof ApiError)) throw e;
                    setError(e);
                    setCandidates([]);
                });
            identity.getMembers().then(
                (m) => !cancelled && setMembers(m),
                () => undefined,
            );
        }
        return () => {
            cancelled = true;
        };
    }, [allowed, planId]);

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

    if (!allowed) return <Alert severity="warning">Teslimat planlarını görüntülemek için yetkiniz yok ya da modül firmanızda kapalı.</Alert>;

    // ---- Mevcut plan ----
    if (planId) {
        if (!plan) return error ? <FormAlert error={error} /> : <Skeleton variant="rounded" height={240} />;
        const open = plan.status === "planned" || plan.status === "inProgress";
        const rows: Row[] = plan.stops.map((s) => ({
            id: s.id,
            number: s.shipmentNumber,
            title: s.customerName,
            subtitle: [s.address?.address, s.address?.district, s.address?.city, s.customerPhone].filter(Boolean).join(", ") || undefined,
            status: s.status,
            actions:
                canDeliver && open && s.status === "pending" ? (
                    <Stack direction="row" spacing={1}>
                        <Button size="small" variant="contained" onClick={() => setStopDialog({ stop: s, kind: "deliver" })}>
                            Teslim edildi
                        </Button>
                        <Button size="small" color="error" onClick={() => setStopDialog({ stop: s, kind: "fail" })}>
                            Teslim edilemedi
                        </Button>
                    </Stack>
                ) : undefined,
        }));
        return (
            <>
                <PageHeader title={`Plan ${plan.number}`} subtitle={`${formatDate(plan.date)} · ${plan.vehicle ?? "Araç yok"} · ${plan.driverName ?? "Sürücü yok"}`} />
                <Stack spacing={2}>
                    <FormAlert error={error} />
                    <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                        <Chip label={PLAN_STATUS_LABELS[plan.status]} color={plan.status === "completed" ? "success" : "info"} />
                        <Box sx={{ flexGrow: 1 }} />
                        <Button variant="outlined" onClick={() => setPdfOpen(true)}>
                            Rota föyü
                        </Button>
                        <Button component={Link} href="/sevkiyat/planlar">
                            Planlar
                        </Button>
                    </Stack>
                    <DashboardCard title="Duraklar">
                        <Reorderable
                            rows={rows}
                            disabled={!canPlan || !open || busy}
                            onMove={(from, to) =>
                                void run(async () => {
                                    const ids = move(plan.stops, from, to).map((s) => s.id);
                                    setPlan(await shipping.plans.reorder(plan.id, ids));
                                })
                            }
                        />
                    </DashboardCard>
                </Stack>
                <DeliveryDialog
                    open={stopDialog?.kind === "deliver"}
                    title={`Teslim edildi · ${stopDialog?.stop.shipmentNumber ?? ""}`}
                    onClose={() => setStopDialog(null)}
                    onSubmit={async (body) => {
                        if (stopDialog) setPlan(await shipping.plans.deliverStop(plan.id, stopDialog.stop.id, body));
                    }}
                />
                <FailDialog
                    open={stopDialog?.kind === "fail"}
                    onClose={() => setStopDialog(null)}
                    onConfirm={async (reason) => {
                        if (stopDialog) setPlan(await shipping.plans.failStop(plan.id, stopDialog.stop.id, reason));
                        setStopDialog(null);
                    }}
                />
                <Dialog open={pdfOpen} onClose={() => setPdfOpen(false)} fullWidth maxWidth="md">
                    <DialogTitle>Rota föyü · {plan.number}</DialogTitle>
                    <DialogContent>
                        <PdfPreview title="Rota föyü" load={() => shipping.plans.pdf(plan.id)} height={560} />
                    </DialogContent>
                    <DialogActions>
                        <Button onClick={() => setPdfOpen(false)}>Kapat</Button>
                    </DialogActions>
                </Dialog>
            </>
        );
    }

    // ---- Yeni plan ----
    const byId = new Map((candidates ?? []).map((c) => [c.id, c]));
    const chosen = picked.map((id) => byId.get(id)).filter((c): c is ShipmentDto => c !== undefined);
    const rows: Row[] = chosen.map((c) => ({ id: c.id, number: c.number, title: c.customerName, subtitle: [c.deliveryAddress?.address, c.deliveryAddress?.district, c.deliveryAddress?.city].filter(Boolean).join(", ") || undefined }));

    return (
        <>
            <PageHeader title="Yeni teslimat planı" subtitle="Kendi aracımızla sevk edilmiş sevkleri seçip durak sırasını belirleyin" />
            <Stack spacing={2}>
                <FormAlert error={error} />
                <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                    <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                    <TextField size="small" label="Araç" value={vehicle} onChange={(e) => setVehicle(e.target.value)} />
                    {members.length > 0 && (
                        <TextField
                            select
                            size="small"
                            label="Sürücü (üye)"
                            value={driverUserId}
                            onChange={(e) => setDriverUserId(e.target.value)}
                            sx={{ minWidth: 200 }}
                            slotProps={{ select: { native: true }, htmlInput: { "aria-label": "Sürücü (üye)" }, inputLabel: { shrink: true } }}
                        >
                            <option value="">Üye değil</option>
                            {members.map((m) => (
                                <option key={m.userId} value={m.userId}>
                                    {m.fullName}
                                </option>
                            ))}
                        </TextField>
                    )}
                    <TextField size="small" label="Sürücü adı" value={driverName} onChange={(e) => setDriverName(e.target.value)} helperText="Üye seçilmediyse serbest metin" />
                </Stack>
                <DashboardCard title="Eklenebilir sevkler">
                    {candidates === null ? (
                        <Skeleton variant="rounded" height={120} />
                    ) : candidates.length === 0 ? (
                        <Typography color="text.secondary">Kendi aracımızla sevk edilmiş, plana eklenebilir sevk yok.</Typography>
                    ) : (
                        <Stack>
                            {candidates.map((c) => (
                                <FormControlLabel
                                    key={c.id}
                                    control={<Checkbox checked={picked.includes(c.id)} onChange={() => setPicked((cur) => (cur.includes(c.id) ? cur.filter((x) => x !== c.id) : [...cur, c.id]))} />}
                                    label={`${c.number} · ${c.customerName}`}
                                />
                            ))}
                        </Stack>
                    )}
                </DashboardCard>
                {chosen.length > 0 && (
                    <DashboardCard title="Durak sırası">
                        <Reorderable rows={rows} onMove={(from, to) => setPicked(move(picked, from, to))} />
                    </DashboardCard>
                )}
                <Box>
                    <Button
                        variant="contained"
                        disabled={busy || chosen.length === 0 || !canPlan}
                        onClick={() =>
                            void run(async () => {
                                const created = await shipping.plans.create({
                                    date,
                                    vehicle: vehicle.trim() || null,
                                    driverUserId: driverUserId || null,
                                    driverName: driverName.trim() || null,
                                    shipmentIds: chosen.map((c) => c.id),
                                });
                                router.push(`/sevkiyat/planlar/${created.id}`);
                            })
                        }
                    >
                        Planı oluştur
                    </Button>
                </Box>
            </Stack>
        </>
    );
}
