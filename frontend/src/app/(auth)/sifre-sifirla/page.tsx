import { Suspense } from "react";
import type { Metadata } from "next";
import ResetPasswordForm from "./ResetPasswordForm";

export const metadata: Metadata = { title: "Şifre sıfırla" };

export default function Page() {
    return (
        <Suspense>
            <ResetPasswordForm />
        </Suspense>
    );
}
