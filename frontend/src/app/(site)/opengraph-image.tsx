import { ImageResponse } from "next/og";

export const alt = "Modaryo ERP";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

export default function OpengraphImage() {
    return new ImageResponse(
        (
            <div style={{ width: "100%", height: "100%", display: "flex", flexDirection: "column", justifyContent: "center", padding: 80, background: "#f6f7f9", color: "#1a2233" }}>
                <div style={{ fontSize: 96, fontWeight: 800, letterSpacing: -3 }}>Modaryo</div>
                <div style={{ fontSize: 44, marginTop: 24, color: "#4a5568" }}>Cari, stok, satış ve muhasebe tek yerde</div>
            </div>
        ),
        { ...size },
    );
}
