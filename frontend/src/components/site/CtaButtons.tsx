import Link from "next/link";
import Button, { type ButtonProps } from "@mui/material/Button";
import Stack from "@mui/material/Stack";

type CtaProps = Pick<ButtonProps, "size" | "fullWidth" | "sx">;

/** Ana çağrı: kayıt sayfası. */
export function PrimaryCta({ label = "Ücretsiz başla", ...props }: CtaProps & { label?: string }) {
    return (
        <Button component={Link} href="/kayit" variant="contained" {...props}>
            {label}
        </Button>
    );
}

/** İkincil çağrı: demo talebi. */
export function SecondaryCta({ label = "Demo iste", href = "/demo", ...props }: CtaProps & { label?: string; href?: string }) {
    return (
        <Button component={Link} href={href} variant="outlined" color="inherit" {...props}>
            {label}
        </Button>
    );
}

export default function CtaButtons({ size = "large" }: { size?: ButtonProps["size"] }) {
    return (
        <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5}>
            <PrimaryCta size={size} />
            <SecondaryCta size={size} />
        </Stack>
    );
}
