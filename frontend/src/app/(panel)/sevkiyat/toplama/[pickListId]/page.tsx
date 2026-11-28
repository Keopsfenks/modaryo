import type { Metadata } from "next";
import PickScreen from "@/components/shipping/PickScreen";

export const metadata: Metadata = { title: "Toplama" };

export default async function Page({ params }: { params: Promise<{ pickListId: string }> }) {
    const { pickListId } = await params;
    return <PickScreen pickListId={pickListId} />;
}
