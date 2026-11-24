"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import PdfPreview from "@/components/invoicing/PdfPreview";
import { invoicing, type DocumentSettings } from "@/lib/api/invoicing";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

const HEX = /^#[0-9A-Fa-f]{6}$/;

/** Belge ayarları: vurgu rengi, alt not, IBAN gösterimi, e-posta şablonları ve kayıtlı ayarlarla üretilen örnek fatura önizlemesi. */
export default function DocumentSettingsForm() {
    const form = useSettingsForm<DocumentSettings>("belgeler");
    const [saved, setSaved] = useState(false);
    const value = form.value;
    const patch = (change: Partial<DocumentSettings>) => form.setValue((current) => (current ? { ...current, ...change } : current));

    const submit = async () => {
        setSaved(false);
        if (value && !HEX.test(value.accentColor)) return;
        setSaved(await form.save());
    };

    return (
        <Stack spacing={3}>
            <SettingsFormShell form={form} saved={saved} onSubmit={() => void submit()}>
                {value && (
                    <>
                        <Stack direction="row" spacing={2} sx={{ alignItems: "flex-start" }}>
                            <input
                                type="color"
                                aria-label="Vurgu rengi seçici"
                                value={HEX.test(value.accentColor) ? value.accentColor : "#5D87FF"}
                                onChange={(e) => patch({ accentColor: e.target.value.toUpperCase() })}
                                style={{ width: 56, height: 40, border: 0, padding: 0, background: "none", cursor: "pointer" }}
                            />
                            <TextField
                                size="small"
                                label="Vurgu rengi"
                                value={value.accentColor}
                                onChange={(e) => patch({ accentColor: e.target.value })}
                                error={!HEX.test(value.accentColor) || !!form.fieldErrors.accentColor}
                                helperText={form.fieldErrors.accentColor ?? (!HEX.test(value.accentColor) ? "#RRGGBB biçiminde olmalıdır." : "PDF başlık ve tablo rengi")}
                                sx={{ width: 220 }}
                            />
                        </Stack>
                        <TextField
                            label="Alt not"
                            value={value.footerNote ?? ""}
                            onChange={(e) => patch({ footerNote: e.target.value || null })}
                            error={!!form.fieldErrors.footerNote}
                            helperText={form.fieldErrors.footerNote ?? "Her sayfanın altında görünür (en fazla 500 karakter)."}
                            multiline
                            minRows={2}
                            slotProps={{ htmlInput: { maxLength: 500 } }}
                        />
                        <FormControlLabel control={<Switch checked={value.showIbans} onChange={(e) => patch({ showIbans: e.target.checked })} />} label="Firma IBAN'larını satış faturalarında göster" />
                        <Alert severity="info" icon={false}>
                            <Typography variant="subtitle2">E-posta şablon değişkenleri</Typography>
                            <Typography variant="body2">
                                <code>{"{firma}"}</code> firma unvanı, <code>{"{belgeNo}"}</code> fatura numarası, <code>{"{alici}"}</code> cari adı, <code>{"{tutar}"}</code> ödenecek tutar, <code>{"{vade}"}</code> vade tarihi.
                            </Typography>
                        </Alert>
                        <TextField
                            label="Fatura e-postası konusu"
                            value={value.invoiceEmailSubject}
                            onChange={(e) => patch({ invoiceEmailSubject: e.target.value })}
                            error={!!form.fieldErrors.invoiceEmailSubject}
                            helperText={form.fieldErrors.invoiceEmailSubject}
                            slotProps={{ htmlInput: { maxLength: 200 } }}
                        />
                        <TextField
                            label="Fatura e-postası metni"
                            value={value.invoiceEmailBody}
                            onChange={(e) => patch({ invoiceEmailBody: e.target.value })}
                            error={!!form.fieldErrors.invoiceEmailBody}
                            helperText={form.fieldErrors.invoiceEmailBody}
                            multiline
                            minRows={3}
                            slotProps={{ htmlInput: { maxLength: 2000 } }}
                        />
                    </>
                )}
            </SettingsFormShell>
            {!form.loading && (
                <div>
                    <Typography variant="h6" sx={{ mb: 1 }}>
                        Örnek fatura önizlemesi
                    </Typography>
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                        Önizleme kaydedilmiş ayarlarla üretilir; değişiklikleri görmek için kaydedin.
                    </Typography>
                    <PdfPreview title="Örnek fatura" load={() => invoicing.samplePdf()} refreshKey={form.version} height={720} />
                </div>
            )}
        </Stack>
    );
}
