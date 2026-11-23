"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import { erp } from "@/lib/api/erp";
import { useSession } from "@/session/SessionProvider";
import { patchSetup, type SetupSettings } from "./setupSettings";

/** Kurulum tamamlanmadı/ertelenmediyse (ve ayarlar.firma izni varsa) panelin üstünde görünen davet. Zorunlu yönlendirme yoktur. */
export default function SetupBanner() {
    const { hasPermission } = useSession();
    const allowed = hasPermission("ayarlar.firma");
    const [visible, setVisible] = useState(false);

    useEffect(() => {
        if (!allowed) {
            return;
        }
        let active = true;
        erp.settings.get<SetupSettings>("kurulum").then(
            (dto) => active && setVisible(!dto.value.completedAt && !dto.value.dismissedAt),
            () => undefined,
        );
        return () => {
            active = false;
        };
    }, [allowed]);

    if (!allowed || !visible) {
        return null;
    }

    const dismiss = async () => {
        setVisible(false);
        try {
            await patchSetup({ dismissedAt: new Date().toISOString() });
        } catch {
            setVisible(true);
        }
    };

    return (
        <Alert
            severity="info"
            sx={{ mb: 3 }}
            action={
                <>
                    <Button color="inherit" size="small" href="/kurulum">
                        Kuruluma başla
                    </Button>
                    <Button color="inherit" size="small" onClick={() => void dismiss()}>
                        Daha sonra
                    </Button>
                </>
            }
        >
            Firmanızı birkaç adımda kuralım
        </Alert>
    );
}
