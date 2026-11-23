"use client";

import { Fragment, useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Collapse from "@mui/material/Collapse";
import Skeleton from "@mui/material/Skeleton";
import Tab from "@mui/material/Tab";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TablePagination from "@mui/material/TablePagination";
import TableRow from "@mui/material/TableRow";
import Tabs from "@mui/material/Tabs";
import Typography from "@mui/material/Typography";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { erp } from "@/lib/api/erp";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity } from "@/lib/api/identity";
import { formatDate } from "@/lib/format";

const PAGE_SIZE = 25;

type Source = "identity" | "erp";

type Row = {
    id: string;
    at: string;
    actorLabel: string;
    action: string;
    entityType: string;
    entityId: string;
    before: unknown;
    after: unknown;
};

/** Eylem kodu → Türkçe etiket; bilinmeyen eylem ham haliyle gösterilir. */
const actionLabels: Record<string, string> = {
    "role.created": "Rol oluşturuldu",
    "role.updated": "Rol güncellendi",
    "role.deleted": "Rol silindi",
    "role.reset": "Rol varsayılana döndürüldü",
    "modules.updated": "Modüller güncellendi",
    "branch.created": "Şube eklendi",
    "branch.updated": "Şube güncellendi",
    "branch.deleted": "Şube silindi",
    "member.role_changed": "Üye rolü değişti",
    "member.branches_changed": "Üye şube erişimi değişti",
    "member.removed": "Üye çıkarıldı",
    "settings.updated": "Ayarlar güncellendi",
};

const entityLabels: Record<string, string> = {
    role: "Rol",
    modules: "Modüller",
    branch: "Şube",
    member: "Üye",
    settings: "Ayarlar",
};

/** ERP kayıtlarında before/after JSON metnidir; kimlik kayıtlarında zaten nesnedir. */
function parse(value: unknown): unknown {
    if (typeof value !== "string") return value ?? null;
    try {
        return JSON.parse(value) as unknown;
    } catch {
        return value;
    }
}

const show = (v: unknown) => (v === undefined || v === null ? "—" : typeof v === "object" ? JSON.stringify(v) : String(v));

type DiffRow = { field: string; before: unknown; after: unknown };

/** Alan düzeyinde değişenleri bulur. */
function diff(beforeRaw: unknown, afterRaw: unknown): DiffRow[] {
    const before = parse(beforeRaw);
    const after = parse(afterRaw);
    const isObj = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null && !Array.isArray(v);
    const b = isObj(before) ? before : {};
    const a = isObj(after) ? after : {};
    const fields = [...new Set([...Object.keys(b), ...Object.keys(a)])];
    return fields
        .filter((f) => JSON.stringify(b[f]) !== JSON.stringify(a[f]))
        .map((f) => ({ field: f, before: b[f], after: a[f] }));
}

function AuditTable({ source }: { source: Source }) {
    const [page, setPage] = useState(0);
    const [data, setData] = useState<{ items: Row[]; total: number } | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [open, setOpen] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        const q = { page: page + 1, pageSize: PAGE_SIZE };
        const request: Promise<{ items: Row[]; total: number }> = source === "identity" ? identity.audit.list(q) : erp.audit.list(q);
        request.then(
            (result) => {
                if (!cancelled) {
                    setError(null);
                    setData(result);
                }
            },
            (e: unknown) => {
                if (!cancelled) {
                    setData({ items: [], total: 0 });
                    setError(errorMessage(e));
                }
            },
        );
        return () => {
            cancelled = true;
        };
    }, [source, page]);

    if (data === null) return <Skeleton variant="rounded" height={140} />;

    return (
        <>
            {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
            <TableContainer>
                <Table size="small" sx={{ minWidth: 640 }}>
                    <TableHead>
                        <TableRow>
                            <TableCell>Zaman</TableCell>
                            <TableCell>Kullanıcı</TableCell>
                            <TableCell>Eylem</TableCell>
                            <TableCell>Kayıt</TableCell>
                            <TableCell align="right" />
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {data.items.map((row) => {
                            const rows = diff(row.before, row.after);
                            const expanded = open === row.id;
                            return (
                                <Fragment key={row.id}>
                                    <TableRow hover>
                                        <TableCell>{formatDate(row.at)}</TableCell>
                                        <TableCell>{row.actorLabel}</TableCell>
                                        <TableCell>{actionLabels[row.action] ?? row.action}</TableCell>
                                        <TableCell>{entityLabels[row.entityType] ?? row.entityType}</TableCell>
                                        <TableCell align="right">
                                            <Button size="small" aria-expanded={expanded} onClick={() => setOpen(expanded ? null : row.id)}>
                                                Değişiklik
                                            </Button>
                                        </TableCell>
                                    </TableRow>
                                    <TableRow>
                                        <TableCell colSpan={5} sx={{ py: 0, borderBottom: expanded ? undefined : "none" }}>
                                            <Collapse in={expanded} unmountOnExit>
                                                <Box sx={{ py: 1.5 }}>
                                                    {rows.length === 0 ? (
                                                        <Typography variant="body2" color="text.secondary">Alan değişikliği yok.</Typography>
                                                    ) : (
                                                        <Table size="small">
                                                            <TableHead>
                                                                <TableRow>
                                                                    <TableCell>Alan</TableCell>
                                                                    <TableCell>Önce</TableCell>
                                                                    <TableCell>Sonra</TableCell>
                                                                </TableRow>
                                                            </TableHead>
                                                            <TableBody>
                                                                {rows.map((d) => (
                                                                    <TableRow key={d.field}>
                                                                        <TableCell>{d.field}</TableCell>
                                                                        <TableCell>{show(d.before)}</TableCell>
                                                                        <TableCell>{show(d.after)}</TableCell>
                                                                    </TableRow>
                                                                ))}
                                                            </TableBody>
                                                        </Table>
                                                    )}
                                                </Box>
                                            </Collapse>
                                        </TableCell>
                                    </TableRow>
                                </Fragment>
                            );
                        })}
                        {data.items.length === 0 && (
                            <TableRow>
                                <TableCell colSpan={5}>
                                    <Typography variant="body2" color="text.secondary">Kayıt yok.</Typography>
                                </TableCell>
                            </TableRow>
                        )}
                    </TableBody>
                </Table>
            </TableContainer>
            <TablePagination
                component="div"
                count={data.total}
                page={page}
                rowsPerPage={PAGE_SIZE}
                rowsPerPageOptions={[PAGE_SIZE]}
                onPageChange={(_, p) => setPage(p)}
            />
        </>
    );
}

/** Denetim kaydı: kimlik (kullanıcılar ve roller) ve ERP (ayarlar) kaynakları için iki sekme. */
export default function AuditLog() {
    const [source, setSource] = useState<Source>("identity");

    return (
        <DashboardCard title="Denetim Kaydı">
            <Tabs value={source} onChange={(_, v: Source) => setSource(v)} sx={{ mb: 2 }}>
                <Tab value="identity" label="Kullanıcılar ve roller" />
                <Tab value="erp" label="Ayarlar" />
            </Tabs>
            <AuditTable key={source} source={source} />
        </DashboardCard>
    );
}
