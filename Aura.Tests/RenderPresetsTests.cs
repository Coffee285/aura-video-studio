using Xunit;
using Aura.Core.Rendering;
using Aura.Core.Models;

namespace Aura.Tests;

public class RenderPresetsTests
{
    [Fact]
    public void YouTube1080p_Should_HaveCorrectSettings()
    {
        // Act
        var preset = RenderPresets.YouTube1080p;

        // Assert
        Assert.Equal(1920, preset.Res.Width);
        Assert.Equal(1080, preset.Res.Height);
        Assert.Equal("mp4", preset.Container);
        Assert.Equal(12000, preset.VideoBitrateK);
        Assert.Equal(256, preset.AudioBitrateK);
    }

    [Fact]
    public void YouTubeShorts_Should_BeVertical()
    {
        // Act
        var preset = RenderPresets.YouTubeShorts;

        // Assert
        Assert.Equal(1080, preset.Res.Width);
        Assert.Equal(1920, preset.Res.Height);
        Assert.True(preset.Res.Height > preset.Res.Width, "Shorts should be vertical");
    }

    [Theory]
    [InlineData("YouTube 1080p")]
    [InlineData("youtube1080p")]
    [InlineData("1080p")]
    public void GetPresetByName_Should_FindYouTube1080p(string name)
    {
        // Act
        var preset = RenderPresets.GetPresetByName(name);

        // Assert
        Assert.NotNull(preset);
        Assert.Equal(1920, preset.Res.Width);
        Assert.Equal(1080, preset.Res.Height);
    }

    [Fact]
    public void GetPresetByName_Should_ReturnNull_ForInvalidName()
    {
        // Act
        var preset = RenderPresets.GetPresetByName("InvalidPresetName");

        // Assert
        Assert.Null(preset);
    }

    [Fact]
    public void GetPresetNames_Should_ReturnAllPresets()
    {
        // Act
        var names = RenderPresets.GetPresetNames();

        // Assert
        Assert.NotEmpty(names);
        Assert.Contains("YouTube 1080p", names);
        Assert.Contains("YouTube 4K", names);
    }

    [Fact]
    public void CreateCustom_Should_CreateValidRenderSpec()
    {
        // Act
        var spec = RenderPresets.CreateCustom(
            width: 1280,
            height: 720,
            container: "mp4",
            videoBitrateK: 8000,
            audioBitrateK: 192
        );

        // Assert
        Assert.Equal(1280, spec.Res.Width);
        Assert.Equal(720, spec.Res.Height);
        Assert.Equal("mp4", spec.Container);
        Assert.Equal(8000, spec.VideoBitrateK);
        Assert.Equal(192, spec.AudioBitrateK);
    }

    [Fact]
    public void CreateCustom_Should_ThrowForInvalidDimensions()
    {
        // Act & Assert
        Assert.Throws<System.ArgumentException>(() =>
            RenderPresets.CreateCustom(-1, 720)
        );
    }

    [Fact]
    public void SuggestVideoBitrate_Should_ReturnReasonableValue()
    {
        // Arrange
        var resolution1080p = new Resolution(1920, 1080);
        var resolution4K = new Resolution(3840, 2160);

        // Act
        int bitrate1080p = RenderPresets.SuggestVideoBitrate(resolution1080p);
        int bitrate4K = RenderPresets.SuggestVideoBitrate(resolution4K);

        // Assert
        Assert.True(bitrate1080p > 0);
        Assert.True(bitrate4K > bitrate1080p, "4K should suggest higher bitrate than 1080p");
    }

    [Fact]
    public void RequiresHighTierHardware_Should_ReturnTrue_For4K()
    {
        // Arrange
        var resolution4K = new Resolution(3840, 2160);

        // Act
        bool requires = RenderPresets.RequiresHighTierHardware(resolution4K);

        // Assert
        Assert.True(requires);
    }

    [Fact]
    public void RequiresHighTierHardware_Should_ReturnFalse_For1080p()
    {
        // Arrange
        var resolution1080p = new Resolution(1920, 1080);

        // Act
        bool requires = RenderPresets.RequiresHighTierHardware(resolution1080p);

        // Assert
        Assert.False(requires);
    }

    [Fact]
    public void YouTube1080p_Should_IncludeFpsAndCodec()
    {
        // Arrange & Act
        var preset = RenderPresets.YouTube1080p;

        // Assert
        Assert.Equal(30, preset.Fps);
        Assert.Equal("H264", preset.Codec);
        Assert.Equal(75, preset.QualityLevel);
        Assert.True(preset.EnableSceneCut);
    }

    [Fact]
    public void YouTubeShorts_Should_BeVerticalWithCorrectSettings()
    {
        // Arrange & Act
        var preset = RenderPresets.YouTubeShorts;

        // Assert
        Assert.Equal(1080, preset.Res.Width);
        Assert.Equal(1920, preset.Res.Height);
        Assert.Equal(30, preset.Fps);
        Assert.Equal("H264", preset.Codec);
    }

    [Fact]
    public void YouTube4K_Should_HaveHigherBitrateAndCorrectSettings()
    {
        // Arrange & Act
        var preset = RenderPresets.YouTube4K;

        // Assert
        Assert.Equal(3840, preset.Res.Width);
        Assert.Equal(2160, preset.Res.Height);
        Assert.Equal(45000, preset.VideoBitrateK);
        Assert.Equal(30, preset.Fps);
        Assert.Equal("H264", preset.Codec);
    }
}
