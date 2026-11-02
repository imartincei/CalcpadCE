using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Calcpad.S3Server.Services
{
    public class S3Options
    {
        public string ServiceURL { get; set; } = "http://localhost:3900";
        public string Region { get; set; } = "garage";
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public bool ForcePathStyle { get; set; } = true;
    }

    public sealed class S3ObjectStore : IObjectStore, IDisposable
    {
        private const string MetaPrefix = "x-amz-meta-";
        private readonly AmazonS3Client _s3;

        public S3ObjectStore(S3Options options)
        {
            _s3 = new AmazonS3Client(
                new BasicAWSCredentials(options.AccessKey, options.SecretKey),
                new AmazonS3Config
                {
                    ServiceURL = options.ServiceURL,
                    AuthenticationRegion = options.Region,
                    ForcePathStyle = options.ForcePathStyle,
                    // Only send checksums when an operation requires them.
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
                });
        }

        public Task CreateBucketAsync(string bucket, CancellationToken ct = default) =>
            _s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);

        public Task DeleteBucketAsync(string bucket, CancellationToken ct = default) =>
            _s3.DeleteBucketAsync(bucket, ct);

        public async Task<IReadOnlyList<string>> ListKeysAsync(string bucket, string prefix, CancellationToken ct = default)
        {
            var keys = new List<string>();
            var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix };
            ListObjectsV2Response response;
            do
            {
                response = await _s3.ListObjectsV2Async(request, ct);
                if (response.S3Objects is { } objects)
                    keys.AddRange(objects.Select(o => o.Key));
                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated == true);
            return keys;
        }

        public async Task<StoredObject?> HeadAsync(string bucket, string key, CancellationToken ct = default)
        {
            try
            {
                var response = await _s3.GetObjectMetadataAsync(bucket, key, ct);
                return new StoredObject(key, response.ContentLength, ReadMetadata(response.Metadata));
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<byte[]?> GetAsync(string bucket, string key, CancellationToken ct = default)
        {
            try
            {
                using var response = await _s3.GetObjectAsync(bucket, key, ct);
                using var ms = new MemoryStream();
                await response.ResponseStream.CopyToAsync(ms, ct);
                return ms.ToArray();
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task PutAsync(string bucket, string key, byte[] body, string contentType,
            IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default)
        {
            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = new MemoryStream(body),
                ContentType = contentType
            };
            foreach (var (name, value) in metadata)
                request.Metadata[name] = value;
            await _s3.PutObjectAsync(request, ct);
        }

        public Task CopyAsync(string bucket, string sourceKey, string destinationKey, CancellationToken ct = default) =>
            _s3.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = bucket,
                SourceKey = sourceKey,
                DestinationBucket = bucket,
                DestinationKey = destinationKey,
                MetadataDirective = S3MetadataDirective.COPY
            }, ct);

        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default) =>
            _s3.DeleteObjectAsync(bucket, key, ct);

        private static Dictionary<string, string> ReadMetadata(MetadataCollection metadata)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in metadata.Keys)
            {
                var key = name.StartsWith(MetaPrefix, StringComparison.OrdinalIgnoreCase) ? name[MetaPrefix.Length..] : name;
                result[key] = metadata[name];
            }
            return result;
        }

        public void Dispose() => _s3.Dispose();
    }
}
