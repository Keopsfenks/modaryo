import type { Metadata } from "next";
import InvitationLanding from "./InvitationLanding";

export const metadata: Metadata = { title: "Davet" };

export default async function InvitationPage({ params }: PageProps<"/davet/[token]">) {
    const { token } = await params;
    return <InvitationLanding token={token} />;
}
