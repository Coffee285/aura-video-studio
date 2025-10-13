using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Providers.Tts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aura.Tests;

public class TtsProviderTests
{
    [Fact]
    public async Task MockTtsProvider_Should_GenerateValidWav()
    {
        // Arrange
        var provider = new MockTtsProvider(NullLogger<MockTtsProvider>.Instance);
        var lines = new List<ScriptLine>
        {
            new ScriptLine(0, "Hello world", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2)),
            new ScriptLine(1, "This is a test", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3))
        };
        var voiceSpec = new VoiceSpec("Mock Voice 1", 1.0, 0.0, PauseStyle.Natural);

        // Act
        var result = await provider.SynthesizeAsync(lines, voiceSpec, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        
        // Verify it's a valid WAV file
        var fileInfo = new FileInfo(result);
        Assert.True(fileInfo.Length > 44); // At least header size
        
        // Read and verify WAV header
        using var stream = File.OpenRead(result);
        using var reader = new BinaryReader(stream);
        
        var riff = new string(reader.ReadChars(4));
        Assert.Equal("RIFF", riff);
        
        reader.ReadInt32(); // File size
        
        var wave = new string(reader.ReadChars(4));
        Assert.Equal("WAVE", wave);
        
        // Cleanup
        File.Delete(result);
    }

    [Fact]
    public async Task MockTtsProvider_Should_GenerateCorrectDuration()
    {
        // Arrange
        var provider = new MockTtsProvider(NullLogger<MockTtsProvider>.Instance);
        var lines = new List<ScriptLine>
        {
            new ScriptLine(0, "Line 1", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1)),
            new ScriptLine(1, "Line 2", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)),
            new ScriptLine(2, "Line 3", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1))
        };
        var voiceSpec = new VoiceSpec("Mock Voice 1", 1.0, 0.0, PauseStyle.Natural);

        // Act
        var result = await provider.SynthesizeAsync(lines, voiceSpec, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        
        // Read WAV file to check duration
        using var stream = File.OpenRead(result);
        using var reader = new BinaryReader(stream);
        
        // Read RIFF header
        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        var riff = new string(reader.ReadChars(4)); // "RIFF"
        reader.ReadInt32(); // File size
        var wave = new string(reader.ReadChars(4)); // "WAVE"
        
        // Read fmt chunk
        var fmt = new string(reader.ReadChars(4)); // "fmt "
        int fmtSize = reader.ReadInt32();
        reader.ReadInt16(); // Audio format
        short numChannels = reader.ReadInt16(); // Num channels
        int sampleRate = reader.ReadInt32();
        reader.ReadInt32(); // Byte rate
        reader.ReadInt16(); // Block align
        short bitsPerSample = reader.ReadInt16();
        
        // Read data chunk header
        var data = new string(reader.ReadChars(4)); // "data"
        int dataSize = reader.ReadInt32();
        
        // Calculate duration from data size, sample rate, channels, and bits per sample
        int bytesPerSample = numChannels * (bitsPerSample / 8);
        double durationSeconds = (double)dataSize / (sampleRate * bytesPerSample);
        
        // Expected duration is 6 seconds (last line ends at 5 + 1)
        Assert.InRange(durationSeconds, 5.5, 6.5);
        
        // Cleanup
        File.Delete(result);
    }

    [Fact]
    public async Task MockTtsProvider_Should_ReturnMockVoices()
    {
        // Arrange
        var provider = new MockTtsProvider(NullLogger<MockTtsProvider>.Instance);

        // Act
        var voices = await provider.GetAvailableVoicesAsync();

        // Assert
        Assert.NotNull(voices);
        Assert.NotEmpty(voices);
        Assert.Contains("Mock Voice 1", voices);
    }

    [Fact]
    public async Task MockTtsProvider_Should_HandleEmptyLines()
    {
        // Arrange
        var provider = new MockTtsProvider(NullLogger<MockTtsProvider>.Instance);
        var lines = new List<ScriptLine>
        {
            new ScriptLine(0, "", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1))
        };
        var voiceSpec = new VoiceSpec("Mock Voice 1", 1.0, 0.0, PauseStyle.Natural);

        // Act
        var result = await provider.SynthesizeAsync(lines, voiceSpec, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        
        // Cleanup
        File.Delete(result);
    }

#if WINDOWS10_0_19041_0_OR_GREATER
    [Fact]
    public async Task WindowsTtsProvider_Should_ReturnVoices()
    {
        // Arrange
        var provider = new WindowsTtsProvider(NullLogger<WindowsTtsProvider>.Instance);

        // Act
        var voices = await provider.GetAvailableVoicesAsync();

        // Assert
        Assert.NotNull(voices);
        Assert.NotEmpty(voices);
    }

    [Fact]
    public async Task WindowsTtsProvider_Should_GenerateValidWav()
    {
        // Arrange
        var provider = new WindowsTtsProvider(NullLogger<WindowsTtsProvider>.Instance);
        var voices = await provider.GetAvailableVoicesAsync();
        
        var lines = new List<ScriptLine>
        {
            new ScriptLine(0, "Test", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1))
        };
        var voiceSpec = new VoiceSpec(voices[0], 1.0, 0.0, PauseStyle.Natural);

        // Act
        var result = await provider.SynthesizeAsync(lines, voiceSpec, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        
        // Cleanup
        File.Delete(result);
    }
#else
    [Fact]
    public async Task WindowsTtsProvider_Should_GenerateValidStubWav_OnNonWindows()
    {
        // Arrange
        var provider = new WindowsTtsProvider(NullLogger<WindowsTtsProvider>.Instance);
        var lines = new List<ScriptLine>
        {
            new ScriptLine(0, "Test", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1))
        };
        var voiceSpec = new VoiceSpec("Any Voice", 1.0, 0.0, PauseStyle.Natural);

        // Act
        var result = await provider.SynthesizeAsync(lines, voiceSpec, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        
        // Verify file is NOT zero-byte
        var fileInfo = new FileInfo(result);
        Assert.True(fileInfo.Length > 128, $"File should be >128 bytes but is {fileInfo.Length} bytes");
        
        // Verify it's a valid WAV file
        using var stream = File.OpenRead(result);
        using var reader = new BinaryReader(stream);
        
        var riff = new string(reader.ReadChars(4));
        Assert.Equal("RIFF", riff);
        
        reader.ReadInt32(); // File size
        
        var wave = new string(reader.ReadChars(4));
        Assert.Equal("WAVE", wave);
        
        // Cleanup
        File.Delete(result);
    }
#endif

    [Fact]
    public async Task NullTtsProvider_Should_GenerateValidWav()
    {
        // Arrange
        var provider = new NullTtsProvider(NullLogger<NullTtsProvider>.Instance);
        var lines = new List<ScriptLine>
        {
            new ScriptLine(0, "Test", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2))
        };
        var voiceSpec = new VoiceSpec("Null (Silent)", 1.0, 0.0, PauseStyle.Natural);

        // Act
        var result = await provider.SynthesizeAsync(lines, voiceSpec, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        
        // Verify file is NOT zero-byte
        var fileInfo = new FileInfo(result);
        Assert.True(fileInfo.Length > 128, $"File should be >128 bytes but is {fileInfo.Length} bytes");
        
        // Read and verify WAV header
        using var stream = File.OpenRead(result);
        using var reader = new BinaryReader(stream);
        
        var riff = new string(reader.ReadChars(4));
        Assert.Equal("RIFF", riff);
        
        reader.ReadInt32(); // File size
        
        var wave = new string(reader.ReadChars(4));
        Assert.Equal("WAVE", wave);
        
        // Read fmt chunk
        var fmt = new string(reader.ReadChars(4)); // "fmt "
        int fmtSize = reader.ReadInt32();
        reader.ReadInt16(); // Audio format
        short numChannels = reader.ReadInt16();
        int sampleRate = reader.ReadInt32();
        
        // Verify it's 48kHz stereo as per spec
        Assert.Equal(48000, sampleRate);
        Assert.Equal(2, numChannels);
        
        // Cleanup
        File.Delete(result);
    }

    [Fact]
    public async Task AllTtsProviders_Should_GenerateMinimumFileSize()
    {
        // Arrange
        var mockProvider = new MockTtsProvider(NullLogger<MockTtsProvider>.Instance);
        var nullProvider = new NullTtsProvider(NullLogger<NullTtsProvider>.Instance);
        var windowsProvider = new WindowsTtsProvider(NullLogger<WindowsTtsProvider>.Instance);
        
        var lines = new List<ScriptLine>
        {
            new ScriptLine(0, "Test", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.25))
        };
        var voiceSpec = new VoiceSpec("Test Voice", 1.0, 0.0, PauseStyle.Natural);

        // Act & Assert - MockTtsProvider
        var mockResult = await mockProvider.SynthesizeAsync(lines, voiceSpec, CancellationToken.None);
        Assert.True(File.Exists(mockResult));
        var mockFileInfo = new FileInfo(mockResult);
        Assert.True(mockFileInfo.Length > 128, $"MockTtsProvider file should be >128 bytes but is {mockFileInfo.Length}");
        File.Delete(mockResult);

        // Act & Assert - NullTtsProvider
        var nullResult = await nullProvider.SynthesizeAsync(lines, voiceSpec, CancellationToken.None);
        Assert.True(File.Exists(nullResult));
        var nullFileInfo = new FileInfo(nullResult);
        Assert.True(nullFileInfo.Length > 128, $"NullTtsProvider file should be >128 bytes but is {nullFileInfo.Length}");
        File.Delete(nullResult);

        // Act & Assert - WindowsTtsProvider (stub on non-Windows)
        var windowsResult = await windowsProvider.SynthesizeAsync(lines, voiceSpec, CancellationToken.None);
        Assert.True(File.Exists(windowsResult));
        var windowsFileInfo = new FileInfo(windowsResult);
        Assert.True(windowsFileInfo.Length > 128, $"WindowsTtsProvider file should be >128 bytes but is {windowsFileInfo.Length}");
        File.Delete(windowsResult);
    }
}
