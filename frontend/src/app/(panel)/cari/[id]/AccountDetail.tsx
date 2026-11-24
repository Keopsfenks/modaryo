"use client";

import { useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";
import FormAlert from "@/components/auth/FormAlert";
import AccountForm from "@/components/cari/AccountForm";
import AccountHeader from "@/components/cari/AccountHeader";
import AddressesTab from "@/components/cari/AddressesTab";
import DueTab from "@/components/cari/DueTab";
import OpenInvoicesTab from "@/components/invoicing/OpenInvoicesTab";
import CustomerPricesTab from "@/components/pricing/CustomerPricesTab";
import MovementsTab from "@/components/cari/MovementsTab";
import StatementTab from "@/components/cari/StatementTab";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { erp, type CurrentAccountDto } from "@/lib/api/erp";
import { useSession } from "@/session/SessionProvider";

const BASE_TABS = [
    { value: "genel", label: "Genel" },
    { value: "adresler", label: "Adresler" },
    { value: "hareketler", label: "Hareketler" },
    { value: "ekstre", label: "Ekstre" },
    { value: "vade", label: "Vade" },
    { value: "acik-faturalar", label: "Açık faturalar" },
] as const;

const PRICES_TAB = { value: "ozel-fiyatlar", label: "Özel fiyatlar" } as const;

type TabValue = (typeof BASE_TABS)[number]["value"] | typeof PRICES_TAB.value;

/** Cari detay sayfası: başlık ve sekmeler. Seçili sekme ?sekme= parametresinde tutulur. */
export default function AccountDetail({ id }: { id: string }) {
    const router = useRouter();
    const searchParams = useSearchParams();
    const { hasPermission } = useSession();
    const canRead = hasPermission("cari.read");
    const canManage = hasPermission("cari.manage");
    const canMove = hasPermission("cari.movements");

    const [account, setAccount] = useState<CurrentAccountDto | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [actionError, setActionError] = useState<ApiError | string | null>(null);
    const [saved, setSaved] = useState(false);
    const [busy, setBusy] = useState(false);
    const [confirmDelete, setConfirmDelete] = useState(false);

    const canSeePrices = hasPermission("fiyat.read");
    const canManagePrices = hasPermission("fiyat.manage");
    const tabs: readonly { value: TabValue; label: string }[] = account?.isCustomer && canSeePrices ? [...BASE_TABS, PRICES_TAB] : BASE_TABS;
    const sekme = searchParams.get("sekme");
    const tab: TabValue = tabs.find((t) => t.value === sekme)?.value ?? "genel";

    useEffect(() => {
        if (!canRead) return;
        let ignore = false;
        erp.getAccount(id).then(
            (dto) => {
                if (!ignore) setAccount(dto);
            },
            (e) => {
                if (ignore) return;
                if (!(e instanceof ApiError)) throw e;
                setLoadError(e);
            },
        );
        return () => {
            ignore = true;
        };
    }, [id, canRead]);

    if (!canRead) {
        return <Alert severity="warning">Cari hesapları görüntülemek için yetkiniz yok.</Alert>;
    }
    if (loadError) {
        return <FormAlert error={loadError} />;
    }
    if (!account) {
        return (
            <Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
                <CircularProgress />
            </Box>
        );
    }

    const run = async (action: () => Promise<void>) => {
        setBusy(true);
        setActionError(null);
        try {
            await action();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setActionError(e.code === "has_movements" ? "Hareketi olan cari silinemez. Bunun yerine pasifleştirin." : e);
        } finally {
            setBusy(false);
        }
    };

    const toggleActive = () =>
        run(async () => {
            await erp.setActive(account.id, !account.isActive);
            setAccount(await erp.getAccount(account.id));
        });

    const remove = () => {
        setConfirmDelete(false);
        return run(async () => {
            await erp.deleteAccount(account.id);
            router.push("/finans/cari-hesaplar");
        });
    };

    const changeTab = (_event: React.SyntheticEvent, value: TabValue) => {
        router.replace(`/cari/${id}?sekme=${value}`, { scroll: false });
    };

    return (
        <Box sx={{ display: "flex", flexDirection: "column", gap: 3 }}>
            <AccountHeader account={account} canManage={canManage} busy={busy} onToggleActive={toggleActive} onDelete={() => setConfirmDelete(true)} />
            <FormAlert error={actionError} />

            <Tabs className="no-print" value={tab} onChange={changeTab} variant="scrollable" scrollButtons="auto" aria-label="Cari sekmeleri">
                {tabs.map((t) => (
                    <Tab key={t.value} value={t.value} label={t.label} />
                ))}
            </Tabs>

            {tab === "genel" && (
                <DashboardCard>
                    {!canManage && (
                        <Alert severity="info" sx={{ mb: 2 }}>
                            Kartı düzenlemek için yetkiniz yok; alanlar salt okunur.
                        </Alert>
                    )}
                    {saved && (
                        <Alert severity="success" sx={{ mb: 2 }}>
                            Kart kaydedildi.
                        </Alert>
                    )}
                    <fieldset disabled={!canManage} inert={!canManage} style={{ border: 0, margin: 0, padding: 0, minWidth: 0 }}>
                        <AccountForm
                            initial={account}
                            onSaved={(dto) => {
                                setAccount(dto);
                                setSaved(true);
                            }}
                        />
                    </fieldset>
                </DashboardCard>
            )}
            {tab === "adresler" && <AddressesTab account={account} canManage={canManage} onChanged={setAccount} />}
            {tab === "hareketler" && <MovementsTab account={account} canMove={canMove} onChanged={setAccount} />}
            {tab === "ekstre" && <StatementTab account={account} />}
            {tab === "vade" && <DueTab account={account} />}
            {tab === "ozel-fiyatlar" && <CustomerPricesTab account={account} canManage={canManagePrices} />}
            {tab === "acik-faturalar" && <OpenInvoicesTab account={account} />}

            <Dialog open={confirmDelete} onClose={() => setConfirmDelete(false)}>
                <DialogTitle>Cari silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>{account.name} kartı kalıcı olarak silinecek. Bu işlem geri alınamaz.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirmDelete(false)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={remove}>
                        Evet, sil
                    </Button>
                </DialogActions>
            </Dialog>
        </Box>
    );
}
