import type { NextConfig } from "next";

const identityApi = process.env.IDENTITY_API_URL ?? "http://localhost:5100";
const erpApi = process.env.ERP_API_URL ?? "http://localhost:5282";

const nextConfig: NextConfig = {
    // Tarayıcı yalnızca bu siteyle konuşur; API istekleri servislere aktarılır.
    // Böylece "modaryo_sid" cookie'si birinci taraf olur ve CORS gerekmez.
    async rewrites() {
        return [
            { source: "/api/identity/:path*", destination: `${identityApi}/api/identity/:path*` },
            { source: "/api/erp/:path*", destination: `${erpApi}/api/erp/:path*` },
        ];
    },
};

export default nextConfig;
