"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";
import PageHeader from "@/components/common/PageHeader";
import { useSession } from "@/session/SessionProvider";
import ExpenseCategories from "./ExpenseCategories";
import ExpenseForm from "./ExpenseForm";

/** Masraflar sayfası: fiş girişi ve kategori yönetimi. */
export default function Expenses() {
    const { hasPermission } = useSession();
    const [tab, setTab] = useState<"fis" | "kategoriler">("fis");

    if (!hasPermission("masraf.manage")) return <Alert severity="warning">Masraf ve gelir fişleri için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Masraflar" subtitle="Masraf ve gelir fişleri; tutar KDV dahil girilir, net ve KDV ayrılır." />
            <Box sx={{ display: "flex", flexDirection: "column", gap: 3 }}>
                <Tabs value={tab} onChange={(_, v) => setTab(v)} aria-label="Masraf sekmeleri">
                    <Tab value="fis" label="Fiş girişi" />
                    <Tab value="kategoriler" label="Kategoriler" />
                </Tabs>
                {tab === "fis" ? <ExpenseForm /> : <ExpenseCategories canManage />}
            </Box>
        </>
    );
}
