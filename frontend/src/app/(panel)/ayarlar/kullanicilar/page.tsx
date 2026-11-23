import type { Metadata } from "next";
import Grid from "@mui/material/Grid";
import SettingsPage from "@/components/settings/SettingsPage";
import CompanyCard from "./CompanyCard";
import InvitationsCard from "./InvitationsCard";
import MembersTable from "./MembersTable";

export const metadata: Metadata = { title: "Kullanıcılar" };

export default function MembersPage() {
    return (
        <SettingsPage category="kullanicilar" title="Kullanıcılar" description="Üyeler, roller ve davetler">
            <Grid container spacing={3}>
                <Grid size={{ xs: 12, lg: 8 }}>
                    <MembersTable />
                </Grid>
                <Grid size={{ xs: 12, lg: 4 }}>
                    <CompanyCard />
                </Grid>
                <Grid size={12}>
                    <InvitationsCard />
                </Grid>
            </Grid>
        </SettingsPage>
    );
}
