using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Audio;
using Aura.Core.Models;
using Aura.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Tts;

/// <summary>
/// Null TTS provider that returns silence - used as final fallback when no other TTS is available
/// </summary>
public class NullTtsProvider : ITtsProvider
{
    private readonly ILogger<NullTtsProvider> _logger;
    private readonly string _outputDir;

    public NullTtsProvider(ILogger<NullTtsProvider> logger)
    {
        _logger = logger;
        _outputDir = Path.Combine(Path.GetTempPath(), "aura-null-tts");
        Directory.CreateDirectory(_outputDir);
    }

    public Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
    {
        // Return a single "silent" voice
        var voices = new List<string> { "Null (Silent)" };
        return Task.FromResult<IReadOnlyList<string>>(voices);
    }

    public async Task<string> SynthesizeAsync(
        IEnumerable<ScriptLine> lines,
        VoiceSpec spec,
        CancellationToken ct = default)
    {
        _logger.LogWarning("NullTtsProvider: Generating silent audio placeholder");
        
        // Calculate total duration
        var totalDuration = TimeSpan.Zero;
        foreach (var line in lines)
        {
            totalDuration += line.Duration;
        }

        var outputPath = Path.Combine(_outputDir, $"silent-{Guid.NewGuid()}.wav");

        // Generate a silent WAV file with standard format: PCM 16-bit, 48kHz, stereo
        int durationMs = totalDuration.TotalMilliseconds > 0 
            ? (int)Math.Ceiling(totalDuration.TotalMilliseconds) 
            : 1000; // Default to 1 second
        
        // Use atomic write utility with standard format
        await Task.Run(() => WavFileWriter.WritePcm16Silent(
            outputPath, 
            durationMs, 
            sampleRate: 48000, 
            channels: 2, 
            logger: _logger), ct);

        _logger.LogInformation("Generated silent audio: {Path}, Duration: {Duration}ms", 
            outputPath, durationMs);
        
        return outputPath;
    }
}
