"use client";

import { useEffect, useMemo, useState } from "react";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import CariPicker from "@/components/treasury/CariPicker";
import type { CurrentAccountListItem } from "@/lib/api/erp";
import { identity, type BranchDto, type MemberDto } from "@/lib/api/identity";
import { CURRENCIES } from "@/lib/money";
import { todayIso } from "@/lib/date";
import { useSession } from "@/session/SessionProvider";
import { BACK_TO_BACK_MODULE } from "./SalesLines";
import { toPreviewInput, toSalesLineInput, validateSalesLine, type SalesLineDraft } from "./salesModel";

export type SalesDocInitial = {
    customer: { id: string; name: string } | null;
    branchId: string;
    date: string;
    currency: string;
    salesRepUserId: string | null;
    docDiscount: number;
    notes: string | null;
    lines: SalesLineDraft[];
};

const nativeSelect = (label: string) => ({ select: { native: true }, htmlInput: { "aria-label": label } });

/** Teklif ve sipariş formlarının ortak durumu: başlık alanları, satırlar, doğrulama ve sunucu önizlemesi için gövde. */
export function useSalesDoc(initial: SalesDocInitial | null) {
    const { canAccessBranch, hasModule } = useSession();
    const [customer, setCustomer] = useState<CurrentAccountListItem | null>(
        initial?.customer ? ({ id: initial.customer.id, code: "", name: initial.customer.name } as CurrentAccountListItem) : null,
    );
    const [branchId, setBranchId] = useState(initial?.branchId ?? "");
    const [date, setDate] = useState(initial?.date ?? todayIso());
    const [currency, setCurrency] = useState(initial?.currency ?? "TRY");
    const [repId, setRepId] = useState(initial?.salesRepUserId ?? "");
    const [docDiscount, setDocDiscount] = useState(initial && initial.docDiscount ? String(initial.docDiscount).replace(".", ",") : "");
    const [notes, setNotes] = useState(initial?.notes ?? "");
    const [lines, setLines] = useState<SalesLineDraft[]>(initial?.lines ?? []);
    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [members, setMembers] = useState<MemberDto[]>([]);

    useEffect(() => {
        let cancelled = false;
        identity.branches.list().then(
            (list) => {
                if (cancelled) return;
                const usable = list.filter((b) => b.isActive && canAccessBranch(b.id));
                setBranches(usable);
                setBranchId((cur) => cur || (usable.find((b) => b.isDefault) ?? usable[0])?.id || "");
            },
            () => undefined,
        );
        // Temsilci seçenekleri kimlik servisinin üye listesinden gelir; yetki yoksa liste boş kalır.
        identity.getMembers().then(
            (list) => !cancelled && setMembers(list),
            () => undefined,
        );
        return () => {
            cancelled = true;
        };
    }, [canAccessBranch]);

    const moduleOn = hasModule(BACK_TO_BACK_MODULE);

    const previewInput = useMemo(
        () => toPreviewInput({ lines, currentAccountId: customer?.id ?? "", branchId, date, currency, docDiscount }),
        [lines, customer, branchId, date, currency, docDiscount],
    );

    /** Hata iletisi ya da null; satır hatalarını işaretler. */
    function validate(): string | null {
        if (!customer) return "Müşteri seçilmelidir.";
        if (!branchId) return "Şube seçilmelidir.";
        if (lines.length === 0) return "En az bir satır eklenmelidir.";
        const found = lines.map(validateSalesLine);
        if (found.some(Boolean)) {
            setLines(lines.map((l, i) => ({ ...l, error: found[i] })));
            return "Satırlardaki hataları düzeltin.";
        }
        if (docDiscount.trim() && previewInput === null) return "Belge iskontosu 0-100 arasında olmalıdır.";
        return null;
    }

    function body() {
        return {
            currentAccountId: customer?.id ?? "",
            branchId,
            date,
            currency,
            salesRepUserId: repId || null,
            docDiscount: docDiscount.trim() ? Number(docDiscount.replace(",", ".")) : null,
            notes: notes.trim() || null,
            lines: lines.map((l) => toSalesLineInput(l, moduleOn)),
        };
    }

    return {
        customer,
        pickCustomer(item: CurrentAccountListItem | null) {
            setCustomer(item);
            if (item?.currency) setCurrency(item.currency);
        },
        branchId,
        setBranchId,
        date,
        setDate,
        currency,
        setCurrency,
        repId,
        setRepId,
        docDiscount,
        setDocDiscount,
        notes,
        setNotes,
        lines,
        setLines,
        branches,
        members,
        previewInput,
        validate,
        body,
    };
}

export type SalesDoc = ReturnType<typeof useSalesDoc>;

type HeaderFieldsProps = { doc: SalesDoc; disabled?: boolean; children?: React.ReactNode };

/** Müşteri, şube, tarih, para birimi, satış temsilcisi, belge iskontosu ve not alanları. */
export function SalesHeaderFields({ doc, disabled, children }: HeaderFieldsProps) {
    return (
        <Stack spacing={2}>
            <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                <CariPicker label="Müşteri" role="customer" value={doc.customer} onChange={doc.pickCustomer} disabled={disabled} />
                <TextField select size="small" label="Şube" value={doc.branchId} disabled={disabled} onChange={(e) => doc.setBranchId(e.target.value)} slotProps={nativeSelect("Şube")} sx={{ minWidth: 180 }}>
                    {doc.branches.map((b) => (
                        <option key={b.id} value={b.id}>
                            {b.name}
                        </option>
                    ))}
                </TextField>
            </Stack>
            <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                <TextField size="small" type="date" label="Tarih" value={doc.date} disabled={disabled} onChange={(e) => doc.setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                {children}
                <TextField select size="small" label="Para birimi" value={doc.currency} disabled={disabled} onChange={(e) => doc.setCurrency(e.target.value)} slotProps={nativeSelect("Para birimi")} sx={{ width: 130 }}>
                    {CURRENCIES.map((c) => (
                        <option key={c} value={c}>
                            {c}
                        </option>
                    ))}
                </TextField>
                <TextField select size="small" label="Satış temsilcisi" value={doc.repId} disabled={disabled} onChange={(e) => doc.setRepId(e.target.value)} slotProps={nativeSelect("Satış temsilcisi")} sx={{ minWidth: 200 }}>
                    <option value="">Carinin temsilcisi</option>
                    {doc.members.map((m) => (
                        <option key={m.userId} value={m.userId}>
                            {m.fullName}
                        </option>
                    ))}
                </TextField>
                <TextField
                    size="small"
                    label="Belge iskontosu %"
                    value={doc.docDiscount}
                    disabled={disabled}
                    onChange={(e) => doc.setDocDiscount(e.target.value)}
                    slotProps={{ htmlInput: { inputMode: "decimal" } }}
                    sx={{ width: 160 }}
                />
            </Stack>
            <TextField size="small" label="Not" value={doc.notes} disabled={disabled} onChange={(e) => doc.setNotes(e.target.value)} multiline minRows={2} slotProps={{ htmlInput: { maxLength: 1000 } }} fullWidth />
        </Stack>
    );
}
