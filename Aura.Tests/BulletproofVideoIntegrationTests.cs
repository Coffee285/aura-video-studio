using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Audio;
using Aura.Core.Dependencies;
using Aura.Core.Models;
using Aura.Core.Providers;
using Microsoft.Extensions.Logging;
using Xunit;
using Aura.Core.Models.Streaming;
using System.Runtime.CompilerServices;

namespace Aura.Tests;

/// <summary>
/// Integration tests verifying bulletproof video generation
/// </summary>
public class BulletproofVideoIntegrationTests
{
    private readonly ILogger<WavFileWriter> _wavLogger;
    private readonly ILogger<TtsFallbackService> _fallbackLogger;
    private readonly ILogger<FfmpegLocator> _locatorLogger;
    private readonly WavFileWriter _wavFileWriter;
    private readonly TtsFallbackService _ttsFallbackService;

    public BulletproofVideoIntegrationTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        _wavLogger = loggerFactory.CreateLogger<WavFileWriter>();
        _fallbackLogger = loggerFactory.CreateLogger<TtsFallbackService>();
        _locatorLogger = loggerFactory.CreateLogger<FfmpegLocator>();
        
        _wavFileWriter = new WavFileWriter(_wavLogger);
        _ttsFallbackService = new TtsFallbackService(_fallbackLogger, _wavFileWriter);
    }

    [Fact]
    public async Task WavFileWriter_Should_NeverCreateZeroByteFiles()
    {
        // Arrange
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.wav");
        var audioData = new short[1000];

        try
        {
            // Act
            await _wavFileWriter.WriteAsync(outputPath, audioData);

            // Assert
            Assert.True(File.Exists(outputPath));
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 128, "File must be larger than 128 bytes");
            Assert.True(_wavFileWriter.ValidateWavFile(outputPath), "File must have valid RIFF structure");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task TtsFallbackService_Should_NeverReturnNull()
    {
        // Arrange
        var lines = CreateTestScriptLines(3);
        var voiceSpec = new VoiceSpec("TestVoice", 1.0, 1.0, PauseStyle.Natural);
        var mockProvider = new AlwaysFailingTtsProvider();

        // Act
        var result = await _ttsFallbackService.SynthesizeWithFallbackAsync(
            mockProvider, lines, voiceSpec, 10.0, CancellationToken.None);

        // Assert - Even with complete failure, we get a valid result
        Assert.NotNull(result);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));
        Assert.True(new FileInfo(result.OutputPath).Length > 128);
        Assert.Equal("Silent Fallback", result.UsedVoice);

        // Cleanup
        if (File.Exists(result.OutputPath))
            File.Delete(result.OutputPath);
    }

    [Fact]
    public async Task FfmpegLocator_Should_ProvideDetailedDiagnostics()
    {
        // Arrange
        var locator = new FfmpegLocator(_locatorLogger);

        // Act
        var result = await locator.CheckAllCandidatesAsync(ct: CancellationToken.None);

        // Assert - Should have diagnostics even if not found
        Assert.NotNull(result);
        Assert.NotNull(result.AttemptedPaths);
        Assert.NotEmpty(result.AttemptedPaths);
        Assert.NotNull(result.Source); // Should be "Missing" if not found
        Assert.NotNull(result.Diagnostics);
    }

    [Fact]
    public async Task AtomicWrites_Should_PreventPartialFiles()
    {
        // Arrange
        var outputPath = Path.Combine(Path.GetTempPath(), $"atomic_{Guid.NewGuid()}.wav");
        var partPath = outputPath + ".part";
        var audioData = new short[5000];

        try
        {
            // Act
            await _wavFileWriter.WriteAsync(outputPath, audioData);

            // Assert - No partial file should remain
            Assert.True(File.Exists(outputPath), "Output file should exist");
            Assert.False(File.Exists(partPath), "Partial file should be cleaned up");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            if (File.Exists(partPath))
                File.Delete(partPath);
        }
    }

    [Fact]
    public async Task SilentFallback_Should_BeValidWav()
    {
        // Arrange
        var outputPath = Path.Combine(Path.GetTempPath(), $"silent_{Guid.NewGuid()}.wav");

        try
        {
            // Act
            await _wavFileWriter.GenerateSilenceAsync(outputPath, 5.0);

            // Assert
            Assert.True(File.Exists(outputPath));
            Assert.True(_wavFileWriter.ValidateWavFile(outputPath));
            
            // Verify RIFF header
            using var stream = File.OpenRead(outputPath);
            using var reader = new BinaryReader(stream);
            var riff = new string(reader.ReadChars(4));
            Assert.Equal("RIFF", riff);
            
            reader.ReadInt32(); // File size
            var wave = new string(reader.ReadChars(4));
            Assert.Equal("WAVE", wave);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public void FfmpegValidationResult_Should_TrackSource()
    {
        // Arrange & Act
        var result = new FfmpegValidationResult
        {
            Found = true,
            FfmpegPath = "/path/to/ffmpeg",
            Source = "Portable",
            HasX264 = true,
            VersionString = "6.0"
        };

        // Assert
        Assert.Equal("Portable", result.Source);
        Assert.True(result.HasX264);
        Assert.NotNull(result.Diagnostics);
    }

    [Fact]
    public void TtsFallbackResult_Should_ProvideActionableMessage()
    {
        // Arrange
        var result = new TtsFallbackResult
        {
            UsedFallback = true,
            UsedVoice = "Silent Fallback",
            FallbackReason = "All TTS voices failed"
        };

        // Act
        var message = TtsFallbackService.CreateDiagnosticMessage(result);

        // Assert
        Assert.Contains("Fix it", message);
        Assert.Contains("failed", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultipleFailedAttempts_Should_StillProduceValidOutput()
    {
        // Arrange - Simulate multiple failures before success
        var lines = CreateTestScriptLines(2);
        var voiceSpec = new VoiceSpec("Voice1", 1.0, 1.0, PauseStyle.Natural);
        var mockProvider = new MultipleFailuresTtsProvider(_wavFileWriter);

        // Act
        var result = await _ttsFallbackService.SynthesizeWithFallbackAsync(
            mockProvider, lines, voiceSpec, 5.0, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(result.OutputPath));
        Assert.True(new FileInfo(result.OutputPath).Length > 128);
        Assert.NotEmpty(result.Diagnostics);

        // Cleanup
        if (File.Exists(result.OutputPath))
            File.Delete(result.OutputPath);
    }

    private static IEnumerable<ScriptLine> CreateTestScriptLines(int count)
    {
        var lines = new List<ScriptLine>();
        for (int i = 0; i < count; i++)
        {
            lines.Add(new ScriptLine(
                SceneIndex: i,
                Text: $"Test line {i}",
                Start: TimeSpan.FromSeconds(i * 2),
                Duration: TimeSpan.FromSeconds(2)
            ));
        }
        return lines;
    }

    // Mock providers for testing
    private sealed class AlwaysFailingTtsProvider : ITtsProvider
    {
        public Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
        {
            return Task.FromResult<IReadOnlyList<string>>(new List<string> { "Voice1", "Voice2" });
        }

        public Task<string> SynthesizeAsync(
            IEnumerable<ScriptLine> lines, VoiceSpec spec, CancellationToken ct)
        {
            throw new InvalidOperationException("TTS provider always fails");
        }
    }

    private sealed class MultipleFailuresTtsProvider : ITtsProvider
    {
        private readonly WavFileWriter _writer;
        private int _attempts;

        public MultipleFailuresTtsProvider(WavFileWriter writer)
        {
            _writer = writer;
        }

        public Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
        {
            return Task.FromResult<IReadOnlyList<string>>(
                new List<string> { "Voice1", "Voice2", "Voice3" });
        }

        public async Task<string> SynthesizeAsync(
            IEnumerable<ScriptLine> lines, VoiceSpec spec, CancellationToken ct)
        {
            _attempts++;
            
            // Fail first 2 attempts, succeed on 3rd (fallback to silence)
            if (_attempts < 3)
            {
                throw new InvalidOperationException($"Attempt {_attempts} failed");
            }

            // This won't be reached as fallback will kick in after 2 alternate voice failures
            var outputPath = Path.Combine(Path.GetTempPath(), $"multi_fail_{Guid.NewGuid()}.wav");
            var duration = lines.Sum(l => l.Duration.TotalSeconds);
            await _writer.GenerateSilenceAsync(outputPath, duration, ct: ct);
            return outputPath;
        }
    }
}
