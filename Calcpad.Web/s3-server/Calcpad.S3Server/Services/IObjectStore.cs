namespace Calcpad.S3Server.Services
{
    public record StoredObject(string Key, long Size, IReadOnlyDictionary<string, string> Metadata);

    /// <summary>The S3 calls the server makes, so the versioning logic can be tested without Garage.</summary>
    public interface IObjectStore
    {
        Task CreateBucketAsync(string bucket, CancellationToken ct = default);
        Task DeleteBucketAsync(string bucket, CancellationToken ct = default);
        Task<IReadOnlyList<string>> ListKeysAsync(string bucket, string prefix, CancellationToken ct = default);
        Task<StoredObject?> HeadAsync(string bucket, string key, CancellationToken ct = default);
        Task<byte[]?> GetAsync(string bucket, string key, CancellationToken ct = default);
        Task PutAsync(string bucket, string key, byte[] body, string contentType, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default);
        Task CopyAsync(string bucket, string sourceKey, string destinationKey, CancellationToken ct = default);
        Task DeleteAsync(string bucket, string key, CancellationToken ct = default);
    }
}
