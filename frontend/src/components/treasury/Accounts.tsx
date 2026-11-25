"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import DialogContentText from "@mui/material/DialogContentText";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import { IconPencil, IconPower, IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type BranchDto } from "@/lib/api/identity";
import { ACCOUNT_KIND_LABELS, treasury, type TreasuryAccountDto, type TreasuryAccountKind } from "@/lib/api/treasury";
import { isValidTrIban, normalizeIban } from "@/lib/iban";
import { CURRENCIES, formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

type Draft = { id?: string; kind: TreasuryAccountKind; name: string; currency: Currency; branchId: string; iban: string; bankName: string };

const emptyDraft = (branchId: string): Draft => ({ kind: "cash", name: "", currency: "TRY", branchId, iban: "", bankName: "" });

/** Kasa, banka ve kart POS hesapları: bakiyeler, oluşturma, düzenleme, pasifleştirme ve silme. */
export default function Accounts() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canRead = hasPermission("finans.read");
    const canManage = hasPermission("finans.manage");

    const [accounts, setAccounts] = useState<TreasuryAccountDto[] | null>(null);
    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [draft, setDraft] = useState<Draft | null>(null);
    const [deleting, setDeleting] = useState<TreasuryAccountDto | null>(null);
    const [draftError, setDraftError] = useState<ApiError | string | null>(null);

    const load = useCallback(async () => {
        try {
            setAccounts(await treasury.accounts());
        } catch (e) {
            setError(errorMessage(e));
            setAccounts((c) => c ?? []);
        }
    }, []);

    useEffect(() => {
        if (!canRead) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
        identity.branches.list().then(setBranches, () => setBranches([]));
    }, [canRead, load]);

    if (!canRead) return <Alert severity="warning">Kasa ve banka hesaplarını görmek için yetkiniz yok.</Alert>;

    const branchName = (id: string | null) => branches.find((b) => b.id === id)?.name ?? "—";
    const branchOptions = branches.filter((b) => b.isActive);

    async function save() {
        if (!draft) return;
        setDraftError(null);
        const isCash = draft.kind === "cash";
        if (draft.kind === "bank" && !isValidTrIban(draft.iban)) return setDraftError("Geçerli bir TR IBAN girin.");
        const body = {
            kind: draft.kind,
            name: draft.name.trim(),
            currency: draft.currency,
            branchId: draft.branchId,
            iban: draft.kind === "bank" ? normalizeIban(draft.iban) : null,
            bankName: isCash ? null : draft.bankName.trim() || null,
        };
        try {
            if (draft.id) await treasury.updateAccount(draft.id, body);
            else await treasury.createAccount(body);
            setDraft(null);
            await load();
        } catch (e) {
            setDraftError(e instanceof ApiError ? errorMessage(e) : errorMessage(e));
        }
    }

    async function toggle(a: TreasuryAccountDto) {
        setError(null);
        try {
            await treasury.setAccountActive(a.id, !a.isActive);
        } catch (e) {
            setError(errorMessage(e));
        }
        await load();
    }

    async function remove(a: TreasuryAccountDto) {
        setDeleting(null);
        setError(null);
        try {
            await treasury.deleteAccount(a.id);
        } catch (e) {
            setError(errorMessage(e));
        }
        await load();
    }

    const valid = !!draft && !!draft.name.trim() && !!draft.branchId && (draft.kind === "cash" || !!draft.bankName.trim()) && (draft.kind !== "bank" || !!draft.iban.trim());

    return (
        <>
            <PageHeader title="Kasa & Banka" subtitle="Kasa, banka ve kart POS hesapları; bakiye hareketlerin toplamıdır." />
            <DashboardCard
                action={
                    canManage && (
                        <Button variant="contained" onClick={() => setDraft(emptyDraft(branchOptions[0]?.id ?? ""))}>
                            Yeni hesap
                        </Button>
                    )
                }
            >
                <FormAlert error={error} />
                {accounts === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : accounts.length === 0 ? (
                    <Typography color="text.secondary" sx={{ py: 3 }}>
                        Henüz hesap yok. İlk kasanızı ya da banka hesabınızı ekleyin.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Ad</TableCell>
                                    <TableCell>Tür</TableCell>
                                    <TableCell>Şube</TableCell>
                                    <TableCell>IBAN</TableCell>
                                    <TableCell align="right">Bakiye</TableCell>
                                    {canManage && <TableCell align="right">İşlem</TableCell>}
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {accounts.map((a) => (
                                    <TableRow key={a.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`/finans/kasa-banka/${a.id}`)}>
                                        <TableCell>
                                            {a.name} {!a.isActive && <Chip size="small" label="Pasif" sx={{ ml: 1 }} />}
                                        </TableCell>
                                        <TableCell>{ACCOUNT_KIND_LABELS[a.kind]}</TableCell>
                                        <TableCell>{branchName(a.branchId)}</TableCell>
                                        <TableCell>{a.iban ?? ""}</TableCell>
                                        <TableCell align="right" sx={a.balance < 0 ? { color: "error.main" } : undefined}>
                                            {formatMoney(a.balance, a.currency as Currency)}
                                        </TableCell>
                                        {canManage && (
                                            <TableCell align="right" onClick={(e) => e.stopPropagation()}>
                                                <Tooltip title="Düzenle">
                                                    <IconButton
                                                        aria-label={`${a.name} düzenle`}
                                                        onClick={() =>
                                                            setDraft({ id: a.id, kind: a.kind, name: a.name, currency: a.currency as Currency, branchId: a.branchId ?? "", iban: a.iban ?? "", bankName: a.bankName ?? "" })
                                                        }
                                                    >
                                                        <IconPencil size={18} />
                                                    </IconButton>
                                                </Tooltip>
                                                <Tooltip title={a.isActive ? "Pasifleştir" : "Etkinleştir"}>
                                                    <IconButton aria-label={`${a.name} ${a.isActive ? "pasifleştir" : "etkinleştir"}`} onClick={() => void toggle(a)}>
                                                        <IconPower size={18} />
                                                    </IconButton>
                                                </Tooltip>
                                                <Tooltip title="Sil">
                                                    <IconButton aria-label={`${a.name} sil`} onClick={() => setDeleting(a)}>
                                                        <IconTrash size={18} />
                                                    </IconButton>
                                                </Tooltip>
                                            </TableCell>
                                        )}
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
            </DashboardCard>

            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Hesap silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting?.name} hesabı kalıcı olarak silinecek. Hareketi olan hesap silinemez; pasifleştirebilirsiniz.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDeleting(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => deleting && void remove(deleting)}>
                        Evet, sil
                    </Button>
                </DialogActions>
            </Dialog>

            <Dialog open={draft !== null} onClose={() => setDraft(null)} fullWidth maxWidth="xs">
                <DialogTitle>{draft?.id ? "Hesabı düzenle" : "Yeni hesap"}</DialogTitle>
                <DialogContent>
                    <Stack spacing={2} sx={{ pt: 1 }}>
                        <FormAlert error={draftError} />
                        <TextField select label="Tür" value={draft?.kind ?? "cash"} onChange={(e) => setDraft((d) => d && { ...d, kind: e.target.value as TreasuryAccountKind })}>
                            {(Object.keys(ACCOUNT_KIND_LABELS) as TreasuryAccountKind[]).map((k) => (
                                <MenuItem key={k} value={k}>
                                    {ACCOUNT_KIND_LABELS[k]}
                                </MenuItem>
                            ))}
                        </TextField>
                        <TextField label="Ad" value={draft?.name ?? ""} onChange={(e) => setDraft((d) => d && { ...d, name: e.target.value })} />
                        <TextField select label="Para birimi" value={draft?.currency ?? "TRY"} onChange={(e) => setDraft((d) => d && { ...d, currency: e.target.value as Currency })}>
                            {CURRENCIES.map((c) => (
                                <MenuItem key={c} value={c}>
                                    {c}
                                </MenuItem>
                            ))}
                        </TextField>
                        <TextField select label="Şube" value={draft?.branchId ?? ""} onChange={(e) => setDraft((d) => d && { ...d, branchId: e.target.value })}>
                            {branchOptions.map((b) => (
                                <MenuItem key={b.id} value={b.id}>
                                    {b.name}
                                </MenuItem>
                            ))}
                        </TextField>
                        {draft && draft.kind !== "cash" && <TextField label="Banka adı" value={draft.bankName} onChange={(e) => setDraft((d) => d && { ...d, bankName: e.target.value })} />}
                        {draft?.kind === "bank" && <TextField label="IBAN" value={draft.iban} onChange={(e) => setDraft((d) => d && { ...d, iban: e.target.value })} />}
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDraft(null)}>Vazgeç</Button>
                    <Button variant="contained" disabled={!valid} onClick={() => void save()}>
                        Kaydet
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
