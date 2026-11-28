"use client";

import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { buildTrackingUrl } from "@/lib/api/shipping";

type TrackingLinkProps = {
    trackingNo: string | null | undefined;
    /** Kargo firmasının takip şablonu ({no} takip numarasıyla değişir). */
    template?: string | null;
    /** Hazır bağlantı (sunucunun hesapladığı trackingUrl); şablondan önceliklidir. */
    url?: string | null;
};

/** Takip numarasını kargo firmasının takip sayfasına bağlar; bağlantı kurulamıyorsa düz metin gösterir. */
export default function TrackingLink({ trackingNo, template, url }: TrackingLinkProps) {
    if (!trackingNo) return null;
    const href = url || buildTrackingUrl(template, trackingNo);
    if (!href) return <Typography variant="body2">{trackingNo}</Typography>;
    return (
        <Link href={href} target="_blank" rel="noopener noreferrer" variant="body2">
            {trackingNo}
        </Link>
    );
}
