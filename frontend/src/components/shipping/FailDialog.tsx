"use client";

import ReasonDialog from "@/components/purchasing/ReasonDialog";

type FailDialogProps = {
    open: boolean;
    onClose(): void;
    onConfirm(reason: string): Promise<void>;
};

/** Teslimat başarısız: gerekçe ister; sevk "Teslim edilemedi" durumuna geçer. */
export default function FailDialog({ open, onClose, onConfirm }: FailDialogProps) {
    return (
        <ReasonDialog
            open={open}
            title="Teslimat başarısız"
            text="Teslim edilemeyen sevk yeniden gönderilebilir ya da iade alınabilir. Gerekçeyi yazın."
            confirmLabel="Başarısız işaretle"
            onClose={onClose}
            onConfirm={onConfirm}
        />
    );
}
