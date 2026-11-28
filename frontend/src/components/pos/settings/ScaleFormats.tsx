"use client";

import { useState } from "react";
import { IconPlus, IconTrash } from "@tabler/icons-react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { parseScaleBarcode, SCALE_KIND_LABELS, type ScaleBarcodeSetting, type ScaleValueKind } from "@/lib/api/pos";

const fmt = (n: number, max = 3) => n.toLocaleString("tr-TR", { maximumFractionDigits: max, minimumFractionDigits: 0 });
const REASONS = { length: "Barkod 13 haneli olmalıdır.", noFormat: "Hiçbir biçimin önekine uymuyor.", checkDigit: "Kontrol hanesi hatalı." } as const;

/** Terazi barkod biçimleri (önek, kalem kodu, değer) ve canlı deneme alanı: yapıştırılan barkodun çözümü anında görünür. */
export default function ScaleFormats({ value, onChange }: { value: ScaleBarcodeSetting[]; onChange: (next: ScaleBarcodeSetting[]) => void }) {
    const [sample, setSample] = useState("");
    const patch = (index: number, change: Partial<ScaleBarcodeSetting>) => onChange(value.map((f, i) => (i === index ? { ...f, ...change } : f)));
    const num = (text: string) => Math.max(0, Math.min(11, Number(text.replace(/\D/g, "")) || 0));
    const parsed = sample.trim() === "" ? null : parseScaleBarcode(sample, value);

    return (
        <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">
                Biçim: önek + kalem kodu + değer + kontrol hanesi; toplam 13 hane. Kalem kodu, ürün kodu ya da birim barkodu ile eşleşir.
            </Typography>
            <TableContainer>
                <Table size="small" sx={{ minWidth: 640 }}>
                    <TableHead>
                        <TableRow>
                            <TableCell>Önek</TableCell>
                            <TableCell>Kalem kodu hane</TableCell>
                            <TableCell>Değer hane</TableCell>
                            <TableCell>Değer türü</TableCell>
                            <TableCell />
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {value.map((f, i) => {
                            const bad = f.prefix.length + f.itemCodeLength + f.valueLength + 1 !== 13;
                            return (
                                <TableRow key={i}>
                                    <TableCell>
                                        <TextField size="small" value={f.prefix} onChange={(e) => patch(i, { prefix: e.target.value.replace(/\D/g, "").slice(0, 6) })} slotProps={{ htmlInput: { "aria-label": `Önek ${i + 1}`, inputMode: "numeric" } }} sx={{ width: 90 }} />
                                    </TableCell>
                                    <TableCell>
                                        <TextField size="small" value={f.itemCodeLength} error={bad} onChange={(e) => patch(i, { itemCodeLength: num(e.target.value) })} slotProps={{ htmlInput: { "aria-label": `Kalem kodu hane ${i + 1}`, inputMode: "numeric" } }} sx={{ width: 90 }} />
                                    </TableCell>
                                    <TableCell>
                                        <TextField size="small" value={f.valueLength} error={bad} onChange={(e) => patch(i, { valueLength: num(e.target.value) })} slotProps={{ htmlInput: { "aria-label": `Değer hane ${i + 1}`, inputMode: "numeric" } }} sx={{ width: 90 }} />
                                    </TableCell>
                                    <TableCell>
                                        <TextField select size="small" value={f.valueKind} onChange={(e) => patch(i, { valueKind: e.target.value as ScaleValueKind })} sx={{ minWidth: 180 }}>
                                            {(Object.keys(SCALE_KIND_LABELS) as ScaleValueKind[]).map((k) => (
                                                <MenuItem key={k} value={k}>
                                                    {SCALE_KIND_LABELS[k]}
                                                </MenuItem>
                                            ))}
                                        </TextField>
                                        {bad && (
                                            <Typography variant="caption" color="error" sx={{ display: "block" }}>
                                                Önek + kalem + değer + 1 = 13 olmalı.
                                            </Typography>
                                        )}
                                    </TableCell>
                                    <TableCell align="right">
                                        <IconButton aria-label={`Biçimi sil ${i + 1}`} onClick={() => onChange(value.filter((_, j) => j !== i))}>
                                            <IconTrash size={18} />
                                        </IconButton>
                                    </TableCell>
                                </TableRow>
                            );
                        })}
                    </TableBody>
                </Table>
            </TableContainer>
            <div>
                <Button startIcon={<IconPlus size={18} />} onClick={() => onChange([...value, { prefix: "", itemCodeLength: 5, valueLength: 5, valueKind: "weightGrams" }])}>
                    Biçim ekle
                </Button>
            </div>
            <Stack spacing={1} sx={{ maxWidth: 480 }}>
                <TextField label="Denenecek barkod" value={sample} onChange={(e) => setSample(e.target.value.replace(/\s/g, ""))} slotProps={{ htmlInput: { inputMode: "numeric", maxLength: 13 } }} />
                {parsed &&
                    (parsed.ok ? (
                        <Alert severity="success">
                            Önek {parsed.prefix} · Kalem kodu: {parsed.itemCode} · {parsed.weightKg !== null ? `${fmt(parsed.weightKg)} kg` : `${fmt(parsed.priceTry ?? 0, 2)} ₺`}
                        </Alert>
                    ) : (
                        <Alert severity="warning">{REASONS[parsed.reason]}</Alert>
                    ))}
            </Stack>
        </Stack>
    );
}
