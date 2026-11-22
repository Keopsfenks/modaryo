import type { Metadata } from "next";
import { Suspense } from "react";
import NewAccount from "./NewAccount";

export const metadata: Metadata = { title: "Yeni Cari" };

export default function NewAccountPage() {
    return (
        <Suspense>
            <NewAccount />
        </Suspense>
    );
}
