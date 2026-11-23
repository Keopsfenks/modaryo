"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type BranchDto, type PermissionGroup, type RoleDto } from "@/lib/api/identity";
import { useSession } from "@/session/SessionProvider";
import BranchScopeField from "./BranchScopeField";
import PermissionMatrix from "./PermissionMatrix";

const LIST_PATH = "/ayarlar/roller";
const OWNER_MESSAGE = "Sahip rolü tüm yetkilere sahiptir ve değiştirilemez.";

type FormState = { name: string; permissions: string[]; discount: string; allBranches: boolean; branchIds: string[] };

const fromRole = (role: RoleDto, name = role.name): FormState => ({
    name,
    permissions: [...role.permissions].sort(),
    discount: String(role.maxDiscountPercent),
    allBranches: role.allBranches,
    branchIds: role.allBranches ? [] : [...role.branchIds],
});

const EMPTY: FormState = { name: "", permissions: [], discount: "0", allBranches: true, branchIds: [] };

const snapshot = (f: FormState) => JSON.stringify({ ...f, permissions: [...f.permissions].sort(), branchIds: [...f.branchIds].sort() });

const validDiscount = (value: string) => {
    const n = Number(value);
    return value.trim() !== "" && Number.isFinite(n) && n >= 0 && n <= 100 && /^\d+(\.\d{1,2})?$/.test(value.trim());
};

/** Rol oluşturma/düzenleme formu (izin matrisi, iskonto sınırı, şube kapsamı). roleId yoksa yeni rol; ?kopya= ile kaynaktan önceden doldurulur. */
export default function RoleEditor({ roleId }: { roleId?: string }) {
    const router = useRouter();
    const copyFrom = useSearchParams().get("kopya");
    const { hasPermission, canAccessBranch, session } = useSession();
    const canManage = hasPermission("roller.manage");

    const [groups, setGroups] = useState<PermissionGroup[] | null>(null);
    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [role, setRole] = useState<RoleDto | null>(null);
    const [form, setForm] = useState<FormState | null>(null);
    const [initial, setInitial] = useState("");
    const [dropped, setDropped] = useState(false);
    const hasPermissionRef = useRef(hasPermission);
    useEffect(() => {
        hasPermissionRef.current = hasPermission;
    });
    const [loadError, setLoadError] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [nameError, setNameError] = useState<string | null>(null);
    const [discountError, setDiscountError] = useState<string | null>(null);
    const [branchError, setBranchError] = useState<string | null>(null);
    const [saving, setSaving] = useState(false);
    const [confirmReset, setConfirmReset] = useState(false);
    const [confirmLeave, setConfirmLeave] = useState(false);
    const leaving = useRef(false);

    useEffect(() => {
        const sourceId = roleId ?? copyFrom;
        Promise.all([
            identity.permissions.catalog(),
            identity.branches.list().catch(() => [] as BranchDto[]),
            sourceId ? identity.roles.get(sourceId) : Promise.resolve(null),
        ]).then(
            ([catalog, branchList, source]) => {
                let next = source ? fromRole(source, roleId ? source.name : `${source.name} (kopya)`) : EMPTY;
                let droppedAny = false;
                if (source && !roleId) {
                    // Kopyada "*" katalog kodlarına açılır; sunucu oluşturmada her izni çağıranda arar.
                    const catalogCodes = catalog.flatMap((g) => g.permissions.map((p) => p.code));
                    const wanted = source.permissions.includes("*") ? catalogCodes : source.permissions;
                    const kept = wanted.filter((c) => hasPermissionRef.current(c));
                    droppedAny = kept.length < wanted.length;
                    next = { ...next, permissions: [...kept].sort() };
                }
                setDropped(droppedAny);
                setGroups(catalog);
                setBranches(branchList);
                setRole(roleId ? source : null);
                setForm(next);
                // Kopya, kaydedilmemiş yeni bir rol sayılır: başlangıç boş form olduğundan "değişmiş" görünür.
                setInitial(snapshot(roleId ? next : EMPTY));
            },
            (e: unknown) => setLoadError(errorMessage(e)),
        );
    }, [roleId, copyFrom]);

    const locked = !!role?.locked;
    const readOnly = locked || !canManage;
    const dirty = !!form && snapshot(form) !== initial;
    const selected = useMemo(() => new Set(form?.permissions ?? []), [form?.permissions]);
    const allCodes = useMemo(() => new Set((groups ?? []).flatMap((g) => g.permissions.map((p) => p.code))), [groups]);
    // Sahip rolü "*" taşır: matriste her şey işaretli görünür.
    const shown = form?.permissions.includes("*") ? allCodes : selected;

    useEffect(() => {
        if (!dirty) return;
        const handler = (e: BeforeUnloadEvent) => {
            if (leaving.current) return;
            e.preventDefault();
        };
        window.addEventListener("beforeunload", handler);
        return () => window.removeEventListener("beforeunload", handler);
    }, [dirty]);

    if (loadError) return <Alert severity="error">{loadError}</Alert>;
    if (!form || !groups) return <CircularProgress aria-label="Yükleniyor" />;

    const patchForm = (p: Partial<FormState>) => setForm({ ...form, ...p });

    const goBack = () => {
        if (dirty && !readOnly) {
            setConfirmLeave(true);
            return;
        }
        router.push(LIST_PATH);
    };

    const save = async () => {
        setError(null);
        setNameError(null);
        setDiscountError(null);
        setBranchError(null);
        let invalid = false;
        if (form.name.trim().length < 2) {
            setNameError("Rol adı en az 2 karakter olmalıdır.");
            invalid = true;
        }
        if (!validDiscount(form.discount)) {
            setDiscountError("İskonto sınırı 0 ile 100 arasında olmalı ve en fazla 2 ondalık basamak içermelidir.");
            invalid = true;
        }
        if (!form.allBranches && form.branchIds.length === 0) {
            setBranchError("En az bir şube seçin.");
            invalid = true;
        }
        if (invalid) return;

        const body = {
            name: form.name.trim(),
            permissions: form.permissions,
            maxDiscountPercent: Number(form.discount),
            allBranches: form.allBranches,
            branchIds: form.allBranches ? [] : form.branchIds,
        };
        setSaving(true);
        try {
            if (roleId) await identity.roles.update(roleId, body);
            else await identity.roles.create(body);
            leaving.current = true;
            router.push(LIST_PATH);
        } catch (e) {
            if (e instanceof ApiError && e.code === "role_name_taken") setNameError(errorMessage(e));
            else if (e instanceof ApiError && e.fieldErrors.name) setNameError(e.fieldErrors.name);
            else setError(errorMessage(e));
            setSaving(false);
        }
    };

    const reset = async () => {
        if (!roleId) return;
        setConfirmReset(false);
        setError(null);
        try {
            const dto = await identity.roles.reset(roleId);
            const next = fromRole(dto);
            setRole(dto);
            setForm(next);
            setInitial(snapshot(next));
        } catch (e) {
            setError(errorMessage(e));
        }
    };

    const canReset = !!roleId && canManage && (role?.systemKey === "admin" || role?.systemKey === "member");

    return (
        <Stack spacing={3}>
            {locked && <Alert severity="info">{OWNER_MESSAGE}</Alert>}
            {dropped && <Alert severity="info">Sahip olmadığınız izinler kopyaya alınmadı.</Alert>}
            {error && <Alert severity="error">{error}</Alert>}

            <TextField
                label="Rol adı"
                value={form.name}
                onChange={(e) => patchForm({ name: e.target.value })}
                disabled={readOnly}
                error={!!nameError}
                helperText={nameError ?? undefined}
                slotProps={{ htmlInput: { maxLength: 50 } }}
                sx={{ maxWidth: 400 }}
            />

            <PermissionMatrix
                groups={groups}
                selected={shown}
                grantable={(code) => hasPermission(code)}
                onChange={(next) => patchForm({ permissions: [...next] })}
                readOnly={readOnly}
            />

            <TextField
                label="Azami iskonto (%)"
                type="number"
                value={form.discount}
                onChange={(e) => patchForm({ discount: e.target.value })}
                disabled={readOnly}
                error={!!discountError}
                helperText={discountError ?? undefined}
                slotProps={{ htmlInput: { min: 0, max: 100, step: 0.01 } }}
                sx={{ maxWidth: 200 }}
            />

            <BranchScopeField
                allBranches={form.allBranches}
                branchIds={form.branchIds}
                branches={branches}
                canAccessBranch={canAccessBranch}
                canGrantAll={session.branchIds === null}
                disabled={readOnly}
                error={branchError ?? undefined}
                onChange={(next) => patchForm(next)}
            />

            <Stack direction="row" spacing={1}>
                {!readOnly && (
                    <Button variant="contained" onClick={save} disabled={saving}>
                        Kaydet
                    </Button>
                )}
                <Button onClick={goBack}>{readOnly ? "Geri" : "Vazgeç"}</Button>
                {canReset && (
                    <Button color="warning" onClick={() => setConfirmReset(true)}>
                        Varsayılana dön
                    </Button>
                )}
            </Stack>

            <Dialog open={confirmReset} onClose={() => setConfirmReset(false)}>
                <DialogTitle>Varsayılana dön</DialogTitle>
                <DialogContent>
                    <DialogContentText>
                        Bu rolün izinleri hazır varsayılanlara döner; yaptığınız özelleştirmeler kaybolur. Devam edilsin mi?
                    </DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirmReset(false)}>Vazgeç</Button>
                    <Button color="warning" onClick={reset}>
                        Evet, döndür
                    </Button>
                </DialogActions>
            </Dialog>

            <Dialog open={confirmLeave} onClose={() => setConfirmLeave(false)}>
                <DialogTitle>Kaydedilmemiş değişiklikler</DialogTitle>
                <DialogContent>
                    <DialogContentText>Kaydedilmemiş değişiklikleriniz var. Sayfadan ayrılırsanız kaybolacaklar.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirmLeave(false)}>Düzenlemeye devam et</Button>
                    <Button
                        color="error"
                        onClick={() => {
                            leaving.current = true;
                            router.push(LIST_PATH);
                        }}
                    >
                        Ayrıl
                    </Button>
                </DialogActions>
            </Dialog>
        </Stack>
    );
}
