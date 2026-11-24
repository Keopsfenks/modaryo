"use client";

import { useCallback, useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import IconButton from "@mui/material/IconButton";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import { IconPencil, IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { errorMessage } from "@/lib/api/errorMessages";
import { pricing, type CampaignDto } from "@/lib/api/pricing";
import { useSession } from "@/session/SessionProvider";
import CampaignDialog, { DAYS } from "./CampaignDialog";
import { validityText } from "./PriceLists";

export function campaignSummary(c: CampaignDto): string {
    return c.kind === "buyXPayY" ? `${c.buyQty} al ${c.payQty} öde` : `%${String(c.percent ?? 0).replace(".", ",")} indirim`;
}

function windowText(c: CampaignDto): string {
    const days = c.days && c.days.length > 0 ? DAYS.filter((d) => c.days!.includes(d.value)).map((d) => d.short).join(", ") : "Her gün";
    const time = c.fromTime && c.toTime ? ` ${c.fromTime.slice(0, 5)}–${c.toTime.slice(0, 5)}` : "";
    return `${days}${time}`;
}

/** Kampanya listesi: oluşturma, düzenleme, silme. Kampanyalar birikmez; satır başına en iyisi uygulanır. */
export default function Campaigns() {
    const { hasPermission } = useSession();
    const allowed = hasPermission("fiyat.read");
    const canManage = hasPermission("fiyat.manage");

    const [campaigns, setCampaigns] = useState<CampaignDto[] | null>(null);
    const [includeInactive, setIncludeInactive] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [dialog, setDialog] = useState<{ campaign?: CampaignDto } | null>(null);
    const [deleting, setDeleting] = useState<CampaignDto | null>(null);

    const load = useCallback(async () => {
        try {
            setCampaigns(await pricing.campaigns(includeInactive));
        } catch (e) {
            setError(errorMessage(e));
            setCampaigns((current) => current ?? []);
        }
    }, [includeInactive]);

    useEffect(() => {
        if (!allowed) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [allowed, load]);

    if (!allowed) {
        return (
            <>
                <PageHeader title="Kampanyalar" />
                <Alert severity="warning">Kampanyaları görmek için yetkiniz yok.</Alert>
            </>
        );
    }

    async function confirmDelete() {
        if (!deleting) return;
        const target = deleting;
        setDeleting(null);
        setError(null);
        try {
            await pricing.deleteCampaign(target.id);
        } catch (e) {
            setError(errorMessage(e));
        }
        await load();
    }

    return (
        <>
            <PageHeader title="Kampanyalar" subtitle="X al Y öde ve yüzde indirim kampanyaları. Kampanyalar birikmez; satır başına en iyi kampanya uygulanır." />
            <DashboardCard
                action={
                    <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
                        <FormControlLabel control={<Switch checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />} label="Pasifleri göster" />
                        {canManage && (
                            <Button variant="contained" onClick={() => setDialog({})}>
                                Yeni kampanya
                            </Button>
                        )}
                    </Stack>
                }
            >
                <FormAlert error={error} />
                {campaigns === null ? (
                    <Skeleton variant="rounded" height={140} />
                ) : campaigns.length === 0 ? (
                    <Typography color="text.secondary" sx={{ py: 3 }}>
                        Henüz kampanya yok.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Ad</TableCell>
                                    <TableCell>Kampanya</TableCell>
                                    <TableCell>Geçerlilik</TableCell>
                                    <TableCell>Gün ve saat</TableCell>
                                    <TableCell>Kapsam</TableCell>
                                    <TableCell>Durum</TableCell>
                                    <TableCell align="right">İşlem</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {campaigns.map((c) => (
                                    <TableRow key={c.id} hover>
                                        <TableCell>{c.name}</TableCell>
                                        <TableCell>{campaignSummary(c)}</TableCell>
                                        <TableCell>{validityText(c.validFrom, c.validTo)}</TableCell>
                                        <TableCell>{windowText(c)}</TableCell>
                                        <TableCell>{[c.productIds.length && `${c.productIds.length} ürün`, c.variantIds.length && `${c.variantIds.length} varyant`, c.categoryIds.length && `${c.categoryIds.length} kategori`].filter(Boolean).join(", ")}</TableCell>
                                        <TableCell>{c.isActive ? <Chip size="small" color="success" variant="outlined" label="Aktif" /> : <Chip size="small" label="Pasif" />}</TableCell>
                                        <TableCell align="right">
                                            {canManage && (
                                                <>
                                                    <IconButton aria-label={`${c.name} kampanyasını düzenle`} size="small" onClick={() => setDialog({ campaign: c })}>
                                                        <IconPencil size={18} />
                                                    </IconButton>
                                                    <IconButton aria-label={`${c.name} kampanyasını sil`} size="small" onClick={() => setDeleting(c)}>
                                                        <IconTrash size={18} />
                                                    </IconButton>
                                                </>
                                            )}
                                        </TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
            </DashboardCard>

            <CampaignDialog
                open={dialog !== null}
                campaign={dialog?.campaign}
                onClose={() => setDialog(null)}
                onSaved={() => {
                    setDialog(null);
                    void load();
                }}
            />

            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Kampanya silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting?.name} kampanyası silinecek.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDeleting(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void confirmDelete()}>
                        Evet, sil
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
