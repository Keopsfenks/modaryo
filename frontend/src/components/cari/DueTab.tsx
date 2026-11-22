import Alert from "@mui/material/Alert";
import type { CurrentAccountDto } from "@/lib/api/erp";

/** Vade sekmesi için yer tutucu (sonraki görevde doldurulacak). */
// eslint-disable-next-line @typescript-eslint/no-unused-vars
export default function DueTab(_props: { account: CurrentAccountDto }) {
    return <Alert severity="info">Vade takibi bu sürümde henüz hazır değil.</Alert>;
}
