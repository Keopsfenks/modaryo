"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiError } from "@/lib/api/client";
import { erp } from "@/lib/api/erp";
import { errorMessage } from "@/lib/api/errorMessages";

/** Bir ayar kategorisini yükler, düzenletir ve sürümüyle (iyimser kilit) kaydeder. 409'da girilen değer korunur. */
export function useSettingsForm<T>(category: string) {
    const [value, setValue] = useState<T | null>(null);
    const [version, setVersion] = useState(0);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [conflict, setConflict] = useState(false);

    const reload = useCallback(async () => {
        setLoading(true);
        setError(null);
        setConflict(false);
        try {
            const dto = await erp.settings.get<T>(category);
            setValue(dto.value);
            setVersion(dto.version);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setLoading(false);
        }
    }, [category]);

    useEffect(() => {
        let active = true;
        erp.settings.get<T>(category).then(
            (dto) => {
                if (active) {
                    setValue(dto.value);
                    setVersion(dto.version);
                    setLoading(false);
                }
            },
            (e) => {
                if (active) {
                    setError(errorMessage(e));
                    setLoading(false);
                }
            },
        );
        return () => {
            active = false;
        };
    }, [category]);

    const save = useCallback(async () => {
        if (value === null) {
            return false;
        }
        setSaving(true);
        setError(null);
        setConflict(false);
        try {
            const dto = await erp.settings.put<T>(category, version, value);
            setValue(dto.value);
            setVersion(dto.version);
            return true;
        } catch (e) {
            setError(errorMessage(e));
            setConflict(e instanceof ApiError && e.code === "version_conflict");
            return false;
        } finally {
            setSaving(false);
        }
    }, [category, value, version]);

    return { value, version, loading, saving, error, conflict, setValue, save, reload };
}
