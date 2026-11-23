using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Modaryo.Internal;

namespace IdentityService.Infrastructure.Erp;

/// <summary>
/// ERP'nin GET /internal/branches/{id}/usage ucunu çağırır. HttpClient, <see cref="InternalHttpClientHandler"/> ile imzalanır
/// ve 5 sn zaman aşımıyla kurulur (kayıt: Web/DependencyInjection). Her başarısızlık 503 erp_unavailable olur.
/// </summary>
public sealed class ErpBranchUsageClient(HttpClient http, ILogger<ErpBranchUsageClient> logger) : IErpBranchUsageClient {
    public const string UnavailableMessage = "ERP servisine ulaşılamadı; şube şu an silinemez.";

    private sealed record UsageResponse(bool? InUse);

    public async Task<bool> IsInUseAsync(Guid tenantId, Guid branchId, CancellationToken ct) {
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/branches/{branchId:D}/usage");
            request.Headers.Add(InternalHeaders.Tenant, tenantId.ToString("D"));

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<UsageResponse>(ct);
            return body?.InUse ?? throw new JsonException("Yanıtta 'inUse' alanı yok.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException
                                      or OperationCanceledException or InvalidOperationException) {
            // Zaman aşımı (HttpClient.Timeout), bağlantı hatası, 2xx dışı, bozuk yanıt veya eksik BaseUrl yapılandırması.
            logger.LogWarning(e, "ERP şube kullanım sorgusu başarısız (firma {TenantId}, şube {BranchId}).", tenantId, branchId);
            throw new AuthException(StatusCodes.Status503ServiceUnavailable, AuthErrorCodes.ErpUnavailable, UnavailableMessage);
        }
    }
}
