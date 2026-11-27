"use client";

import { useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Divider from "@mui/material/Divider";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormHelperText from "@mui/material/FormHelperText";
import FormLabel from "@mui/material/FormLabel";
import Grid from "@mui/material/Grid";
import MenuItem from "@mui/material/MenuItem";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import {
    erp,
    type AccountKind,
    type AccountRole,
    type AddressInput,
    type CreateAccountInput,
    type CurrentAccountDto,
    type EntryDirection,
    type OpeningBalanceInput,
} from "@/lib/api/erp";
import { identity, type MemberDto } from "@/lib/api/identity";
import { pricing, type CustomerGroupDto } from "@/lib/api/pricing";
import { parseAmount, type Currency } from "@/lib/money";
import { isValidTckn, isValidVkn } from "@/lib/taxNumber";
import CurrencySelect from "./CurrencySelect";

const MAX_MONEY = 999_999_999_999.99;

type FormValues = {
    code: string;
    name: string;
    kind: AccountKind;
    taxNumber: string;
    taxOffice: string;
    isEInvoiceTaxpayer: boolean;
    isCustomer: boolean;
    isSupplier: boolean;
    phone: string;
    email: string;
    notes: string;
    currency: Currency;
    defaultDueDays: string;
    creditLimit: string;
    customerGroupId: string;
    salesRepUserId: string;
    addressTitle: string;
    addressLine: string;
    addressDistrict: string;
    addressCity: string;
    addressPostalCode: string;
    addressCountry: string;
    openingAmount: string;
    openingDirection: EntryDirection;
    openingDate: string;
};

type Errors = Partial<Record<string, string>>;

type AccountFormProps = {
    /** Verilirse düzenleme kipi: ilk adres ve açılış bakiyesi bölümleri yoktur. */
    initial?: CurrentAccountDto;
    /** Yeni cari için önceden işaretlenecek rol. */
    defaultRole?: AccountRole;
    onSaved: (dto: CurrentAccountDto) => void;
};

function today(): string {
    const d = new Date();
    const pad = (n: number) => String(n).padStart(2, "0");
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function initialValues(initial: CurrentAccountDto | undefined, defaultRole: AccountRole | undefined): FormValues {
    return {
        code: initial?.code ?? "",
        name: initial?.name ?? "",
        kind: initial?.kind ?? "corporate",
        taxNumber: initial?.taxNumber ?? "",
        taxOffice: initial?.taxOffice ?? "",
        isEInvoiceTaxpayer: initial?.isEInvoiceTaxpayer ?? false,
        isCustomer: initial ? initial.isCustomer : defaultRole === "customer",
        isSupplier: initial ? initial.isSupplier : defaultRole === "supplier",
        phone: initial?.phone ?? "",
        email: initial?.email ?? "",
        notes: initial?.notes ?? "",
        currency: initial?.currency ?? "TRY",
        defaultDueDays: String(initial?.defaultDueDays ?? 30),
        creditLimit: initial?.creditLimit == null ? "" : String(initial.creditLimit).replace(".", ","),
        customerGroupId: initial?.customerGroupId ?? "",
        salesRepUserId: initial?.salesRepUserId ?? "",
        addressTitle: "",
        addressLine: "",
        addressDistrict: "",
        addressCity: "",
        addressPostalCode: "",
        addressCountry: "Türkiye",
        openingAmount: "",
        openingDirection: "debit",
        openingDate: today(),
    };
}

/** Boşsa null, geçersizse NaN, değilse tutar. Ayrıştırma kuralları lib/money.parseAmount'ta. */
function optionalAmount(text: string): number | null {
    if (!text.trim()) return null;
    return parseAmount(text) ?? Number.NaN;
}

const blankToNull = (text: string) => text.trim() || null;

function validate(v: FormValues, creating: boolean): Errors {
    const errors: Errors = {};
    const code = v.code.trim();
    const name = v.name.trim();

    if (code.length < 1 || code.length > 20) errors.code = "Kod 1 ile 20 karakter arasında olmalı.";
    if (name.length < 2 || name.length > 200) errors.name = "Ad 2 ile 200 karakter arasında olmalı.";
    if (!v.isCustomer && !v.isSupplier) errors.role = "En az biri seçilmeli: Müşteri veya Tedarikçi.";

    const tax = v.taxNumber.trim();
    if (tax) {
        if (v.kind === "corporate" && !isValidVkn(tax)) errors.taxNumber = "Geçerli bir VKN girin (10 hane).";
        if (v.kind === "individual" && !isValidTckn(tax)) errors.taxNumber = "Geçerli bir TCKN girin (11 hane).";
    }

    const dueDays = v.defaultDueDays.trim();
    if (!/^\d+$/.test(dueDays) || Number(dueDays) > 365) errors.defaultDueDays = "Vade günü 0 ile 365 arasında olmalı.";

    const limit = optionalAmount(v.creditLimit);
    if (limit !== null && (Number.isNaN(limit) || limit > MAX_MONEY)) errors.creditLimit = "Kredi limiti 0 veya daha büyük bir tutar olmalı (en çok 2 ondalık). Örn. 1.500,00";

    if (creating) {
        const hasAddress = [v.addressTitle, v.addressLine, v.addressDistrict, v.addressCity, v.addressPostalCode].some((x) => x.trim());
        if (hasAddress) {
            const title = v.addressTitle.trim();
            const line = v.addressLine.trim();
            if (title.length < 1 || title.length > 100) errors.addressTitle = "Adres başlığı 1 ile 100 karakter arasında olmalı.";
            if (line.length < 1 || line.length > 500) errors.addressLine = "Adres 1 ile 500 karakter arasında olmalı.";
        }

        const amount = optionalAmount(v.openingAmount);
        if (amount !== null) {
            if (Number.isNaN(amount) || amount <= 0 || amount > MAX_MONEY) {
                errors.openingAmount = "Tutar 0'dan büyük olmalı (en çok 2 ondalık, 999.999.999.999,99'a kadar). Örn. 1.500,00";
            }
            if (!v.openingDate) errors.openingDate = "Açılış tarihi seçin.";
        }
    }

    return errors;
}

function buildBody(v: FormValues, creating: boolean): CreateAccountInput {
    const body: CreateAccountInput = {
        code: v.code.trim(),
        name: v.name.trim(),
        kind: v.kind,
        taxNumber: blankToNull(v.taxNumber),
        taxOffice: blankToNull(v.taxOffice),
        isEInvoiceTaxpayer: v.isEInvoiceTaxpayer,
        isCustomer: v.isCustomer,
        isSupplier: v.isSupplier,
        phone: blankToNull(v.phone),
        email: blankToNull(v.email),
        notes: blankToNull(v.notes),
        currency: v.currency,
        defaultDueDays: Number(v.defaultDueDays.trim()),
        creditLimit: optionalAmount(v.creditLimit),
        customerGroupId: v.customerGroupId || null,
    };
    if (!creating) return body;

    const hasAddress = [v.addressTitle, v.addressLine, v.addressDistrict, v.addressCity, v.addressPostalCode].some((x) => x.trim());
    const address: AddressInput | null = hasAddress
        ? {
              title: v.addressTitle.trim(),
              line: v.addressLine.trim(),
              district: blankToNull(v.addressDistrict),
              city: blankToNull(v.addressCity),
              postalCode: blankToNull(v.addressPostalCode),
              country: v.addressCountry.trim() || "Türkiye",
              isBilling: true,
              isShipping: true,
              isDefaultBilling: true,
              isDefaultShipping: true,
          }
        : null;

    const amount = optionalAmount(v.openingAmount);
    const openingBalance: OpeningBalanceInput | null =
        amount === null ? null : { amount, direction: v.openingDirection, date: v.openingDate };

    return { ...body, address, openingBalance };
}

/**
 * Cari kart formu; yeni ve düzenle için ortak. Yeni carideyse kod alanı next-code ile doldurulur
 * ve isteğe bağlı ilk adres ile açılış bakiyesi bölümleri görünür. Kaydedilen kart onSaved ile döner.
 */
export default function AccountForm({ initial, defaultRole, onSaved }: AccountFormProps) {
    const creating = !initial;
    const [v, setV] = useState<FormValues>(() => initialValues(initial, defaultRole));
    const [errors, setErrors] = useState<Errors>({});
    const [alert, setAlert] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);
    const [groups, setGroups] = useState<CustomerGroupDto[]>([]);
    const [members, setMembers] = useState<MemberDto[]>([]);

    useEffect(() => {
        // fiyat.read yoksa uç 403 döner: alan gösterilmez, mevcut grup değeri değişmeden korunur.
        pricing.customerGroups().then(setGroups, () => undefined);
        // Satış temsilcisi seçenekleri kimlik servisinin üye listesinden gelir; yetki yoksa alan gösterilmez.
        identity.getMembers().then(setMembers, () => undefined);
    }, []);

    useEffect(() => {
        if (!creating) return;
        let cancelled = false;
        erp.nextCode().then(
            ({ code }) => {
                if (!cancelled) setV((prev) => (prev.code ? prev : { ...prev, code }));
            },
            () => {
                // Öneri alınamazsa kod elle girilir.
            },
        );
        return () => {
            cancelled = true;
        };
    }, [creating]);

    const set = <K extends keyof FormValues>(key: K, value: FormValues[K]) => {
        setV((prev) => ({ ...prev, [key]: value }));
    };
    const text = (key: keyof FormValues & string, label: string, extra: Partial<React.ComponentProps<typeof TextField>> = {}) => (
        <TextField
            id={key}
            label={label}
            fullWidth
            value={v[key] as string}
            onChange={(e) => set(key, e.target.value as never)}
            error={Boolean(errors[key])}
            helperText={errors[key]}
            {...extra}
        />
    );

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (busy) return;

        const found = validate(v, creating);
        setErrors(found);
        setAlert(null);
        if (Object.keys(found).length > 0) return;

        setBusy(true);
        try {
            const body = { ...buildBody(v, creating), ...(members.length > 0 ? { salesRepUserId: v.salesRepUserId || null } : {}) };
            const saved = initial
                ? await erp.updateAccount(initial.id, {
                      code: body.code,
                      name: body.name,
                      kind: body.kind,
                      taxNumber: body.taxNumber,
                      taxOffice: body.taxOffice,
                      isEInvoiceTaxpayer: body.isEInvoiceTaxpayer,
                      isCustomer: body.isCustomer,
                      isSupplier: body.isSupplier,
                      phone: body.phone,
                      email: body.email,
                      notes: body.notes,
                      currency: body.currency,
                      defaultDueDays: body.defaultDueDays,
                      creditLimit: body.creditLimit,
                      customerGroupId: body.customerGroupId,
                      ...(members.length > 0 ? { salesRepUserId: body.salesRepUserId } : {}),
                  })
                : await erp.createAccount(body);
            onSaved(saved);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            await showServerError(e);
        } finally {
            setBusy(false);
        }
    };

    const showServerError = async (e: ApiError) => {
        if (e.code === "code_taken") {
            let message = "Bu kod kullanılıyor.";
            try {
                const { code } = await erp.nextCode();
                message += ` Önerilen kod: ${code}`;
            } catch {
                // Öneri alınamazsa yalnızca uyarı gösterilir.
            }
            setErrors({ code: message });
        } else if (e.code === "tax_number_taken") {
            setErrors({ taxNumber: "Bu numara başka bir cari kartında kayıtlı." });
        } else if (e.code === "currency_locked") {
            setErrors({ currency: "Hareketi olan carinin para birimi değiştirilemez." });
        } else {
            setErrors(e.fieldErrors);
            setAlert(e);
        }
    };

    const taxLabel = v.kind === "corporate" ? "VKN" : "TCKN";

    return (
        <Box component="form" noValidate onSubmit={onSubmit}>
            <FormAlert error={alert} />
            <Grid container spacing={2.5}>
                <Grid size={{ xs: 12, sm: 4 }}>{text("code", "Kod")}</Grid>
                <Grid size={{ xs: 12, sm: 8 }}>{text("name", "Ad / Unvan")}</Grid>

                <Grid size={{ xs: 12, sm: 6 }}>
                    <FormControl>
                        <FormLabel id="kind-label">Tür</FormLabel>
                        <RadioGroup row aria-labelledby="kind-label" value={v.kind} onChange={(e) => set("kind", e.target.value as AccountKind)}>
                            <FormControlLabel value="corporate" control={<Radio />} label="Kurum" />
                            <FormControlLabel value="individual" control={<Radio />} label="Şahıs" />
                        </RadioGroup>
                    </FormControl>
                </Grid>
                <Grid size={{ xs: 12, sm: 6 }}>
                    <FormControl error={Boolean(errors.role)}>
                        <FormLabel id="role-label">Rol</FormLabel>
                        <Box role="group" aria-labelledby="role-label" sx={{ display: "flex" }}>
                            <FormControlLabel
                                control={<Checkbox checked={v.isCustomer} onChange={(e) => set("isCustomer", e.target.checked)} />}
                                label="Müşteri"
                            />
                            <FormControlLabel
                                control={<Checkbox checked={v.isSupplier} onChange={(e) => set("isSupplier", e.target.checked)} />}
                                label="Tedarikçi"
                            />
                        </Box>
                        {errors.role && <FormHelperText id="role-helper-text">{errors.role}</FormHelperText>}
                    </FormControl>
                </Grid>

                <Grid size={{ xs: 12, sm: 6 }}>{text("taxNumber", taxLabel, { slotProps: { htmlInput: { inputMode: "numeric" } } })}</Grid>
                <Grid size={{ xs: 12, sm: 6 }}>{text("taxOffice", "Vergi dairesi")}</Grid>
                <Grid size={12}>
                    <FormControlLabel
                        control={<Switch checked={v.isEInvoiceTaxpayer} onChange={(e) => set("isEInvoiceTaxpayer", e.target.checked)} />}
                        label="e-Fatura mükellefi"
                    />
                </Grid>

                <Grid size={{ xs: 12, sm: 6 }}>{text("phone", "Telefon")}</Grid>
                <Grid size={{ xs: 12, sm: 6 }}>{text("email", "E-posta", { type: "email" })}</Grid>

                <Grid size={{ xs: 12, sm: 4 }}>
                    <CurrencySelect
                        value={v.currency}
                        onChange={(c) => set("currency", c)}
                        disabled={Boolean(initial?.hasMovements)}
                        error={errors.currency}
                        helperText={initial?.hasMovements ? "Hareketi olan carinin para birimi değiştirilemez." : undefined}
                    />
                </Grid>
                <Grid size={{ xs: 12, sm: 4 }}>{text("defaultDueDays", "Varsayılan vade (gün)", { slotProps: { htmlInput: { inputMode: "numeric" } } })}</Grid>
                <Grid size={{ xs: 12, sm: 4 }}>{text("creditLimit", "Kredi limiti", { slotProps: { htmlInput: { inputMode: "decimal" } } })}</Grid>

                {v.isCustomer && groups.length > 0 && (
                    <Grid size={{ xs: 12, sm: 4 }}>
                        <TextField select id="customerGroupId" label="Müşteri grubu" fullWidth value={v.customerGroupId} onChange={(e) => set("customerGroupId", e.target.value)}>
                            <MenuItem value="">Grup yok</MenuItem>
                            {groups.map((g) => (
                                <MenuItem key={g.id} value={g.id}>
                                    {g.name}
                                </MenuItem>
                            ))}
                        </TextField>
                    </Grid>
                )}

                {v.isCustomer && members.length > 0 && (
                    <Grid size={{ xs: 12, sm: 4 }}>
                        <TextField select id="salesRepUserId" label="Satış temsilcisi" fullWidth value={v.salesRepUserId} onChange={(e) => set("salesRepUserId", e.target.value)}>
                            <MenuItem value="">Temsilci yok</MenuItem>
                            {members.map((m) => (
                                <MenuItem key={m.userId} value={m.userId}>
                                    {m.fullName}
                                </MenuItem>
                            ))}
                        </TextField>
                    </Grid>
                )}

                <Grid size={12}>{text("notes", "Notlar", { multiline: true, minRows: 2 })}</Grid>

                {creating && (
                    <>
                        <Grid size={12}>
                            <Divider sx={{ my: 1 }} />
                            <Typography variant="h6">İlk adres (isteğe bağlı)</Typography>
                        </Grid>
                        <Grid size={{ xs: 12, sm: 4 }}>{text("addressTitle", "Adres başlığı")}</Grid>
                        <Grid size={{ xs: 12, sm: 8 }}>{text("addressLine", "Adres")}</Grid>
                        <Grid size={{ xs: 12, sm: 3 }}>{text("addressDistrict", "İlçe")}</Grid>
                        <Grid size={{ xs: 12, sm: 3 }}>{text("addressCity", "İl")}</Grid>
                        <Grid size={{ xs: 12, sm: 3 }}>{text("addressPostalCode", "Posta kodu")}</Grid>
                        <Grid size={{ xs: 12, sm: 3 }}>{text("addressCountry", "Ülke")}</Grid>

                        <Grid size={12}>
                            <Divider sx={{ my: 1 }} />
                            <Typography variant="h6">Açılış bakiyesi (isteğe bağlı)</Typography>
                        </Grid>
                        <Grid size={{ xs: 12, sm: 4 }}>{text("openingAmount", "Açılış tutarı", { slotProps: { htmlInput: { inputMode: "decimal" } } })}</Grid>
                        <Grid size={{ xs: 12, sm: 4 }}>
                            <TextField
                                select
                                id="openingDirection"
                                label="Açılış yönü"
                                fullWidth
                                value={v.openingDirection}
                                onChange={(e) => set("openingDirection", e.target.value as EntryDirection)}
                            >
                                <MenuItem value="debit">Borç</MenuItem>
                                <MenuItem value="credit">Alacak</MenuItem>
                            </TextField>
                        </Grid>
                        <Grid size={{ xs: 12, sm: 4 }}>
                            {text("openingDate", "Açılış tarihi", { type: "date", slotProps: { inputLabel: { shrink: true } } })}
                        </Grid>
                    </>
                )}
            </Grid>
            <Box sx={{ mt: 3, display: "flex", justifyContent: "flex-end" }}>
                <Button type="submit" variant="contained" disabled={busy}>
                    Kaydet
                </Button>
            </Box>
        </Box>
    );
}
