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
    measure_in_use: "Kullanılan kartın ölçüyle satış ayarı kapatılamaz.",
    key_taken: "Bu anahtarla bir özel alan zaten tanımlı.",
    too_many_custom_fields: "En fazla 30 özel alan tanımlanabilir.",
    barcode_sequence_exhausted: "Barkod sırası tükendi; ayarlardan sıradaki barkod numarasını güncelleyin.",
    import_conflict: "Önizlemeden sonra veriler değişti; hiçbir şey yazılmadı. Yeni önizlemeyi kontrol edin.",
    import_expired: "Önizlemenin süresi doldu (30 dakika). Dosyayı yeniden yükleyin.",
    import_has_errors: "Dosyada hatalı satırlar var; hiçbir şey yazılmadı. Düzeltip yeniden yükleyin.",
    import_already_committed: "Bu içe aktarma zaten uygulandı.",
    invalid_file: "Dosya okunamadı. Geçerli bir .xlsx dosyası yükleyin.",
    invalid_template: "Dosyada Kod veya Ad sütunu bulunamadı. Şablonu indirip kullanın.",
    too_many_rows: "Dosyada en fazla 5.000 satır olabilir.",
    empty_file: "Dosyada hiç satır yok.",
    file_too_large: "Dosya çok büyük; en fazla 5 MB yüklenebilir.",
};

const GENERIC_ERROR = "Beklenmeyen bir hata oluştu.";

/** Hata koduna karşılık gelen ileti; yoksa sunucunun başlığı, o da yoksa genel ileti. */
export function errorMessage(err: unknown): string {
    if (err instanceof ApiError) {
        return (err.code && errorMessages[err.code]) || err.message || GENERIC_ERROR;
    }
    return GENERIC_ERROR;
}
