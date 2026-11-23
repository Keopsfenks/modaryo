import type { Metadata } from "next";
import Stack from "@mui/material/Stack";
import BrandList from "@/components/catalog/BrandList";
import CategoryTree from "@/components/catalog/CategoryTree";
import PageHeader from "@/components/common/PageHeader";

export const metadata: Metadata = { title: "Kategoriler ve markalar" };

export default function CategoriesPage() {
    return (
        <>
            <PageHeader title="Kategoriler ve markalar" subtitle="Ürün kartlarını gruplayan kategori ağacı ve marka listesi" />
            <Stack spacing={3}>
                <CategoryTree />
                <BrandList />
            </Stack>
        </>
    );
}
