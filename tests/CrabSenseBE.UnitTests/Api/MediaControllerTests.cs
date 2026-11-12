using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.DTOs.Media;
using CrabSenseBE.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

public sealed class MediaControllerTests
{
    [Fact]
    public async Task Content_ReturnsRangeEnabledStream()
    {
        var id = Guid.NewGuid();
        var stream = new MemoryStream([1, 2, 3]);
        var service = new Mock<IMediaService>();
        service.Setup(x => x.OpenContentAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaContentResult(
                stream, "video/mp4", "sample.mp4", stream.Length));
        var controller = new MediaController(service.Object);

        var result = await controller.Content(id, CancellationToken.None);

        var file = result.Should().BeOfType<FileStreamResult>().Subject;
        file.EnableRangeProcessing.Should().BeTrue();
        file.ContentType.Should().Be("video/mp4");
        file.FileDownloadName.Should().Be("sample.mp4");
        file.FileStream.Should().BeSameAs(stream);
    }
}
