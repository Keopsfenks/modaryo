import { ApiError } from "./client";

/** Backend `code` değeri → kullanıcıya gösterilecek Türkçe ileti. */
export const errorMessages: Record<string, string> = {
    role_locked: "Sahip rolü değiştirilemez.",
    role_in_use: "Bu role atanmış üyeler var; önce başka role taşıyın.",
    role_name_taken: "Bu adda bir rol zaten var.",
    permission_not_grantable: "Sahip olmadığınız bir izni veremezsiniz.",
    branch_is_default: "Varsayılan şube silinemez veya pasifleştirilemez.",
    branch_code_taken: "Bu şube kodu kullanılıyor.",
    branch_in_use: "Bu şubeye bağlı kayıtlar var.",
    version_conflict: "Bu ayar siz düzenlerken değişti. Sayfayı yenileyip tekrar deneyin.",
    unknown_module: "Bilinmeyen modül.",
    module_disabled: "Bu modül firmanızda kapalı.",
    code_taken: "Bu kod başka bir ürün veya varyantta kullanılıyor.",
    barcode_taken: "Bu barkod başka bir kalemde kullanılıyor.",
    product_in_use: "Bu ürün hareket gördüğü için silinemez; pasifleştirebilirsiniz.",
    variant_in_use: "Bu varyant kullanıldığı için kaldırılamaz; pasifleştirebilirsiniz.",
    category_has_children: "Alt kategorisi olan kategori silinemez. Önce alt kategorileri silin veya taşıyın.",
    category_in_use: "Bu kategoriye bağlı ürünler var. Önce ürünlerin kategorisini değiştirin.",
    brand_in_use: "Bu markaya bağlı ürünler var. Önce ürünlerin markasını değiştirin.",
    invalid_conversion: "Birim çevrim katsayısı sıfırdan büyük olmalı ve en fazla 6 ondalık içermelidir.",
    too_many_variants: "En fazla 500 varyant oluşturulabilir.",
};

const GENERIC_ERROR = "Beklenmeyen bir hata oluştu.";

/** Hata koduna karşılık gelen ileti; yoksa sunucunun başlığı, o da yoksa genel ileti. */
export function errorMessage(err: unknown): string {
    if (err instanceof ApiError) {
        return (err.code && errorMessages[err.code]) || err.message || GENERIC_ERROR;
    }
    return GENERIC_ERROR;
}
