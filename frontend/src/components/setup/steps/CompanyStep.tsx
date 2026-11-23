"use client";

import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import CompanyProfileForm from "@/components/settings/CompanyProfileForm";
import type { SetupStepProps } from "@/config/setupSteps";

/** Firma adımı: yalnızca unvan, VKN/TCKN, vergi dairesi ve adres. */
export default function CompanyStep({ onNext }: SetupStepProps) {
    return (
        <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">
                Faturalarda ve belgelerde görünecek temel firma bilgilerini girin.
            </Typography>
            <CompanyProfileForm
                fields={["legalName", "taxNumber", "taxOffice", "address"]}
                submitLabel="Kaydet ve devam et"
                onSaved={onNext}
            />
        </Stack>
    );
}
