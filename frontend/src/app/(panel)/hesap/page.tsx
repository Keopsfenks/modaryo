import type { Metadata } from "next";
import Grid from "@mui/material/Grid";
import PageHeader from "@/components/common/PageHeader";
import ChangePasswordCard from "./ChangePasswordCard";
import ProfileCard from "./ProfileCard";
import SessionsCard from "./SessionsCard";

export const metadata: Metadata = { title: "Hesabım" };

export default function AccountPage() {
    return (
        <>
            <PageHeader title="Hesabım" subtitle="Profil, şifre ve oturum ayarları" />
            <Grid container spacing={3}>
                <Grid size={{ xs: 12, lg: 6 }}>
                    <ProfileCard />
                </Grid>
                <Grid size={{ xs: 12, lg: 6 }}>
                    <ChangePasswordCard />
                </Grid>
                <Grid size={12}>
                    <SessionsCard />
                </Grid>
            </Grid>
        </>
    );
}
