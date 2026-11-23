"use client";

import Link from "next/link";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import PageHeader from "./PageHeader";

export default function ComingSoon({ title }: { title: string }) {
    return (
        <>
            <PageHeader title={title} />
            <Card>
                <CardContent>
                    <Box sx={{ textAlign: "center", py: 6 }}>
                        <Typography variant="h4" gutterBottom>
                            {title} modülü hazırlanıyor
                        </Typography>
                        <Typography color="text.secondary" sx={{ mb: 3 }}>
                            Bu ekran henüz geliştirilmedi. ERP servisine bağlandığında burada listeler ve formlar yer alacak.
                        </Typography>
                        <Button variant="contained" component={Link} href="/panel">
                            Genel Bakış&apos;a dön
                        </Button>
                    </Box>
                </CardContent>
            </Card>
        </>
    );
}
