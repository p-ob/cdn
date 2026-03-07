using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using NpmCdn.Api.Configuration;
using NpmCdn.Api.Services;
using NpmCdn.Storage;

namespace NpmCdn.Api.Tests;

public class EvictionBackgroundServiceTests
{
    [Test]
    public async Task ExecuteAsync_EvictsStalePackages()
    {
        // Arrange
        var storageMock = new Mock<IStorageProvider>();
        var loggerMock = new Mock<ILogger<EvictionBackgroundService>>();
        var optionsMock = new Mock<IOptions<EvictionOptions>>();

        var options = new EvictionOptions
        {
            RunInterval = TimeSpan.FromMilliseconds(50), // Run very frequently for the test
            MaxInactivityPeriod = TimeSpan.FromDays(30)
        };
        optionsMock.Setup(o => o.Value).Returns(options);

        // Simulate that GetStalePackagesAsync returns one stale package
        var stalePackages = new List<(string, string)>
        {
            ("jquery", "1.0.0")
        };

        storageMock.Setup(x => x.GetStalePackagesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stalePackages);

        var service = new EvictionBackgroundService(storageMock.Object, optionsMock.Object, loggerMock.Object);

        // Act
        // Use a cancellation token source that cancels after a brief delay so the while loop terminates
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        try
        {
            await service.StartAsync(cts.Token);
            // Wait until cancellation triggers
            await Task.Delay(250);
        }
        catch (TaskCanceledException)
        {
            // Expected
        }

        // Assert
        storageMock.Verify(x => x.GetStalePackagesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        storageMock.Verify(x => x.DeletePackageVersionAsync("jquery", "1.0.0", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}