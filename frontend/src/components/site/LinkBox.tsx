"use client";

import Link from "next/link";
import Box, { type BoxProps } from "@mui/material/Box";
import Button, { type ButtonProps } from "@mui/material/Button";

/**
 * next/link'i MUI'ye `component` olarak vermek bir fonksiyon geçirmektir; Server Component'ten
 * Client Component'e fonksiyon geçirilemez. Bu sarmalayıcılar geçişi istemci tarafında yapar.
 */
export function LinkBox({ href, ...props }: Omit<BoxProps<"a">, "component" | "href"> & { href: string }) {
    return <Box component={Link} href={href} {...props} />;
}

export function LinkButton({ href, ...props }: Omit<ButtonProps<"a">, "component" | "href"> & { href: string }) {
    return <Button component={Link} href={href} {...props} />;
}
