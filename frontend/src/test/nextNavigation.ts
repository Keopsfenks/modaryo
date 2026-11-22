import { vi } from "vitest";

// next/navigation taklidi. Test dosyalarında:
//   vi.mock("next/navigation", () => import("@/test/nextNavigation"));
// ve beforeEach'te resetNavigation(...) ile başlangıç adresi verilir.

export const router = {
    replace: vi.fn(),
    push: vi.fn(),
    refresh: vi.fn(),
    back: vi.fn(),
    prefetch: vi.fn(),
};

let pathname = "/";
let search = new URLSearchParams();

export function resetNavigation(path = "/", query = "") {
    pathname = path;
    search = new URLSearchParams(query);
    Object.values(router).forEach((fn) => fn.mockReset());
}

export const useRouter = () => router;
export const usePathname = () => pathname;
export const useSearchParams = () => search;
export const useParams = () => ({});
export const redirect = vi.fn();
export const notFound = vi.fn();
