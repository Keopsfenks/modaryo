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
};

const GENERIC_ERROR = "Beklenmeyen bir hata oluştu.";

/** Hata koduna karşılık gelen ileti; yoksa sunucunun başlığı, o da yoksa genel ileti. */
export function errorMessage(err: unknown): string {
    if (err instanceof ApiError) {
        return (err.code && errorMessages[err.code]) || err.message || GENERIC_ERROR;
    }
    return GENERIC_ERROR;
}
