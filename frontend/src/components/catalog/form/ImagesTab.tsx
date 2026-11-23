"use client";

import { useRef, useState } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { IconArrowLeft, IconArrowRight, IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import { errorMessage } from "@/lib/api/errorMessages";
import { erp } from "@/lib/api/erp";
import { MAX_IMAGES } from "./model";

type ImagesTabProps = {
    fileIds: string[];
    onChange: (fileIds: string[]) => void;
};

/** Görseller sekmesi: yükleme (P0b dosyaları, tür "image"), sıralama ve silme. İlk görsel kapak görselidir. */
export default function ImagesTab({ fileIds, onChange }: ImagesTabProps) {
    const input = useRef<HTMLInputElement>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    async function upload(files: FileList | null) {
        if (!files || files.length === 0) return;
        setBusy(true);
        setError(null);
        const added: string[] = [];
        try {
            for (const file of Array.from(files).slice(0, MAX_IMAGES - fileIds.length)) {
                added.push((await erp.files.upload(file, "image")).id);
            }
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            if (added.length > 0) onChange([...fileIds, ...added]);
            setBusy(false);
            if (input.current) input.current.value = "";
        }
    }

    const move = (index: number, delta: number) => {
        const next = [...fileIds];
        [next[index], next[index + delta]] = [next[index + delta], next[index]];
        onChange(next);
    };

    return (
        <Stack spacing={2}>
            <FormAlert error={error} />
            <Typography color="text.secondary">En fazla {MAX_IMAGES} görsel; ilk görsel liste ve aramalarda kapak olarak görünür.</Typography>
            <Box sx={{ display: "flex", gap: 2, flexWrap: "wrap" }}>
                {fileIds.map((id, i) => (
                    <Box key={id} sx={{ width: 140 }}>
                        <Box component="img" src={erp.files.url(id)} alt={`Görsel ${i + 1}`} sx={{ width: 140, height: 140, objectFit: "cover", borderRadius: 1, border: 1, borderColor: "divider" }} />
                        <Box sx={{ display: "flex", justifyContent: "space-between" }}>
                            <IconButton size="small" aria-label={`Görsel ${i + 1} sola taşı`} disabled={i === 0} onClick={() => move(i, -1)}>
                                <IconArrowLeft size={16} />
                            </IconButton>
                            <IconButton size="small" aria-label={`Görsel ${i + 1} sil`} onClick={() => onChange(fileIds.filter((_, j) => j !== i))}>
                                <IconTrash size={16} />
                            </IconButton>
                            <IconButton size="small" aria-label={`Görsel ${i + 1} sağa taşı`} disabled={i === fileIds.length - 1} onClick={() => move(i, 1)}>
                                <IconArrowRight size={16} />
                            </IconButton>
                        </Box>
                    </Box>
                ))}
            </Box>
            <input ref={input} type="file" accept="image/*" multiple hidden aria-label="Görsel dosyası" onChange={(e) => void upload(e.target.files)} />
            <div>
                <Button variant="outlined" disabled={busy || fileIds.length >= MAX_IMAGES} onClick={() => input.current?.click()}>
                    {busy ? "Yükleniyor..." : "Görsel yükle"}
                </Button>
            </div>
        </Stack>
    );
}
