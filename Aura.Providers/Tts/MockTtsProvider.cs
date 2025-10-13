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
/// Mock TTS provider for CI/Linux environments.
/// Generates deterministic beep/silence WAV files with correct length for testing.
/// </summary>
public class MockTtsProvider : ITtsProvider
{
    private readonly ILogger<MockTtsProvider> _logger;
    private readonly string _outputDirectory;

    public MockTtsProvider(ILogger<MockTtsProvider> logger)
    {
        _logger = logger;
        _outputDirectory = Path.Combine(Path.GetTempPath(), "AuraVideoStudio", "TTS");
        
        // Ensure output directory exists
        if (!Directory.Exists(_outputDirectory))
        {
            Directory.CreateDirectory(_outputDirectory);
        }
    }

    public Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
    {
        _logger.LogInformation("MockTtsProvider: Returning mock voices");
        return Task.FromResult<IReadOnlyList<string>>(new List<string> 
        { 
            "Mock Voice 1", 
            "Mock Voice 2", 
            "Mock Voice 3" 
        });
    }

    public async Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec spec, CancellationToken ct)
    {
        _logger.LogInformation("MockTtsProvider: Synthesizing speech with mock voice {Voice}", spec.VoiceName);

        var linesList = lines.ToList();
        
        // Calculate total duration
        TimeSpan totalDuration = TimeSpan.Zero;
        foreach (var line in linesList)
        {
            totalDuration = totalDuration > (line.Start + line.Duration) 
                ? totalDuration 
                : line.Start + line.Duration;
        }

        // Generate a deterministic WAV file with the correct length
        string outputFilePath = Path.Combine(_outputDirectory, $"narration_mock_{DateTime.Now:yyyyMMddHHmmss}.wav");
        
        _logger.LogInformation("MockTtsProvider: Generating {Duration}s of mock audio for {Count} lines", 
            totalDuration.TotalSeconds, linesList.Count);

        // Generate WAV file
        await GenerateWavFileAsync(outputFilePath, totalDuration, ct);

        return outputFilePath;
    }

    /// <summary>
    /// Generates a deterministic WAV file with silence/beep pattern.
    /// WAV format: 44.1kHz, 16-bit, mono
    /// Uses atomic write to prevent incomplete files.
    /// </summary>
    private async Task GenerateWavFileAsync(string outputPath, TimeSpan duration, CancellationToken ct)
    {
        const int sampleRate = 44100;
        const short numChannels = 1;

        int numSamples = (int)(duration.TotalSeconds * sampleRate);
        short[] buffer = new short[numSamples * numChannels];

        // Generate deterministic audio samples (silence with occasional beeps)
        // This creates a predictable pattern for testing
        for (int i = 0; i < numSamples; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Generate a beep every second (simple sine wave at 440 Hz)
            // For the rest, generate silence
            double time = (double)i / sampleRate;
            int secondMark = (int)time;
            double timeInSecond = time - secondMark;
            
            if (timeInSecond < 0.1) // First 100ms of each second has a beep
            {
                // Generate 440 Hz sine wave (A4 note)
                double frequency = 440.0;
                double amplitude = 0.3; // 30% volume
                buffer[i] = (short)(amplitude * short.MaxValue * Math.Sin(2.0 * Math.PI * frequency * time));
            }
            else
            {
                // Silence
                buffer[i] = 0;
            }
        }

        // Use atomic write utility
        await Task.Run(() => WavFileWriter.WriteFromPcmBuffer(outputPath, buffer, sampleRate, numChannels, _logger), ct);
    }
}
