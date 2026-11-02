using System.Collections.Concurrent;
using System.Text;
using Calcpad.Core;
using Calcpad.S3Server.Models;
using Calcpad.S3Server.Services;

namespace Calcpad.Tests;

public class S3ServerTests
{
    private const string Bucket = "lib";

    private static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task Upload_New_StampsFooterAndMetadata()
    {
        var store = new FakeStore();
        var service = new ObjectService(store);

        var result = await service.UploadAsync(Bucket, "steel/beam.cpd", Text("a = 1\n"), null, "alice");

        Assert.Equal(1, result.Info.Version);
        var info = FileMetadata.TryReadFileInfo(Encoding.UTF8.GetString(result.Content));
        Assert.Equal(result.Info.Id, FileMetadata.GetString(info, FileMetadata.IdKey));
        Assert.Equal(result.Info.BaseId, FileMetadata.GetString(info, FileMetadata.BaseIdKey));
        Assert.Equal("1", FileMetadata.GetString(info, FileMetadata.VersionKey));
        Assert.Equal("alice", FileMetadata.GetString(info, "lastUpdatedBy"));
        Assert.Equal(result.Info.Id, store.Objects["steel/beam.cpd"].Meta["id"]);
    }

    [Fact]
    public async Task Upload_KeepsALocalBaseIdOnFirstUpload()
    {
        var service = new ObjectService(new FakeStore());
        var baseId = Guid.NewGuid().ToString();
        var local = FileMetadata.WithFileInfo("a = 1\n", new Dictionary<string, string> { ["baseId"] = baseId, ["id"] = Guid.NewGuid().ToString() });

        var result = await service.UploadAsync(Bucket, "beam.cpd", Text(local), null, "alice");

        Assert.Equal(baseId, result.Info.BaseId);
    }

    [Fact]
    public async Task Upload_NextVersion_BumpsVersionChangesIdAndArchives()
    {
        var store = new FakeStore();
        var service = new ObjectService(store);
        var v1 = await service.UploadAsync(Bucket, "beam.cpd", Text("a = 1\n"), null, "alice");

        var edited = Encoding.UTF8.GetString(v1.Content).Replace("a = 1", "a = 2");
        var v2 = await service.UploadAsync(Bucket, "beam.cpd", Text(edited), null, "bob");

        Assert.Equal(2, v2.Info.Version);
        Assert.Equal(v1.Info.BaseId, v2.Info.BaseId);
        Assert.NotEqual(v1.Info.Id, v2.Info.Id);
        Assert.True(store.Objects.ContainsKey(ObjectService.ArchiveKey("beam.cpd", v1.Info.Id!)));

        var pinned = await service.FindByIdAsync(Bucket, Guid.Parse(v1.Info.Id!), null);
        Assert.False(pinned!.Latest);
        Assert.Equal("beam.cpd", pinned.Key);
        Assert.Contains("a = 1", Encoding.UTF8.GetString((await service.GetContentAsync(Bucket, pinned))!));

        var latest = await service.FindByIdAsync(Bucket, Guid.Parse(v1.Info.BaseId!), null);
        Assert.Equal(v2.Info.Id, latest!.Id);
    }

    [Fact]
    public async Task Upload_FromAStaleVersion_Conflicts()
    {
        var service = new ObjectService(new FakeStore());
        var v1 = await service.UploadAsync(Bucket, "beam.cpd", Text("a = 1\n"), null, "alice");
        await service.UploadAsync(Bucket, "beam.cpd", v1.Content, null, "alice");

        await Assert.ThrowsAsync<UploadConflictException>(() =>
            service.UploadAsync(Bucket, "beam.cpd", v1.Content, null, "bob"));
        var forced = await service.UploadAsync(Bucket, "beam.cpd", v1.Content, ObjectService.AnyVersion, "bob");
        Assert.Equal(3, forced.Info.Version);
    }

    [Fact]
    public async Task Upload_DataFile_StoresMetadataOnlyInS3()
    {
        var store = new FakeStore();
        var service = new ObjectService(store);

        var result = await service.UploadAsync(Bucket, "data/loads.csv", Text("1,2\n"), null, "alice");

        Assert.Equal("1,2\n", Encoding.UTF8.GetString(store.Objects["data/loads.csv"].Body));
        Assert.Equal(result.Info.BaseId, store.Objects["data/loads.csv"].Meta["base-id"]);
    }

    [Fact]
    public async Task List_ByPrefix_ReturnsLatestOnly()
    {
        var service = new ObjectService(new FakeStore());
        var v1 = await service.UploadAsync(Bucket, "steel/beam.cpd", Text("a = 1\n"), null, "alice");
        await service.UploadAsync(Bucket, "steel/beam.cpd", v1.Content, null, "alice");
        await service.UploadAsync(Bucket, "wood/joist.cpd", Text("a = 1\n"), null, "alice");

        var steel = await service.ListAsync(Bucket, "steel", includeVersions: false);
        Assert.Single(steel);
        Assert.Equal(2, (await service.ListAsync(Bucket, "steel", includeVersions: true)).Count);
    }

    [Theory]
    [InlineData("../x.cpd")]
    [InlineData(".versions/x.cpd")]
    [InlineData("a//b.cpd")]
    public async Task Upload_InvalidKey_Throws(string key)
    {
        var service = new ObjectService(new FakeStore());
        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(Bucket, key, Text("a"), null, "alice"));
    }

    [Theory]
    [InlineData(UserRole.Viewer, null, UserRole.Viewer)]
    [InlineData(UserRole.Viewer, UserRole.Contributor, UserRole.Contributor)]
    [InlineData(UserRole.Contributor, UserRole.Viewer, UserRole.Viewer)]
    [InlineData(UserRole.Contributor, UserRole.None, UserRole.None)]
    [InlineData(UserRole.Admin, UserRole.None, UserRole.Admin)]
    public void BucketAccess_GrantOverridesGlobalRoleExceptForAdmins(UserRole global, UserRole? grant, UserRole expected)
    {
        var bucketGrant = grant is { } role ? new BucketGrant { Role = role } : null;
        Assert.Equal(expected, BucketAccess.Resolve(global, bucketGrant));
    }

    private sealed class FakeStore : IObjectStore
    {
        public sealed record Entry(byte[] Body, Dictionary<string, string> Meta);

        public ConcurrentDictionary<string, Entry> Objects { get; } = new();

        public Task CreateBucketAsync(string bucket, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteBucketAsync(string bucket, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListKeysAsync(string bucket, string prefix, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Objects.Keys.Where(k => k.StartsWith(prefix)).ToList());

        public Task<StoredObject?> HeadAsync(string bucket, string key, CancellationToken ct = default) =>
            Task.FromResult(Objects.TryGetValue(key, out var e) ? new StoredObject(key, e.Body.Length, e.Meta) : null);

        public Task<byte[]?> GetAsync(string bucket, string key, CancellationToken ct = default) =>
            Task.FromResult(Objects.TryGetValue(key, out var e) ? e.Body : null);

        public Task PutAsync(string bucket, string key, byte[] body, string contentType, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default)
        {
            Objects[key] = new Entry(body, new Dictionary<string, string>(metadata));
            return Task.CompletedTask;
        }

        public Task CopyAsync(string bucket, string sourceKey, string destinationKey, CancellationToken ct = default)
        {
            Objects[destinationKey] = Objects[sourceKey];
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
        {
            Objects.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
