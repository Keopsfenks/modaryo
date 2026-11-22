import { render, type RenderOptions } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactElement } from "react";
import ThemeRegistry from "@/theme/ThemeRegistry";

/** Bileşeni uygulamanın temasıyla render eder ve bir user-event örneği döndürür. */
export function renderWithTheme(ui: ReactElement, options?: RenderOptions) {
    return {
        user: userEvent.setup(),
        ...render(ui, { wrapper: ({ children }) => <ThemeRegistry>{children}</ThemeRegistry>, ...options }),
    };
}
