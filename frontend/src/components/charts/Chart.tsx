"use client";

import dynamic from "next/dynamic";
import Skeleton from "@mui/material/Skeleton";
import { useColorScheme } from "@mui/material/styles";
import type { ApexOptions } from "apexcharts";
import type { Props as ApexChartProps } from "react-apexcharts";

// ApexCharts tarayıcı API'lerine ihtiyaç duyar; sunucuda render edilmez.
const ReactApexChart = dynamic(() => import("react-apexcharts"), {
    ssr: false,
    loading: () => <Skeleton variant="rounded" height="100%" />,
});

export function useChartMode(): "light" | "dark" {
    const { mode, systemMode } = useColorScheme();
    const resolved = mode === "system" ? systemMode : mode;
    return resolved === "dark" ? "dark" : "light";
}

/** Tüm grafiklerde ortak olan görünüm ayarları (yazı tipi, renkler, araç çubuğu, tema). */
export function baseChartOptions(mode: "light" | "dark"): ApexOptions {
    return {
        chart: {
            fontFamily: "inherit",
            foreColor: mode === "dark" ? "#7C8FAC" : "#adb0bb",
            background: "transparent",
            toolbar: { show: false },
            zoom: { enabled: false },
        },
        theme: { mode },
        tooltip: { theme: mode },
        grid: { borderColor: mode === "dark" ? "#333F55" : "#E5EAEF", strokeDashArray: 3 },
        dataLabels: { enabled: false },
        legend: { show: false },
    };
}

export default function Chart({ height, ...props }: ApexChartProps) {
    return (
        <div style={{ height, minHeight: height }}>
            <ReactApexChart height={height} {...props} />
        </div>
    );
}
