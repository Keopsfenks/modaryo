"use client";

import { useRouter, useSearchParams } from "next/navigation";
import Alert from "@mui/material/Alert";
import AccountForm from "@/components/cari/AccountForm";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import type { AccountRole } from "@/lib/api/erp";
import { useSession } from "@/session/SessionProvider";

const ROLES: Record<string, AccountRole> = { musteri: "customer", tedarikci: "supplier" };

/** Yeni cari sayfası. ?rol=musteri|tedarikci ilgili kutucuğu işaretli açar; kaydedince detay sayfasına gider. */
export default function NewAccount() {
    const router = useRouter();
    const searchParams = useSearchParams();
    const { hasPermission } = useSession();
    const defaultRole = ROLES[searchParams.get("rol") ?? ""];

    return (
        <>
            <PageHeader title="Yeni Cari" subtitle="Müşteri veya tedarikçi kartı oluşturun" />
            {hasPermission("cari.manage") ? (
                <DashboardCard>
                    <AccountForm defaultRole={defaultRole} onSaved={(dto) => router.push(`/cari/${dto.id}`)} />
                </DashboardCard>
            ) : (
                <Alert severity="warning">Cari oluşturmak için yetkiniz yok.</Alert>
            )}
        </>
    );
}
