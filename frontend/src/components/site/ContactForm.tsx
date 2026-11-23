"use client";

import { useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormHelperText from "@mui/material/FormHelperText";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import { siteModules } from "@/content/site/modules";
import { ApiError } from "@/lib/api/client";
import { site, type ContactInput } from "@/lib/api/site";
import { collectErrors, validateEmail, validateFullName, type FieldErrors } from "@/lib/validation";

const RATE_LIMITED = "Çok fazla deneme yaptınız, lütfen biraz sonra tekrar deneyin.";
const CONSENT_REQUIRED = "Devam etmek için KVKK aydınlatma metnini onaylamalısınız.";

type Values = { name: string; company: string; email: string; phone: string; interest: string; message: string; website: string };

/** Demo talebi ve genel iletişim formu. `?ilgi=` modül anahtarı ilgi alanını önceden seçer. */
export default function ContactForm({ topic }: { topic: ContactInput["topic"] }) {
    const ilgi = useSearchParams().get("ilgi") ?? "";
    const [values, setValues] = useState<Values>({
        name: "",
        company: "",
        email: "",
        phone: "",
        interest: siteModules.some((m) => m.slug === ilgi) ? ilgi : "",
        message: "",
        website: "",
    });
    const [consent, setConsent] = useState(false);
    const [errors, setErrors] = useState<FieldErrors>({});
    const [error, setError] = useState<ApiError | string | null>(null);
    const [state, setState] = useState<"idle" | "sending" | "sent">("idle");

    const set = (key: keyof Values) => (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
        setValues((v) => ({ ...v, [key]: event.target.value }));

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (state === "sending") return;

        const found = collectErrors({
            name: validateFullName(values.name),
            email: validateEmail(values.email),
            consent: consent ? undefined : CONSENT_REQUIRED,
        });
        setErrors(found);
        setError(null);
        if (Object.keys(found).length > 0) return;

        setState("sending");
        try {
            await site.contact({
                name: values.name.trim(),
                company: values.company.trim() || undefined,
                email: values.email.trim(),
                phone: values.phone.trim() || undefined,
                topic,
                interest: values.interest || undefined,
                message: values.message.trim() || undefined,
                kvkkConsent: true,
                website: values.website,
            });
            setState("sent");
        } catch (e) {
            setState("idle");
            if (!(e instanceof ApiError)) throw e;
            if (e.status === 429) {
                setError(RATE_LIMITED);
            } else {
                setErrors(e.fieldErrors);
                setError(e);
            }
        }
    };

    if (state === "sent") {
        return (
            <Box role="status" sx={{ borderLeft: "3px solid", borderColor: "primary.main", pl: 3, py: 1 }}>
                <Typography sx={{ fontSize: "1.25rem", fontWeight: 700, lineHeight: 1.3 }}>Teşekkürler, en kısa sürede dönüş yapacağız.</Typography>
            </Box>
        );
    }

    return (
        <Box component="form" noValidate onSubmit={onSubmit} aria-label={topic === "demo" ? "Demo talep formu" : "İletişim formu"}>
            <FormAlert error={error} />
            <Stack spacing={2.5}>
                <TextField id="contact-name" name="name" label="Ad soyad" required autoComplete="name" fullWidth value={values.name} onChange={set("name")} error={Boolean(errors.name)} helperText={errors.name} />
                <TextField id="contact-email" name="email" label="E-posta" type="email" required autoComplete="email" fullWidth value={values.email} onChange={set("email")} error={Boolean(errors.email)} helperText={errors.email} />
                <Stack direction={{ xs: "column", sm: "row" }} spacing={2.5}>
                    <TextField id="contact-company" name="company" label="Firma" autoComplete="organization" fullWidth value={values.company} onChange={set("company")} error={Boolean(errors.company)} helperText={errors.company} />
                    <TextField id="contact-phone" name="phone" label="Telefon" type="tel" autoComplete="tel" fullWidth value={values.phone} onChange={set("phone")} error={Boolean(errors.phone)} helperText={errors.phone} />
                </Stack>
                <TextField id="contact-interest" name="interest" label="İlgilendiğiniz modül" select fullWidth value={values.interest} onChange={set("interest")} slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}>
                    <option value="">Belirtmek istemiyorum</option>
                    {siteModules.map((m) => (
                        <option key={m.slug} value={m.slug}>{m.name}</option>
                    ))}
                </TextField>
                <TextField id="contact-message" name="message" label="Mesajınız" multiline minRows={4} fullWidth value={values.message} onChange={set("message")} error={Boolean(errors.message)} helperText={errors.message} />

                {/* Tuzak alan: ekran dışında, erişilebilirlik ağacında ve sekme sırasında yok. display:none değil, çünkü botlar onu atlar. */}
                <Box aria-hidden="true" sx={{ position: "absolute", left: "-10000px", width: 1, height: 1, overflow: "hidden" }}>
                    <label>
                        Web sitesi
                        <input type="text" name="website" tabIndex={-1} autoComplete="off" value={values.website} onChange={set("website")} />
                    </label>
                </Box>

                <FormControl error={Boolean(errors.consent)}>
                    <FormControlLabel
                        control={<Checkbox name="kvkkConsent" checked={consent} onChange={(e) => setConsent(e.target.checked)} />}
                        label={
                            <Typography component="span" sx={{ fontSize: "0.875rem" }}>
                                <Link href="/kvkk" target="_blank" style={{ textDecoration: "underline" }}>KVKK aydınlatma metni</Link>
                                {"ni okudum, kişisel verilerimin bu talep için işlenmesini onaylıyorum."}
                            </Typography>
                        }
                    />
                    {errors.consent && <FormHelperText>{errors.consent}</FormHelperText>}
                </FormControl>

                <Button type="submit" variant="contained" size="large" disabled={state === "sending"} sx={{ alignSelf: "flex-start" }}>
                    {state === "sending" ? "Gönderiliyor" : topic === "demo" ? "Demo talebini gönder" : "Mesajı gönder"}
                </Button>
            </Stack>
        </Box>
    );
}
