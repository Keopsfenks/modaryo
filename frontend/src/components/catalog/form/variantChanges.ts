import type { VariantDto } from "@/lib/api/catalog";

type AttributeInput = { name: string; values: string[] };

/** Özelliklerin kartezyen çarpımı (backend VariantGenerator ile aynı: değeri olmayan özellik atlanır). */
function combinations(attributes: AttributeInput[]): Record<string, string>[] {
    const used = attributes.filter((a) => a.values.length > 0);
    if (used.length === 0) return [];
    let acc: Record<string, string>[] = [{}];
    for (const attribute of used) {
        acc = acc.flatMap((combo) => attribute.values.map((value) => ({ ...combo, [attribute.name]: value })));
    }
    return acc;
}

const keyOf = (values: Record<string, string>) =>
    JSON.stringify(Object.entries(values).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));

/**
 * Yeni özellik kümesinde karşılığı kalmayan mevcut varyantlar. Backend (SetAttributes) bu varyantları siler;
 * bir varyant yalnızca özellik adları ve değerleri bir kombinasyonla birebir aynıysa korunur. Bu yüzden bir özelliği
 * yeniden adlandırmak ya da yeni özellik eklemek tüm varyantları siler ve yeniden üretir.
 */
export function variantsToRemove(variants: VariantDto[], attributes: AttributeInput[]): VariantDto[] {
    const kept = new Set(combinations(attributes).map(keyOf));
    return variants.filter((variant) => !kept.has(keyOf(variant.values)));
}
