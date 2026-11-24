"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
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
import { pricing, type PriceListDto, type PriceListKind } from "@/lib/api/pricing";
import { useSession } from "@/session/SessionProvider";
import PriceListDialog from "./PriceListDialog";

export const formatDate = (iso: string): string => iso.split("-").reverse().join(".");

export function validityText(from: string | null, to: string | null): string {
    if (!from && !to) return "Süresiz";
    return `${from ? formatDate(from) : "…"} – ${to ? formatDate(to) : "…"}`;
}

export function derivedText(list: PriceListDto, all: PriceListDto[]): string | null {
    if (!list.baseListId || list.adjustmentPercent === null) return null;
    const baseName = all.find((l) => l.id === list.baseListId)?.name ?? "Taban liste";
    const sign = list.adjustmentPercent < 0 ? "−" : "+";
    return `${baseName} ${sign}%${String(Math.abs(list.adjustmentPercent)).replace(".", ",")}`;
}

type PriceListsProps = {
    kind: PriceListKind;
    title: string;
};

/** Satış ya da alış fiyat listelerinin tablosu; yeni liste, düzenleme ve silme. Alış listeleri fiyat.alis ister. */
export default function PriceLists({ kind, title }: PriceListsProps) {
    const { hasPermission } = useSession();
    const allowed = hasPermission("fiyat.read") && (kind === "sale" || hasPermission("fiyat.alis"));
    const canManage = hasPermission("fiyat.manage");

    const [lists, setLists] = useState<PriceListDto[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [includeInactive, setIncludeInactive] = useState(false);
    const [dialog, setDialog] = useState<{ list?: PriceListDto } | null>(null);
    const [deleting, setDeleting] = useState<PriceListDto | null>(null);

    const load = useCallback(async () => {
        try {
            setLists(await pricing.lists({ kind, includeInactive }));
        } catch (e) {
            setError(errorMessage(e));
            setLists((current) => current ?? []);
        }
    }, [kind, includeInactive]);

    useEffect(() => {
        if (!allowed) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [allowed, load]);

    if (!allowed) {
        return (
            <>
                <PageHeader title={title} />
                <Alert severity="warning">{kind === "purchase" ? "Alış fiyat listelerini görmek için yetkiniz yok." : "Fiyat listelerini görmek için yetkiniz yok."}</Alert>
            </>
        );
    }

    async function confirmDelete() {
        if (!deleting) return;
        const target = deleting;
        setDeleting(null);
        setError(null);
        try {
            await pricing.deleteList(target.id);
        } catch (e) {
            setError(errorMessage(e));
        }
        await load();
    }

    const editorPath = (id: string) => `/satis/fiyat-listeleri/${id}`;

    return (
        <>
            <PageHeader title={title} subtitle={kind === "purchase" ? "Tedarikçi ve firma alış fiyat listeleri." : "Satış fiyat listeleri; müşteri gruplarına ve varsayılana atanır."} />
            <DashboardCard
                action={
                    <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
                        <FormControlLabel control={<Switch checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />} label="Pasifleri göster" />
                        {canManage && (
                            <Button variant="contained" onClick={() => setDialog({})}>
                                Yeni liste
                            </Button>
                        )}
                    </Stack>
                }
            >
                <FormAlert error={error} />
                {lists === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : lists.length === 0 ? (
                    <Typography color="text.secondary" sx={{ py: 3 }}>
                        Henüz fiyat listesi yok.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Ad</TableCell>
                                    <TableCell>Tür</TableCell>
                                    <TableCell>Para birimi</TableCell>
                                    <TableCell>KDV</TableCell>
                                    <TableCell>Geçerlilik</TableCell>
                                    <TableCell>Kalem</TableCell>
                                    <TableCell>Durum</TableCell>
                                    <TableCell align="right">İşlem</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {lists.map((list) => {
                                    const derived = derivedText(list, lists);
                                    return (
                                        <TableRow key={list.id} hover>
                                            <TableCell>
                                                <Link href={editorPath(list.id)}>{list.name}</Link>
                                            </TableCell>
                                            <TableCell>{derived ? `Türetilmiş (${derived})` : list.kind === "purchase" ? "Alış" : "Satış"}</TableCell>
                                            <TableCell>{list.currency}</TableCell>
                                            <TableCell>{list.vatIncluded ? "KDV dahil" : "KDV hariç"}</TableCell>
                                            <TableCell>{validityText(list.validFrom, list.validTo)}</TableCell>
                                            <TableCell>{list.itemCount}</TableCell>
                                            <TableCell>
                                                <Stack direction="row" spacing={0.5}>
                                                    {list.isDefault && <Chip size="small" color="primary" label="Varsayılan" />}
                                                    {!list.isActive && <Chip size="small" label="Pasif" />}
                                                </Stack>
                                            </TableCell>
                                            <TableCell align="right">
                                                {canManage && (
                                                    <>
                                                        <IconButton aria-label={`${list.name} listesini düzenle`} size="small" onClick={() => setDialog({ list })}>
                                                            <IconPencil size={18} />
                                                        </IconButton>
                                                        <IconButton aria-label={`${list.name} listesini sil`} size="small" onClick={() => setDeleting(list)}>
                                                            <IconTrash size={18} />
                                                        </IconButton>
                                                    </>
                                                )}
                                            </TableCell>
                                        </TableRow>
                                    );
                                })}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
            </DashboardCard>

            <PriceListDialog
                open={dialog !== null}
                kind={kind}
                list={dialog?.list}
                lists={lists ?? []}
                onClose={() => setDialog(null)}
                onSaved={() => {
                    setDialog(null);
                    void load();
                }}
            />
            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Liste silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting?.name} listesi ve tüm satırları silinecek. Bu işlem geri alınamaz.</DialogContentText>
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
