import { useEffect, useState } from "react";

/** Yazılan değeri belirli bir gecikmeyle API'ye giden değere çevirir. */
export function useDebounced(value: string, delay = 250): string {
    const [debounced, setDebounced] = useState(value);
    useEffect(() => {
        const handle = setTimeout(() => setDebounced(value.trim()), delay);
        return () => clearTimeout(handle);
    }, [value, delay]);
    return debounced;
}
