import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import Typography from "@mui/material/Typography";
import { integrationGroups } from "@/content/site/home";
import Section from "./Section";

export default function Integrations() {
    return (
        <Section id="entegrasyonlar" title="Bağlanacağınız hizmetler" intro="Kapsamı yayına aldıkça ekliyoruz. Aşağıdaki liste planlanan entegrasyonları gösterir; adlar yalnızca bilgi içindir.">
            <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr", md: "repeat(4, 1fr)" }, gap: 4 }}>
                {integrationGroups.map((g) => (
                    <Box key={g.title}>
                        <Typography component="h3" sx={{ fontWeight: 700, mb: 1.5, pb: 1, borderBottom: "1px solid", borderColor: "divider" }}>{g.title}</Typography>
                        <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "flex", flexWrap: "wrap", gap: 1 }}>
                            {g.items.map((item) => (
                                <li key={item}><Chip label={item} variant="outlined" /></li>
                            ))}
                        </Box>
                    </Box>
                ))}
            </Box>
        </Section>
    );
}
