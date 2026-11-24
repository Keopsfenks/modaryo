"use client";

import { useEffect, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";
import Alert from "@mui/material/Alert";
import FormAlert from "@/components/auth/FormAlert";
import ProductStockTab from "@/components/inventory/ProductStockTab";
import ProductPricesTab from "@/components/pricing/ProductPricesTab";
import {
    VAT_RATES,
    catalog,
    type BrandDto,
    type CatalogOptions,
    type CategoryNode,
    type CustomFieldDto,
    type ProductDto,
    type ProductKind,
} from "@/lib/api/catalog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { useSession } from "@/session/SessionProvider";
import CustomFieldsTab from "./CustomFieldsTab";
import GeneralTab from "./GeneralTab";
import ImagesTab from "./ImagesTab";
import { buildInput, initialValues, validate, type Errors, type FormValues } from "./model";
import SuppliersTab from "./SuppliersTab";
import UnitsTab from "./UnitsTab";
import VariantsTab from "./VariantsTab";

const ALL_TABS = [
    { value: "genel", label: "Genel" },
    { value: "varyantlar", label: "Varyantlar" },
    { value: "birimler", label: "Birimler" },
    { value: "tedarikciler", label: "Tedarikçiler" },
    { value: "gorseller", label: "Görseller" },
    { value: "ozel-alanlar", label: "Özel alanlar" },
    { value: "fiyatlar", label: "Fiyatlar" },
    { value: "stok", label: "Stok" },
] as const;

type TabValue = (typeof ALL_TABS)[number]["value"];

type Lookups = {
    settings: Pick<CatalogOptions, "units" | "defaultVatRate">;
    categories: CategoryNode[];
    brands: BrandDto[];
    definitions: CustomFieldDto[];
};

type ProductFormProps = {
    /** Verilirse düzenleme kipi. */
    product?: ProductDto;
    /** Yeni kartta başlangıç türü (?tur=hizmet). */
    defaultKind?: ProductKind;
    /** Kart sunucuda kaydedildiğinde (oluşturma ve güncelleme) çağrılır. */
    onSaved: (product: ProductDto) => void;
    /** Varyant sekmesi kartı değiştirdiğinde çağrılır. */
    onProductChange?: (product: ProductDto) => void;
    canManage?: boolean;
};

/** Ürün ve hizmet formu: Genel, Varyantlar, Birimler, Tedarikçiler, Görseller ve Özel alanlar sekmeleri. Sekme ?sekme= ile tutulur. */
export default function ProductForm({ product, defaultKind = "product", onSaved, onProductChange, canManage = true }: ProductFormProps) {
    const router = useRouter();
    const pathname = usePathname();
    const searchParams = useSearchParams();
    const { hasPermission, hasModule } = useSession();
    const creating = !product;
    const canSeePurchase = hasPermission("fiyat.alis");

    const [lookups, setLookups] = useState<Lookups | null>(null);
    const [v, setV] = useState<FormValues | null>(null);
    const [errors, setErrors] = useState<Errors>({});
    const [alert, setAlert] = useState<ApiError | string | null>(null);
    const [saved, setSaved] = useState(false);
    const [busy, setBusy] = useState(false);
    const [loadError, setLoadError] = useState<ApiError | string | null>(null);
    const [attempt, setAttempt] = useState(0);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            // Birimler, varsayılan KDV ve özel alanlar katalog.read ile okunur (ayarlar.katalog gerekmez).
            // Okunamazsa tahmin yürütülmez: form açılmaz, hata gösterilir.
            let options: CatalogOptions;
            try {
                options = await catalog.options();
            } catch (e) {
                if (!cancelled) setLoadError(errorMessage(e));
                return;
            }
            const [categories, brands] = await Promise.all([catalog.categories().catch(() => []), catalog.brands().catch(() => [])]);
            if (cancelled) return;
            const settings = { units: options.units, defaultVatRate: options.defaultVatRate };
            setLoadError(null);
            setLookups({ settings, categories, brands, definitions: options.customFields });
            setV((current) => current ?? initialValues(product, defaultKind, settings));
            if (!product) {
                catalog.nextProductCode().then(
                    ({ code }) => {
                        if (!cancelled) setV((prev) => (prev && !prev.code ? { ...prev, code } : prev));
                    },
                    () => undefined,
                );
            }
        })();
        return () => {
            cancelled = true;
        };
        // Form değerleri yalnızca ilk yüklemede kurulur (yeniden deneme dışında).
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [attempt]);

    if (loadError && !lookups) {
        return (
            <Box sx={{ py: 3 }}>
                <FormAlert error={loadError} />
                <Button variant="outlined" onClick={() => setAttempt((n) => n + 1)}>
                    Yeniden dene
                </Button>
            </Box>
        );
    }

    if (!lookups || !v) {
        return (
            <Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
                <CircularProgress aria-label="Yükleniyor" />
            </Box>
        );
    }

    const service = v.kind === "service";
    const canSeePrices = hasPermission("fiyat.read");
    const canSeeStock = hasPermission("stok.read");
    const tabs = ALL_TABS.filter(
        (t) =>
            !(service && t.value === "varyantlar") &&
            !(t.value === "fiyatlar" && (creating || !canSeePrices)) &&
            !(t.value === "stok" && (creating || service || !canSeeStock)),
    );
    const sekme = searchParams.get("sekme");
    const tab: TabValue = tabs.some((t) => t.value === sekme) ? (sekme as TabValue) : "genel";

    const patch = (change: Partial<FormValues>) => {
        setSaved(false);
        setV((prev) => (prev ? { ...prev, ...change } : prev));
    };

    const changeTab = (_: React.SyntheticEvent, value: TabValue) => {
        const params = new URLSearchParams(searchParams.toString());
        params.set("sekme", value);
        router.replace(`${pathname}?${params.toString()}`, { scroll: false });
    };

    async function generateBarcode() {
        if (!product) return;
        try {
            patch({ barcode: (await catalog.generateBarcode(product.id)).barcode });
        } catch (e) {
            setAlert(errorMessage(e));
        }
    }

    async function submit(event: React.FormEvent) {
        event.preventDefault();
        // Fiyatlar sekmesi salt okunur araçlar içerir; orada Enter tuşu kartı kaydetmez.
        if (busy || !v || !lookups || tab === "fiyatlar" || tab === "stok") return;
        setSaved(false);
        setAlert(null);
        const found = validate(v, lookups.definitions);
        setErrors(found);
        if (Object.keys(found).length > 0) {
            setAlert("Formdaki hataları düzeltin.");
            return;
        }

        setBusy(true);
        try {
            const input = buildInput(v, lookups.definitions);
            const result = product ? await catalog.updateProduct(product.id, input) : await catalog.createProduct(input);
            setSaved(true);
            onSaved(result);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            if (e.code === "code_taken") {
                let message = errorMessage(e);
                try {
                    message += ` Önerilen kod: ${(await catalog.nextProductCode()).code}`;
                } catch {
                    // Öneri alınamazsa yalnızca uyarı gösterilir.
                }
                setErrors({ code: message });
            } else if (e.code === "barcode_taken") {
                setErrors({ barcode: errorMessage(e) });
            } else {
                setErrors(e.fieldErrors);
                setAlert(errorMessage(e));
            }
        } finally {
            setBusy(false);
        }
    }

    const { settings } = lookups;
    const unitOptions = [...new Set([...settings.units])];

    return (
        <Box component="form" noValidate onSubmit={submit}>
            <Tabs value={tab} onChange={changeTab} variant="scrollable" scrollButtons="auto" aria-label="Ürün sekmeleri" sx={{ mb: 3 }}>
                {tabs.map((t) => (
                    <Tab key={t.value} value={t.value} label={t.label} />
                ))}
            </Tabs>

            <FormAlert error={alert} />
            <fieldset disabled={!canManage} style={{ border: 0, margin: 0, padding: 0, minWidth: 0 }}>
            {saved && !alert && (
                <Alert severity="success" sx={{ mb: 2 }}>
                    Kart kaydedildi.
                </Alert>
            )}

            {tab === "genel" && (
                <GeneralTab
                    v={v}
                    errors={errors}
                    creating={creating}
                    categories={lookups.categories}
                    brands={lookups.brands}
                    unitOptions={unitOptions}
                    vatRates={VAT_RATES}
                    showSeriLot={hasModule("seri-lot")}
                    canManageBrands={hasPermission("katalog.manage")}
                    canSeePurchase={canSeePurchase}
                    onChange={patch}
                    onBrandCreated={(brand) => setLookups((l) => (l ? { ...l, brands: [...l.brands, brand] } : l))}
                    onGenerateBarcode={() => void generateBarcode()}
                    onKindChange={(kind) => patch({ ...initialValues(undefined, kind, settings), code: v.code, name: v.name, description: v.description, kind })}
                />
            )}
            {tab === "varyantlar" &&
                (product ? (
                    <VariantsTab product={product} canManage={canManage} canSeePurchase={canSeePurchase} onChanged={(p) => onProductChange?.(p)} />
                ) : (
                    <Alert severity="info">Varyantlar kart kaydedildikten sonra tanımlanır. Önce kartı kaydedin.</Alert>
                ))}
            {tab === "birimler" && <UnitsTab units={v.units} baseUnit={v.soldByMeasure ? v.measureUnit : v.baseUnit} unitOptions={unitOptions} onChange={(units) => patch({ units })} />}
            {tab === "tedarikciler" && <SuppliersTab suppliers={v.suppliers} onChange={(suppliers) => patch({ suppliers })} />}
            {tab === "gorseller" && <ImagesTab fileIds={v.imageFileIds} onChange={(imageFileIds) => patch({ imageFileIds })} />}
            {tab === "ozel-alanlar" && <CustomFieldsTab definitions={lookups.definitions} kind={v.kind} values={v.customFields} errors={errors} onChange={(customFields) => patch({ customFields })} />}

            </fieldset>

            {tab === "fiyatlar" && product && <ProductPricesTab product={product} />}
            {tab === "stok" && product && <ProductStockTab product={product} />}

            {tab !== "varyantlar" && tab !== "fiyatlar" && tab !== "stok" && canManage && (
                <Box sx={{ mt: 3, display: "flex", justifyContent: "flex-end" }}>
                    <Button type="submit" variant="contained" disabled={busy}>
                        {busy ? "Kaydediliyor..." : "Kaydet"}
                    </Button>
                </Box>
            )}
        </Box>
    );
}
