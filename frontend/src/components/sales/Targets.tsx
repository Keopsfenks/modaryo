"use client";

import { useEffect, useMemo, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import type { ApexOptions } from "apexcharts";
import FormAlert from "@/components/auth/FormAlert";
import Chart, { baseChartOptions, useChartMode } from "@/components/charts/Chart";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity } from "@/lib/api/identity";
import { sales, type TargetActualRow, type TargetRow } from "@/lib/api/sales";
import { formatMoney, parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

export const MONTHS = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];

type Rep = { userId: string; name: string };

const cellKey = (userId: string, month: number) => `${userId}:${month}`;
const asText = (n: number) => (n === 0 ? "" : String(n).replace(".", ","));
const firstName = (name: string) => name.split(" ")[0] ?? name;

/** Satış hedefleri: temsilci x ay hedef ızgarası (TRY), hedef / gerçekleşen grafiği ve temsilci özeti. */
export default function Targets() {
    const { hasPermission } = useSession();
    const allowed = hasPermission("satis.targets");
    const mode = useChartMode();
    const [yearText, setYearText] = useState(String(new Date().getFullYear()));
    const year = Number(yearText);
    const validYear = Number.isInteger(year) && year >= 2000 && year <= 2100;
    const [reps, setReps] = useState<Rep[]>([]);
    const [cells, setCells] = useState<Record<string, string> | null>(null);
    const [actuals, setActuals] = useState<TargetActualRow[]>([]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [members, setMembers] = useState<Rep[]>([]);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        // Temsilci adayları kimlik servisinin üye listesinden gelir; okunamazsa yalnızca hedefi olanlar listelenir.
        identity.getMembers().then(
            (list) => !cancelled && setMembers(list.map((m) => ({ userId: m.userId, name: m.fullName }))),
            () => undefined,
        );
        return () => {
            cancelled = true;
        };
    }, [allowed]);

    useEffect(() => {
        if (!allowed || !validYear) return;
        let cancelled = false;
        Promise.all([sales.targets.get(year), sales.targets.actuals(year)]).then(
            ([targets, report]) => {
                if (cancelled) return;
                setError(null);
                setActuals(report.rows);
                setCells(Object.fromEntries(targets.rows.map((r) => [cellKey(r.userId, r.month), asText(r.amountTry)])));
                const known = new Map<string, string>();
                for (const r of [...targets.rows, ...report.rows]) known.set(r.userId, r.userName ?? known.get(r.userId) ?? "Temsilci");
                setReps(Array.from(known, ([userId, name]) => ({ userId, name })));
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setCells((c) => c ?? {});
            },
        );
        return () => {
            cancelled = true;
        };
    }, [allowed, validYear, year]);

    const rows: Rep[] = useMemo(() => {
        const merged = new Map(members.map((m) => [m.userId, m.name]));
        for (const r of reps) merged.set(r.userId, merged.get(r.userId) ?? r.name);
        return Array.from(merged, ([userId, name]) => ({ userId, name })).sort((a, b) => a.name.localeCompare(b.name, "tr"));
    }, [members, reps]);

    const monthly = useMemo(() => {
        const target = Array<number>(12).fill(0);
        const actual = Array<number>(12).fill(0);
        for (const r of actuals) {
            target[r.month - 1] += r.targetTry;
            actual[r.month - 1] += r.actualTry;
        }
        return { target, actual };
    }, [actuals]);

    const options: ApexOptions = useMemo(() => {
        const base = baseChartOptions(mode);
        return {
            ...base,
            chart: { ...base.chart, type: "bar" },
            colors: ["#5D87FF", "#13DEB9"],
            plotOptions: { bar: { columnWidth: "50%", borderRadius: 4 } },
            xaxis: { categories: MONTHS.map((m) => m.slice(0, 3)) },
            yaxis: { labels: { formatter: (v: number) => formatMoney(v, "TRY") } },
            legend: { show: true, position: "top" },
        };
    }, [mode]);

    if (!allowed) return <Alert severity="warning">Satış hedeflerini yönetmek için yetkiniz yok.</Alert>;

    async function save() {
        if (!cells || !validYear) return;
        const payload: { userId: string; month: number; amountTry: number }[] = [];
        for (const rep of rows) {
            for (let month = 1; month <= 12; month++) {
                const value = (cells[cellKey(rep.userId, month)] ?? "").trim();
                if (!value) continue;
                const amount = parseAmount(value);
                if (amount === null || amount < 0) return setError(`${rep.name} · ${MONTHS[month - 1]}: hedef tutarı geçersiz.`);
                payload.push({ userId: rep.userId, month, amountTry: amount });
            }
        }
        setBusy(true);
        setError(null);
        setNotice(null);
        try {
            const saved = await sales.targets.put(year, payload);
            setCells(Object.fromEntries(saved.rows.map((r: TargetRow) => [cellKey(r.userId, r.month), asText(r.amountTry)])));
            setActuals(await sales.targets.actuals(year).then((r) => r.rows));
            setNotice("Hedefler kaydedildi.");
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const summary = rows.map((rep) => {
        const mine = actuals.filter((a) => a.userId === rep.userId);
        const target = mine.reduce((s, a) => s + a.targetTry, 0);
        const actual = mine.reduce((s, a) => s + a.actualTry, 0);
        return { rep, target, actual, percent: target > 0 ? (actual / target) * 100 : null };
    });

    return (
        <>
            <PageHeader title="Satış hedefleri" subtitle="Temsilci ve ay bazında hedef (TRY) ile kesilmiş faturalardan gerçekleşen" />
            <Stack spacing={3}>
                <FormAlert error={error} />
                {notice && !error && <Alert severity="success">{notice}</Alert>}
                <DashboardCard
                    title="Hedef ızgarası"
                    action={
                        <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                            <TextField size="small" label="Yıl" value={yearText} onChange={(e) => setYearText(e.target.value)} error={!validYear} slotProps={{ htmlInput: { inputMode: "numeric" } }} sx={{ width: 100 }} />
                            <Button variant="contained" disabled={busy || !validYear || cells === null} onClick={() => void save()}>
                                {busy ? "Kaydediliyor..." : "Kaydet"}
                            </Button>
                        </Stack>
                    }
                >
                    {cells === null ? (
                        <Skeleton variant="rounded" height={160} />
                    ) : rows.length === 0 ? (
                        <Typography color="text.secondary">Hedef girilecek temsilci yok; üyeleri görüntüleme yetkisi gerekir.</Typography>
                    ) : (
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 1100 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Temsilci</TableCell>
                                        {MONTHS.map((m) => (
                                            <TableCell key={m}>{m.slice(0, 3)}</TableCell>
                                        ))}
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {rows.map((rep) => (
                                        <TableRow key={rep.userId}>
                                            <TableCell>{rep.name}</TableCell>
                                            {MONTHS.map((m, i) => (
                                                <TableCell key={m} sx={{ minWidth: 92 }}>
                                                    <TextField
                                                        size="small"
                                                        value={cells[cellKey(rep.userId, i + 1)] ?? ""}
                                                        onChange={(e) => setCells((cur) => ({ ...(cur ?? {}), [cellKey(rep.userId, i + 1)]: e.target.value }))}
                                                        slotProps={{ htmlInput: { "aria-label": `${firstName(rep.name)} ${m} hedef`, inputMode: "decimal" } }}
                                                    />
                                                </TableCell>
                                            ))}
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )}
                </DashboardCard>

                <DashboardCard title="Hedef ve gerçekleşen">
                    <Chart
                        type="bar"
                        height={300}
                        series={[
                            { name: "Hedef", data: monthly.target },
                            { name: "Gerçekleşen", data: monthly.actual },
                        ]}
                        options={options}
                    />
                    {summary.length > 0 && (
                        <TableContainer sx={{ mt: 2 }}>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Temsilci</TableCell>
                                        <TableCell align="right">Yıllık hedef</TableCell>
                                        <TableCell align="right">Gerçekleşen</TableCell>
                                        <TableCell align="right">Oran</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {summary.map((s) => (
                                        <TableRow key={s.rep.userId}>
                                            <TableCell>{s.rep.name}</TableCell>
                                            <TableCell align="right">{formatMoney(s.target, "TRY")}</TableCell>
                                            <TableCell align="right">{formatMoney(s.actual, "TRY")}</TableCell>
                                            <TableCell align="right">{s.percent === null ? "—" : `% ${s.percent.toLocaleString("tr-TR", { maximumFractionDigits: 1 })}`}</TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )}
                </DashboardCard>
            </Stack>
        </>
    );
}
