"use client";

import { useMemo, useState } from "react";
import Button from "@mui/material/Button";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Step from "@mui/material/Step";
import StepLabel from "@mui/material/StepLabel";
import Stepper from "@mui/material/Stepper";
import Typography from "@mui/material/Typography";
import { setupSteps, type SetupStep } from "@/config/setupSteps";
import { useSession } from "@/session/SessionProvider";

/** Kurulum sihirbazı: adımlar order alanına göre sıralanır; module belirtilen adım yalnızca o modül açıksa görünür. */
export default function SetupWizard({ steps = setupSteps }: { steps?: SetupStep[] }) {
    const { hasModule } = useSession();
    const [index, setIndex] = useState(0);
    const ordered = useMemo(
        () => steps.filter((s) => !s.module || hasModule(s.module)).sort((a, b) => a.order - b.order),
        [steps, hasModule],
    );
    const last = ordered.length - 1;
    const current = ordered[Math.min(index, last)];
    if (!current) {
        return null;
    }
    const { Component } = current;
    const next = () => setIndex((i) => Math.min(i + 1, last));
    const back = () => setIndex((i) => Math.max(i - 1, 0));

    return (
        <Stack spacing={3}>
            <Typography variant="h4">Firma kurulumu</Typography>
            <Stepper activeStep={index} alternativeLabel>
                {ordered.map((s) => (
                    <Step key={s.key}>
                        <StepLabel>
                            <span data-testid="setup-step-label">{s.title}</span>
                        </StepLabel>
                    </Step>
                ))}
            </Stepper>
            <Paper variant="outlined" sx={{ p: 3 }}>
                <Component onNext={next} onBack={back} />
            </Paper>
            {!current.ownNavigation && (
                <Stack direction="row" spacing={1}>
                    <Button disabled={index === 0} onClick={back}>
                        Geri
                    </Button>
                    <Button variant="contained" disabled={index >= last} onClick={next}>
                        İleri
                    </Button>
                </Stack>
            )}
        </Stack>
    );
}
