using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ERP.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Files;

/// <summary>Files:S3 yapılandırması. Anahtar: {KeyPrefix}{tenantId}/{kind}/{uuid}{ext}.</summary>
public sealed class FileStorageOptions {
    public const string Section = "Files:S3";

    public string ServiceUrl { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "modaryo";

    public string KeyPrefix { get; set; } = string.Empty;
}

/// <summary>S3 uyumlu herhangi bir depoya (yol tarzı adresleme) bağlanan dosya deposu.</summary>
public sealed class S3FileStorage(
    IAmazonS3                    s3,
    IOptions<FileStorageOptions> options
) : IFileStorage {
    private readonly FileStorageOptions _options = options.Value;

    public static IAmazonS3 CreateClient(FileStorageOptions options) {
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
            throw new InvalidOperationException("Dosya deposu yapılandırılmamış (Files:S3:ServiceUrl).");

        var config = new AmazonS3Config {
                                           ServiceURL     = options.ServiceUrl,
                                           ForcePathStyle = true,
                                           AuthenticationRegion = "us-east-1",
                                           // Üçüncü taraf S3 uyumlu depolar isteğe bağlı sağlama toplamı başlıklarını sevmeyebilir.
                                           RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                                           ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
                                       };
        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }

    public string BuildKey(Guid tenantId, string kind, string extension) {
        return $"{_options.KeyPrefix}{tenantId}/{kind}/{Guid.CreateVersion7()}{extension}";
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) {
        await s3.PutObjectAsync(new PutObjectRequest {
                                                         BucketName        = _options.Bucket,
                                                         Key               = key,
                                                         InputStream       = content,
                                                         ContentType       = contentType,
                                                         AutoCloseStream   = false,
                                                         UseChunkEncoding  = false
                                                     }, ct);
    }

    public async Task<Stream> GetAsync(string key, CancellationToken ct) {
        try {
            var response = await s3.GetObjectAsync(_options.Bucket, key, ct);
            return new ResponseOwningStream(response);
        } catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
            throw new FileNotFoundException("Dosya içeriği bulunamadı.", key, ex);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct) {
        await s3.DeleteObjectAsync(_options.Bucket, key, ct);
    }

    /// <summary>Akış kapatılınca yanıtı (ve bağlantıyı) da bırakır.</summary>
    private sealed class ResponseOwningStream(GetObjectResponse response) : Stream {
        private readonly Stream _inner = response.ResponseStream;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => response.ContentLength;

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) {
            return _inner.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }

        public override void SetLength(long value) {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing) {
            if (disposing) response.Dispose();
            base.Dispose(disposing);
        }
    }
}
