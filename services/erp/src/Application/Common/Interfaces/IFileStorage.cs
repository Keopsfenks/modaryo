namespace ERP.Application.Common.Interfaces;

/// <summary>Nesne deposu (S3 uyumlu). Anahtarı çağıran üretir; depo firma kavramını bilmez.</summary>
public interface IFileStorage {
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);

    /// <summary>İçeriği açar; çağıran akışı kapatmalıdır. Nesne yoksa <see cref="FileNotFoundException" /> fırlatır.</summary>
    Task<Stream> GetAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);

    /// <summary>Yeni nesne anahtarı: {önek}{firma}/{tür}/{uuid}{uzantı}.</summary>
    string BuildKey(Guid tenantId, string kind, string extension);
}

/// <summary>Sihirli baytlarla tespit edilen içerik türü.</summary>
public sealed record SniffedType(string ContentType, string Extension, bool IsImage);

public interface IFileSniffer {
    /// <summary>Dosyanın ilk baytlarına bakar; desteklenmeyen türde null döner.</summary>
    SniffedType? Sniff(ReadOnlySpan<byte> head);
}
