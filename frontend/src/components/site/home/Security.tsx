import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import { securityItems } from "@/content/site/home";
import Section from "./Section";

export default function Security() {
    return (
        <Section id="guvenlik" title="Verileriniz firmanıza ait" intro="Ticari kayıtlarınız hassastır. Erişimi siz yönetirsiniz, değişiklikler kayda geçer." tone="band">
            <Box component="dl" sx={{ m: 0, display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" }, columnGap: 8 }}>
                {securityItems.map((item) => (
                    <Box key={item.title} sx={{ py: 2.5, borderTop: "1px solid", borderColor: "divider" }}>
                        <Typography component="dt" sx={{ fontWeight: 700, fontSize: "1.125rem", mb: 0.5 }}>{item.title}</Typography>
                        <Typography component="dd" sx={{ m: 0, color: "text.secondary", lineHeight: 1.6 }}>{item.text}</Typography>
                    </Box>
                ))}
            </Box>
        </Section>
    );
}
