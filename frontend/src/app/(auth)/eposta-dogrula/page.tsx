import { Suspense } from "react";
import type { Metadata } from "next";
import ConfirmEmail from "./ConfirmEmail";

export const metadata: Metadata = { title: "E-posta doğrulama" };

export default function ConfirmEmailPage() {
    return (
        <Suspense>
            <ConfirmEmail />
        </Suspense>
    );
}
