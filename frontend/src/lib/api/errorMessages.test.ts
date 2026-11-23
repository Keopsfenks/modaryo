import { describe, expect, it } from "vitest";
import { ApiError } from "./client";
import { errorMessage } from "./errorMessages";

describe("errorMessage", () => {
    it("ErrorMessage_KnownCode_ReturnsMappedText", () => {
        expect(errorMessage(new ApiError(409, "Sunucu başlığı", "role_in_use"))).toBe("Bu role atanmış üyeler var; önce başka role taşıyın.");
    });

    it("ErrorMessage_UnknownCode_FallsBackToTitle", () => {
        expect(errorMessage(new ApiError(404, "Kayıt bulunamadı.", "not_found"))).toBe("Kayıt bulunamadı.");
    });

    it("ErrorMessage_NotApiError_ReturnsGeneric", () => {
        expect(errorMessage(new Error("x"))).toBe("Beklenmeyen bir hata oluştu.");
    });
});
