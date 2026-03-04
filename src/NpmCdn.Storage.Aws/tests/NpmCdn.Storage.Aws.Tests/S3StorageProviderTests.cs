using System.Net;

using Amazon.S3;
using Amazon.S3.Model;

using Moq;

using NpmCdn.Storage.Aws;

using TUnit.Assertions;
using TUnit.Core;

namespace NpmCdn.Storage.Aws.Tests;

public class S3StorageProviderTests
{
    private Mock<IAmazonS3>? _s3Mock;
    private S3StorageProvider? _provider;
    private const string BucketName = "cdn-bucket";

    [Before(Test)]
    public void Setup()
    {
        _s3Mock = new Mock<IAmazonS3>();
        _provider = new S3StorageProvider(_s3Mock.Object, BucketName);
    }

    [Test]
    public async Task FileExistsAsync_WhenObjectExists_ReturnsTrue()
    {
        // Arrange
        _s3Mock!.Setup(x => x.GetObjectMetadataAsync(BucketName, "packages/jquery/3.7.1/dist/jquery.js", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse { HttpStatusCode = HttpStatusCode.OK });

        // Act
        var result = await _provider!.FileExistsAsync("jquery", "3.7.1", "dist/jquery.js");

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task FileExistsAsync_WhenObjectMissing_ReturnsFalse()
    {
        // Arrange
        _s3Mock!.Setup(x => x.GetObjectMetadataAsync(BucketName, "packages/jquery/3.7.1/dist/jquery.js", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound });

        // Act
        var result = await _provider!.FileExistsAsync("jquery", "3.7.1", "dist/jquery.js");

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ReadFileAsync_WhenObjectMissing_ReturnsNull()
    {
        // Arrange
        _s3Mock!.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound });

        // Act
        var result = await _provider!.ReadFileAsync("jquery", "3.7.1", "dist/jquery.js");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task WriteFileAsync_CallsPutObjectWithStream()
    {
        // Arrange
        using var stream = new MemoryStream();
        _s3Mock!.Setup(x => x.PutObjectAsync(It.Is<PutObjectRequest>(r => r.Key == "packages/jquery/3.7.1/dist/jquery.js" && r.BucketName == BucketName && r.InputStream == stream), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK })
            .Verifiable();

        // Act
        await _provider!.WriteFileAsync("jquery", "3.7.1", "dist/jquery.js", stream);

        // Assert
        _s3Mock.Verify();
    }
}