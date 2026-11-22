import { Suspense } from "react";
import type { Metadata } from "next";
import ForgotPasswordForm from "./ForgotPasswordForm";

export const metadata: Metadata = { title: "Şifremi unuttum" };

export default function Page() {
    return (
        <Suspense>
            <ForgotPasswordForm />
        </Suspense>
    );
}
