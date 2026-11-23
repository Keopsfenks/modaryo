import { LinkBox } from "@/components/site/LinkBox";
import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import { highlightSlugs } from "@/content/site/home";
import { getModule, type SiteModule } from "@/content/site/modules";
import ComingSoonChip from "../ComingSoonChip";
import Section from "./Section";

export default function ModuleHighlights() {
    const modules = highlightSlugs.map(getModule).filter((m): m is SiteModule => m !== undefined);

    return (
        <Section id="moduller" title="Günlük işinizin hepsi bir arada" intro="Defter, hesap makinesi ve Excel tablolarını bırakın; satıştan tahsilata her şey aynı kayıtlardan beslensin.">
            <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr", md: "repeat(3, 1fr)" }, border: "1px solid", borderColor: "divider", borderRadius: "10px", overflow: "hidden", bgcolor: "background.paper" }}>
                {modules.map((m) => (
                    <Box key={m.slug} component="li" sx={{ borderBottom: "1px solid", borderRight: "1px solid", borderColor: "divider", mb: "-1px", mr: "-1px" }}>
                        <LinkBox
                            href={`/ozellikler/${m.slug}`}
                            sx={{ display: "block", p: 3, height: "100%", color: "inherit", textDecoration: "none", "&:hover": { bgcolor: "action.hover" }, "&:focus-visible": { outline: "2px solid", outlineColor: "primary.main", outlineOffset: -2 } }}
                        >
                            <Box sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", mb: 1.5 }}>
                                <Box sx={{ color: "primary.main", display: "flex" }}><m.icon size={26} stroke={1.6} aria-hidden /></Box>
                                {m.status === "coming" && <ComingSoonChip />}
                            </Box>
                            <Typography component="h3" sx={{ fontWeight: 700, fontSize: "1.125rem", mb: 0.75 }}>{m.name}</Typography>
                            <Typography sx={{ color: "text.secondary", lineHeight: 1.55 }}>{m.summary}</Typography>
                        </LinkBox>
                    </Box>
                ))}
            </Box>
        </Section>
    );
}
