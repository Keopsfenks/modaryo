namespace ERP.Application.Common.Exceptions;

/// <summary>İş kuralı ihlali: HTTP durumu ve istemcinin ayırt edebileceği makine kodu taşır.</summary>
public sealed class AppException(
    int    status,
    string code,
    string message
) : Exception(message) {
    public int Status { get; } = status;

    public string Code { get; } = code;

    /// <summary>Yanıta "details" olarak eklenen isteğe bağlı ayrıntı (ör. yetersiz stok satırları).</summary>
    public object? Details { get; init; }
}