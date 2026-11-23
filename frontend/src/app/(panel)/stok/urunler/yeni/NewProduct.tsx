"use client";

import { useRouter, useSearchParams } from "next/navigation";
import Alert from "@mui/material/Alert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import ProductForm from "@/components/catalog/form/ProductForm";
import { useSession } from "@/session/SessionProvider";

/** Yeni ürün ya da hizmet sayfası. ?tur=hizmet formu hizmet türüyle açar; kaydedince kartın sayfasına gider. */
export default function NewProduct() {
    const router = useRouter();
    const searchParams = useSearchParams();
    const { hasPermission } = useSession();
    const service = searchParams.get("tur") === "hizmet";

    return (
        <>
            <PageHeader title={service ? "Yeni Hizmet" : "Yeni Ürün"} subtitle="Kart bilgilerini girin; varyantlar kayıttan sonra tanımlanır" />
            {hasPermission("katalog.manage") ? (
                <DashboardCard>
                    <ProductForm defaultKind={service ? "service" : "product"} onSaved={(p) => router.push(`/stok/urunler/${p.id}`)} />
                </DashboardCard>
            ) : (
                <Alert severity="warning">Ürün oluşturmak için yetkiniz yok.</Alert>
            )}
        </>
    );
}
