"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import Box from "@mui/material/Box";
import CircularProgress from "@mui/material/CircularProgress";
import { ApiError, UNAUTHORIZED_EVENT } from "@/lib/api/client";
import { identity, type SessionInfo } from "@/lib/api/identity";
import { loginUrl } from "@/lib/navigation";

export type SessionState = {
    session: SessionInfo;
    refresh(): Promise<void>;
    setSession(session: SessionInfo): void;
    hasPermission(permission: string): boolean;
    switchTenant(tenantId: string): Promise<void>;
    logout(): Promise<void>;
};

const SessionContext = createContext<SessionState | null>(null);

function currentLocation() {
    return window.location.pathname + window.location.search;
}

/**
 * Oturumu kimlik servisinden yükler ve alt bileşenlere sağlar.
 * - Oturum yoksa (ya da herhangi bir API çağrısı 401 dönerse) → /giris?next=<şu anki adres>
 * - requireCompany açıkken aktif firma yoksa → /baslangic
 * Oturum hazır olana kadar çocukları render etmez.
 */
export function SessionProvider({ children, requireCompany = true }: { children: React.ReactNode; requireCompany?: boolean }) {
    const router = useRouter();
    const [session, setSession] = useState<SessionInfo | null>(null);

    const toLogin = useCallback(() => router.replace(loginUrl(currentLocation())), [router]);

    const apply = useCallback(
        (loaded: SessionInfo) => {
            if (requireCompany && !loaded.activeCompany) {
                router.replace("/baslangic");
                return;
            }
            setSession(loaded);
        },
        [requireCompany, router],
    );

    const fail = useCallback(
        (e: unknown) => {
            if (e instanceof ApiError && e.status === 401) {
                toLogin();
                return;
            }
            throw e;
        },
        [toLogin],
    );

    const load = useCallback(() => identity.getSession().then(apply, fail), [apply, fail]);

    useEffect(() => {
        let cancelled = false;
        identity.getSession().then(
            (loaded) => !cancelled && apply(loaded),
            (e: unknown) => !cancelled && fail(e),
        );
        return () => {
            cancelled = true;
        };
    }, [apply, fail]);

    useEffect(() => {
        const onUnauthorized = () => toLogin();
        window.addEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
        return () => window.removeEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
    }, [toLogin]);

    const value = useMemo<SessionState | null>(() => {
        if (!session) return null;
        return {
            session,
            setSession,
            refresh: load,
            hasPermission: (permission) => session.permissions.includes("*") || session.permissions.includes(permission),
            switchTenant: async (tenantId) => {
                setSession(await identity.switchTenant(tenantId));
                router.refresh();
            },
            logout: async () => {
                await identity.logout();
                router.replace("/giris");
            },
        };
    }, [session, load, router]);

    if (!value) {
        return (
            <Box sx={{ minHeight: "100vh", display: "grid", placeItems: "center" }} aria-busy="true">
                <CircularProgress aria-label="Oturum yükleniyor" />
            </Box>
        );
    }

    return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionState {
    const value = useContext(SessionContext);
    if (!value) {
        throw new Error("useSession yalnızca SessionProvider içinde kullanılabilir.");
    }
    return value;
}
