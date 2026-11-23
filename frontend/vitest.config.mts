import mdx from "@mdx-js/rollup";
import react from "@vitejs/plugin-react";
import tsconfigPaths from "vite-tsconfig-paths";
import { defineConfig } from "vitest/config";

export default defineConfig({
    plugins: [{ enforce: "pre", ...mdx() }, tsconfigPaths(), react()],
    test: {
        environment: "jsdom",
        environmentOptions: { jsdom: { url: "http://localhost:3000/" } },
        setupFiles: ["./vitest.setup.ts"],
        include: ["src/**/*.test.{ts,tsx}"],
        restoreMocks: true,
    },
});
