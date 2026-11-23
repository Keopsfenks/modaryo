"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { visibleCategories } from "@/config/settings";
import { useSession } from "@/session/SessionProvider";
import NoAccess from "./NoAccess";

/** /ayarlar → kullanıcının görebildiği ilk kategoriye yönlendirir. */
export default function SettingsIndex() {
    const router = useRouter();
    const session = useSession();
    const first = visibleCategories(session)[0];

    useEffect(() => {
        if (first) {
            router.replace(first.href);
        }
    }, [first, router]);

    return first ? null : <NoAccess />;
}
