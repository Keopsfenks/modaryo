"use client";

import { useState } from "react";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { DEFAULT_CARRIERS, type CarrierInfo, type ShippingSettings } from "@/lib/api/shipping";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

const TEMPLATE_ERROR = "Takip bağlantısı https ile başlamalı ve {no} içermelidir.";
const VARIABLES = ["{alici}", "{belgeNo}", "{kargo}", "{takipNo}", "{takipLink}"];

/** Boş şablon geçerlidir (takip bağlantısı üretilmez); doluysa https ile başlamalı ve {no} içermelidir. */
export function templateValid(template: string | null): boolean {
    const t = (template ?? "").trim();
    return t === "" || (t.startsWith("https://") && t.includes("{no}") && t.length <= 500);
}

/** Sevkiyat ayarları: toplama-paketleme akışı, müşteri e-postası (şablon ve değişkenler) ve kargo firmaları (etkinlik, takip şablonu). */
export default function ShippingSettingsForm() {
    const form = useSettingsForm<ShippingSettings>("sevkiyat");
    const [saved, setSaved] = useState(false);
    const [showErrors, setShowErrors] = useState(false);
    const value = form.value;

    const patch = (change: Partial<ShippingSettings>) => {
        setSaved(false);
        form.setValue((cur) => (cur ? { ...cur, ...change } : cur));
    };
    const patchCarrier = (code: string, change: Partial<CarrierInfo>) => {
        const list = (value?.carriers?.length ? value.carriers : DEFAULT_CARRIERS).map((c) => (c.code === code ? { ...c, ...change } : c));
        patch({ carriers: list });
    };

    const carriers = value ? (value.carriers?.length ? value.carriers : DEFAULT_CARRIERS) : [];
    const anyInvalid = carriers.some((c) => !templateValid(c.trackingUrlTemplate));

    const submit = async () => {
        setSaved(false);
        setShowErrors(true);
        if (anyInvalid) return;
        setSaved(await form.save());
    };

    return (
        <SettingsFormShell form={form} saved={saved} onSubmit={() => void submit()}>
            {value && (
                <Stack spacing={3} sx={{ maxWidth: 900 }}>
                    <Stack>
                        <FormControlLabel
                            control={<Switch checked={value.usePickPack} onChange={(e) => patch({ usePickPack: e.target.checked })} />}
                            label="Toplama ve paketleme akışını kullan"
                        />
                        <Typography variant="caption" color="text.secondary">
                            Kapalıyken sevkler taslaktan doğrudan sevk edilebilir.
                        </Typography>
                    </Stack>
                    <FormControlLabel
                        control={<Switch checked={value.notifyCustomerOnShip} onChange={(e) => patch({ notifyCustomerOnShip: e.target.checked })} />}
                        label="Sevk edilince müşteriye e-posta gönder"
                    />
                    <Stack spacing={2}>
                        <TextField label="E-posta konusu" value={value.shipEmailSubject} onChange={(e) => patch({ shipEmailSubject: e.target.value })} disabled={!value.notifyCustomerOnShip} slotProps={{ htmlInput: { maxLength: 200 } }} />
                        <TextField
                            label="E-posta metni"
                            multiline
                            minRows={5}
                            value={value.shipEmailBody}
                            onChange={(e) => patch({ shipEmailBody: e.target.value })}
                            disabled={!value.notifyCustomerOnShip}
                            helperText={`Kullanılabilir değişkenler: ${VARIABLES.join(" ")}`}
                            slotProps={{ htmlInput: { maxLength: 4000 } }}
                        />
                    </Stack>
                    <Stack spacing={1}>
                        <Typography variant="h6">Kargo firmaları</Typography>
                        <Typography variant="body2" color="text.secondary">
                            Takip bağlantısı şablonunda takip numarasının geleceği yere {"{no}"} yazın. Adresler firmaların herkese açık sayfalarına dayanır; firmalar değiştirebilir, canlıya almadan önce deneyin.
                        </Typography>
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 640 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Firma</TableCell>
                                        <TableCell>Etkin</TableCell>
                                        <TableCell sx={{ width: "60%" }}>Takip bağlantısı</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {carriers.map((c) => {
                                        const bad = showErrors && !templateValid(c.trackingUrlTemplate);
                                        return (
                                            <TableRow key={c.code}>
                                                <TableCell>{c.name}</TableCell>
                                                <TableCell>
                                                    <Switch checked={c.isActive} onChange={(e) => patchCarrier(c.code, { isActive: e.target.checked })} slotProps={{ input: { "aria-label": `${c.name} etkin` } }} />
                                                </TableCell>
                                                <TableCell>
                                                    {c.code === "kendi-arac" ? (
                                                        <Typography variant="body2" color="text.secondary">
                                                            Teslimat planıyla teslim edilir; takip numarası istenmez.
                                                        </Typography>
                                                    ) : (
                                                        <TextField
                                                            fullWidth
                                                            size="small"
                                                            placeholder="https://…?code={no}"
                                                            value={c.trackingUrlTemplate ?? ""}
                                                            onChange={(e) => patchCarrier(c.code, { trackingUrlTemplate: e.target.value === "" ? null : e.target.value })}
                                                            error={bad}
                                                            helperText={bad ? TEMPLATE_ERROR : undefined}
                                                            slotProps={{ htmlInput: { "aria-label": `${c.name} takip bağlantısı` } }}
                                                        />
                                                    )}
                                                </TableCell>
                                            </TableRow>
                                        );
                                    })}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    </Stack>
                </Stack>
            )}
        </SettingsFormShell>
    );
}
