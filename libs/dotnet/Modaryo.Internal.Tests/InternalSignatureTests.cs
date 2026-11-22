using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Modaryo.Internal.Tests;

public class InternalSignatureTests {
    private sealed record Vector(
        string Secret,
        string Method,
        string PathAndQuery,
        long   Timestamp,
        string Tenant,
        string Body,
        string BodySha256Hex,
        string CanonicalString,
        string SignatureHex
    );

    private static Vector LoadVector() {
        var path = Path.Combine(AppContext.BaseDirectory, "internal-signature-v1.json");
        return JsonSerializer.Deserialize<Vector>(File.ReadAllText(path),
                                                  new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [Test]
    public void Signature_KnownVector() {
        var v    = LoadVector();
        var body = Encoding.UTF8.GetBytes(v.Body);

        Convert.ToHexStringLower(SHA256.HashData(body)).ShouldBe(v.BodySha256Hex);
        InternalSignature.CanonicalString(v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, body)
            .ShouldBe(v.CanonicalString);
        InternalSignature.Compute(v.Secret, v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, body)
            .ShouldBe(v.SignatureHex);
        InternalSignature.Verify(v.Secret, v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, body, v.SignatureHex)
            .ShouldBeTrue();
    }

    [Test]
    public void Verify_ConstantTimeAndRejectsWrongSecret() {
        var v         = LoadVector();
        var body      = Encoding.UTF8.GetBytes(v.Body);
        var signature = InternalSignature.Compute(v.Secret, v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, body);

        InternalSignature.Verify("wrong", v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, body, signature)
            .ShouldBeFalse();
        InternalSignature.Verify(v.Secret, "PUT", v.PathAndQuery, v.Timestamp, v.Tenant, body, signature)
            .ShouldBeFalse();
        InternalSignature.Verify(v.Secret, v.Method, v.PathAndQuery, v.Timestamp, null, body, signature)
            .ShouldBeFalse();
        InternalSignature.Verify(v.Secret, v.Method, v.PathAndQuery, v.Timestamp + 1, v.Tenant, body, signature)
            .ShouldBeFalse();
        InternalSignature.Verify(v.Secret, v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, "x"u8, signature)
            .ShouldBeFalse();

        // Bozuk, kısa, uzun ve onaltılık olmayan değerler istisna atmadan reddedilir.
        foreach (var bad in new[] { "", "zz", signature[..^2], signature + "00", new string('g', 64) })
            InternalSignature.Verify(v.Secret, v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, body, bad)
                .ShouldBeFalse();

        InternalSignature.Verify(v.Secret, v.Method, v.PathAndQuery, v.Timestamp, v.Tenant, body,
                                 signature.ToUpperInvariant()).ShouldBeTrue();
    }

    [Test]
    public async Task Handler_SignedRequest_VerifiesWithInternalSignature() {
        var tenant = Guid.NewGuid().ToString("D");
        var time   = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var inner  = new CapturingHandler();
        using var client = new HttpClient(new InternalHttpClientHandler("einvoice", "s3cret", time) {
                                              InnerHandler = inner
                                          }) { BaseAddress = new Uri("http://erp.local") };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/einvoice/send?x=1&y=%C3%A7") {
            Content = new StringContent("{\"a\":1}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(InternalHeaders.Tenant, tenant);
        using var _ = await client.SendAsync(request);

        var sent = inner.Request!;
        sent.Headers.GetValues(InternalHeaders.Service).ShouldBe(["einvoice"]);
        var timestamp = long.Parse(sent.Headers.GetValues(InternalHeaders.Timestamp).Single());
        timestamp.ShouldBe(1_800_000_000);
        InternalSignature.Verify("s3cret", "POST", "/internal/einvoice/send?x=1&y=%C3%A7", timestamp, tenant,
                                 "{\"a\":1}"u8, sent.Headers.GetValues(InternalHeaders.Signature).Single())
            .ShouldBeTrue();
    }

    [Test]
    public async Task Handler_NoTenantNoBody_SignsEmptyTenantAndEmptyBody() {
        var inner = new CapturingHandler();
        using var client = new HttpClient(new InternalHttpClientHandler("einvoice", "s3cret") { InnerHandler = inner }) {
                               BaseAddress = new Uri("http://erp.local")
                           };

        using var _ = await client.GetAsync("/internal/ping");

        var sent      = inner.Request!;
        var timestamp = long.Parse(sent.Headers.GetValues(InternalHeaders.Timestamp).Single());
        InternalSignature.Verify("s3cret", "GET", "/internal/ping", timestamp, null, [],
                                 sent.Headers.GetValues(InternalHeaders.Signature).Single()).ShouldBeTrue();
    }

    private sealed class CapturingHandler : HttpMessageHandler {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
