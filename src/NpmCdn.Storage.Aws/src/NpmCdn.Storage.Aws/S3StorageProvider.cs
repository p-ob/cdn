using System.Net;

using Amazon.S3;
using Amazon.S3.Model;

namespace NpmCdn.Storage.Aws;

public class S3StorageProvider(IAmazonS3 s3Client, string bucketName) : IStorageProvider
{
    private readonly IAmazonS3 _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
    private readonly string _bucketName = string.IsNullOrEmpty(bucketName) ? throw new ArgumentNullException(nameof(bucketName)) : bucketName;

    private static string GetObjectKey(string packageName, string version, string filePath)
    {
        return $"packages/{packageName}/{version}/{filePath}".Replace('\\', '/');
    }

    private static string GetAccessFileKey(string packageName, string version)
    {
        return $"packages/{packageName}/{version}/.last_accessed";
    }

    public async Task<bool> FileExistsAsync(string packageName, string version, string filePath, CancellationToken cancellationToken = default)
    {
        var objectKey = GetObjectKey(packageName, version, filePath);
        try
        {
            var response = await _s3Client.GetObjectMetadataAsync(_bucketName, objectKey, cancellationToken);
            return response.HttpStatusCode == HttpStatusCode.OK;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<Stream?> ReadFileAsync(string packageName, string version, string filePath, CancellationToken cancellationToken = default)
    {
        var objectKey = GetObjectKey(packageName, version, filePath);
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey
            };

            var response = await _s3Client.GetObjectAsync(request, cancellationToken);

            // Asynchronously fire-and-forget the access time touch so it doesn't block the stream return
            _ = TouchPackageAccessTimeAsync(packageName, version, CancellationToken.None);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task WriteFileAsync(string packageName, string version, string filePath, Stream content, CancellationToken cancellationToken = default)
    {
        var objectKey = GetObjectKey(packageName, version, filePath);

        var putRequest = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            InputStream = content
        };

        await _s3Client.PutObjectAsync(putRequest, cancellationToken);
    }

    public async Task TouchPackageAccessTimeAsync(string packageName, string version, CancellationToken cancellationToken = default)
    {
        var objectKey = GetAccessFileKey(packageName, version);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // AWS requires a valid stream to PutObject content directly
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(timestamp));

        var putRequest = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            InputStream = stream
        };

        await _s3Client.PutObjectAsync(putRequest, cancellationToken);
    }

    public async Task DeletePackageVersionAsync(string packageName, string version, CancellationToken cancellationToken = default)
    {
        var prefix = $"packages/{packageName}/{version}/";

        var listRequest = new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = prefix
        };

        ListObjectsV2Response listResponse;
        do
        {
            listResponse = await _s3Client.ListObjectsV2Async(listRequest, cancellationToken);

            if (listResponse.S3Objects.Count > 0)
            {
                var deleteRequest = new DeleteObjectsRequest
                {
                    BucketName = _bucketName,
                    Objects = listResponse.S3Objects.Select(x => new KeyVersion { Key = x.Key }).ToList()
                };

                await _s3Client.DeleteObjectsAsync(deleteRequest, cancellationToken);
            }

            listRequest.ContinuationToken = listResponse.NextContinuationToken;
        } while (listResponse.IsTruncated == true);
    }

    public async Task<IEnumerable<(string PackageName, string Version)>> GetStalePackagesAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        var stalePackages = new List<(string, string)>();
        var cutoffUnix = ((DateTimeOffset)cutoffDate).ToUnixTimeSeconds();

        var listRequest = new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = "packages/"
        };

        ListObjectsV2Response listResponse;
        do
        {
            listResponse = await _s3Client.ListObjectsV2Async(listRequest, cancellationToken);

            // Access files are located at packages/{org?}/{pkg}/{version}/.last_accessed
            var accessFiles = listResponse.S3Objects.Where(x => x.Key.EndsWith("/.last_accessed")).ToList();

            foreach (var file in accessFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    using var response = await _s3Client.GetObjectAsync(_bucketName, file.Key, cancellationToken);
                    using var reader = new StreamReader(response.ResponseStream);
                    var content = await reader.ReadToEndAsync(cancellationToken);

                    if (long.TryParse(content, out var lastAccessedSeconds))
                    {
                        if (lastAccessedSeconds < cutoffUnix)
                        {
                            // "packages/@types/jquery/3.7.1/.last_accessed"
                            var parts = file.Key.Split('/');
                            if (parts.Length >= 4)
                            {
                                var version = parts[parts.Length - 2];
                                var packageName = string.Join('/', parts.Skip(1).Take(parts.Length - 3));
                                stalePackages.Add((packageName, version));
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore read errors
                }
            }

            listRequest.ContinuationToken = listResponse.NextContinuationToken;
        } while (listResponse.IsTruncated == true);

        return stalePackages;
    }
}