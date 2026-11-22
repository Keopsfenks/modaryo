import Alert from "@mui/material/Alert";
import type { CurrentAccountDto } from "@/lib/api/erp";

/** Ekstre sekmesi için yer tutucu (sonraki görevde doldurulacak). */
// eslint-disable-next-line @typescript-eslint/no-unused-vars
export default function StatementTab(_props: { account: CurrentAccountDto }) {
    return <Alert severity="info">Ekstre bu sürümde henüz hazır değil.</Alert>;
}
