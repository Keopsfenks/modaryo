"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Skeleton from "@mui/material/Skeleton";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { identity, type SessionListItem } from "@/lib/api/identity";
import { describeUserAgent } from "@/lib/userAgent";

const dateTime = new Intl.DateTimeFormat("tr-TR", { dateStyle: "medium", timeStyle: "short" });

export default function SessionsCard() {
    const router = useRouter();
    const [sessions, setSessions] = useState<SessionListItem[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [confirmOpen, setConfirmOpen] = useState(false);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        identity.getSessions().then(setSessions, (e: unknown) => {
            if (e instanceof ApiError) setError(e);
            setSessions([]);
        });
    }, []);

    const revoke = async (id: string) => {
        setError(null);
        try {
            await identity.revokeSession(id);
            setSessions((list) => list?.filter((s) => s.id !== id) ?? null);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    };

    const logoutAll = async () => {
        setBusy(true);
        try {
            await identity.logoutAll();
            router.replace("/giris");
        } catch (e) {
            setBusy(false);
            setConfirmOpen(false);
            if (e instanceof ApiError) setError(e);
        }
    };

    return (
        <DashboardCard
            title="Aktif oturumlar"
            subtitle="Hesabınıza giriş yapılmış cihazlar."
            action={
                <Button color="error" variant="outlined" onClick={() => setConfirmOpen(true)}>
                    Tüm cihazlardan çıkış yap
                </Button>
            }
        >
            <FormAlert error={error} />
            {sessions === null ? (
                <Skeleton variant="rounded" height={120} />
            ) : (
                <TableContainer sx={{ mx: -1 }}>
                    <Table size="small" sx={{ minWidth: 560, "& td, & th": { px: 1, py: 1.5 } }}>
                        <TableHead>
                            <TableRow>
                                <TableCell>Cihaz</TableCell>
                                <TableCell>IP adresi</TableCell>
                                <TableCell>Giriş zamanı</TableCell>
                                <TableCell align="right" />
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {sessions.map((session) => (
                                <TableRow key={session.id}>
                                    <TableCell>
                                        <Box sx={{ display: "flex", alignItems: "center", gap: 1, flexWrap: "wrap" }}>
                                            <Typography variant="subtitle2" color="text.primary" sx={{ fontWeight: 600 }}>
                                                {describeUserAgent(session.userAgent)}
                                            </Typography>
                                            {session.isCurrent && <Chip size="small" label="Bu cihaz" color="primary" />}
                                            {session.rememberMe && <Chip size="small" label="Hatırlanıyor" variant="outlined" />}
                                        </Box>
                                    </TableCell>
                                    <TableCell>{session.ip ?? "—"}</TableCell>
                                    <TableCell>{dateTime.format(new Date(session.createdAt))}</TableCell>
                                    <TableCell align="right">
                                        {!session.isCurrent && (
                                            <Button size="small" color="error" onClick={() => revoke(session.id)}>
                                                Kapat
                                            </Button>
                                        )}
                                    </TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                </TableContainer>
            )}

            <Dialog open={confirmOpen} onClose={() => setConfirmOpen(false)}>
                <DialogTitle>Tüm cihazlardan çıkış yapılsın mı?</DialogTitle>
                <DialogContent>
                    <DialogContentText>Bu cihaz dahil, hesabınızın açık olduğu tüm oturumlar kapatılacak.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirmOpen(false)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={logoutAll} disabled={busy}>
                        Çıkış yap
                    </Button>
                </DialogActions>
            </Dialog>
        </DashboardCard>
    );
}
