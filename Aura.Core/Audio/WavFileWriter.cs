using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Aura.Core.Audio;

/// <summary>
/// Utility for writing WAV files atomically with standard formats
/// </summary>
public static class WavFileWriter
{
    /// <summary>
    /// Write a silent PCM 16-bit WAV file with specified duration and format.
    /// Uses atomic write (temp file + move) to prevent zero-byte or incomplete files.
    /// </summary>
    /// <param name="path">Output path for the WAV file</param>
    /// <param name="durationMs">Duration in milliseconds</param>
    /// <param name="sampleRate">Sample rate (default 48000)</param>
    /// <param name="channels">Number of channels (default 2 for stereo)</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public static void WritePcm16Silent(string path, int durationMs, int sampleRate = 48000, int channels = 2, ILogger? logger = null)
    {
        if (durationMs < 0)
        {
            throw new ArgumentException("Duration must be non-negative", nameof(durationMs));
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentException("Sample rate must be positive", nameof(sampleRate));
        }

        if (channels <= 0 || channels > 2)
        {
            throw new ArgumentException("Channels must be 1 (mono) or 2 (stereo)", nameof(channels));
        }

        // Create buffer with zeros for silent audio
        double durationSeconds = durationMs / 1000.0;
        int numSamples = (int)Math.Ceiling(durationSeconds * sampleRate);
        
        // Ensure at least 1 sample even for zero duration
        if (numSamples == 0)
        {
            numSamples = 1;
        }
        
        short[] buffer = new short[numSamples * channels];
        
        WriteFromPcmBuffer(path, buffer, sampleRate, channels, logger);
    }

    /// <summary>
    /// Write a PCM 16-bit WAV file from an audio buffer.
    /// Uses atomic write (temp file + move) to prevent zero-byte or incomplete files.
    /// </summary>
    /// <param name="path">Output path for the WAV file</param>
    /// <param name="buffer">PCM 16-bit samples (interleaved for stereo)</param>
    /// <param name="sampleRate">Sample rate (e.g., 48000)</param>
    /// <param name="channels">Number of channels (1 for mono, 2 for stereo)</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public static void WriteFromPcmBuffer(string path, short[] buffer, int sampleRate, int channels, ILogger? logger = null)
    {
        if (buffer == null || buffer.Length == 0)
        {
            throw new ArgumentException("Buffer cannot be null or empty", nameof(buffer));
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentException("Sample rate must be positive", nameof(sampleRate));
        }

        if (channels <= 0 || channels > 2)
        {
            throw new ArgumentException("Channels must be 1 (mono) or 2 (stereo)", nameof(channels));
        }

        // Use temp file for atomic write
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + ".tmp";
        
        try
        {
            // Write WAV file to temp location
            WriteWavFile(tempPath, buffer, sampleRate, channels);

            // Verify the file was written correctly
            if (!File.Exists(tempPath))
            {
                throw new IOException($"WAV file write failed: temp file does not exist at {tempPath}");
            }
            
            var fileInfo = new FileInfo(tempPath);
            const int minFileSize = 44; // WAV header minimum size
            if (fileInfo.Length < minFileSize)
            {
                throw new IOException($"WAV file write failed: file is {fileInfo.Length} bytes (expected >={minFileSize})");
            }

            // Atomic move to final location
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tempPath, path);

            logger?.LogInformation("Wrote WAV file: {Path} ({Samples} samples, {Rate}Hz, {Channels}ch)", 
                path, buffer.Length / channels, sampleRate, channels);
        }
        catch (Exception ex)
        {
            // Clean up temp file on error
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            
            logger?.LogError(ex, "Failed to write WAV file: {Path}", path);
            throw;
        }
    }

    /// <summary>
    /// Write WAV file with PCM 16-bit format
    /// </summary>
    private static void WriteWavFile(string path, short[] buffer, int sampleRate, int channels)
    {
        const short bitsPerSample = 16;
        int dataSize = buffer.Length * sizeof(short);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(fileStream);

        // RIFF header
        writer.Write(new[] { 'R', 'I', 'F', 'F' });
        writer.Write(36 + dataSize); // File size - 8
        writer.Write(new[] { 'W', 'A', 'V', 'E' });

        // fmt subchunk
        writer.Write(new[] { 'f', 'm', 't', ' ' });
        writer.Write(16); // Subchunk1Size (16 for PCM)
        writer.Write((short)1); // AudioFormat (1 for PCM)
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        // data subchunk
        writer.Write(new[] { 'd', 'a', 't', 'a' });
        writer.Write(dataSize);

        // Write audio samples
        foreach (var sample in buffer)
        {
            writer.Write(sample);
        }
    }
}
