using System;
using System.IO;
using Aura.Core.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aura.Tests;

public class WavFileWriterTests
{
    [Fact]
    public void WritePcm16Silent_Should_CreateValidWavFile()
    {
        // Arrange
        string outputPath = Path.Combine(Path.GetTempPath(), $"test-silent-{Guid.NewGuid()}.wav");

        try
        {
            // Act
            WavFileWriter.WritePcm16Silent(outputPath, durationMs: 500, sampleRate: 48000, channels: 2);

            // Assert
            Assert.True(File.Exists(outputPath));
            
            // Verify file size is greater than minimum (header + some data)
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 128, $"Expected file size >128 bytes, got {fileInfo.Length}");

            // Verify WAV header
            using var stream = File.OpenRead(outputPath);
            using var reader = new BinaryReader(stream);

            var riff = new string(reader.ReadChars(4));
            Assert.Equal("RIFF", riff);

            int fileSize = reader.ReadInt32();
            
            var wave = new string(reader.ReadChars(4));
            Assert.Equal("WAVE", wave);

            // Read fmt chunk
            var fmt = new string(reader.ReadChars(4));
            Assert.Equal("fmt ", fmt);
            
            int fmtSize = reader.ReadInt32();
            Assert.Equal(16, fmtSize); // PCM format
            
            short audioFormat = reader.ReadInt16();
            Assert.Equal(1, audioFormat); // PCM
            
            short numChannels = reader.ReadInt16();
            Assert.Equal(2, numChannels);
            
            int sampleRate = reader.ReadInt32();
            Assert.Equal(48000, sampleRate);
            
            int byteRate = reader.ReadInt32();
            short blockAlign = reader.ReadInt16();
            short bitsPerSample = reader.ReadInt16();
            Assert.Equal(16, bitsPerSample);

            // Read data chunk
            var data = new string(reader.ReadChars(4));
            Assert.Equal("data", data);
            
            int dataSize = reader.ReadInt32();
            Assert.True(dataSize > 0);
        }
        finally
        {
            // Cleanup
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void WritePcm16Silent_Should_CreateCorrectDuration()
    {
        // Arrange
        string outputPath = Path.Combine(Path.GetTempPath(), $"test-duration-{Guid.NewGuid()}.wav");
        int durationMs = 1000; // 1 second
        int sampleRate = 48000;
        int channels = 2;

        try
        {
            // Act
            WavFileWriter.WritePcm16Silent(outputPath, durationMs, sampleRate, channels);

            // Assert
            using var stream = File.OpenRead(outputPath);
            using var reader = new BinaryReader(stream);

            // Skip to fmt chunk
            reader.BaseStream.Seek(12, SeekOrigin.Begin); // Skip RIFF header and WAVE
            var fmt = new string(reader.ReadChars(4));
            int fmtSize = reader.ReadInt32();
            reader.ReadInt16(); // Audio format
            short numChannels = reader.ReadInt16();
            int actualSampleRate = reader.ReadInt32();
            reader.ReadInt32(); // Byte rate
            reader.ReadInt16(); // Block align
            short bitsPerSample = reader.ReadInt16();

            // Skip to data chunk
            var data = new string(reader.ReadChars(4));
            int dataSize = reader.ReadInt32();

            // Calculate duration from data size
            int bytesPerSample = numChannels * (bitsPerSample / 8);
            double actualDurationMs = (double)dataSize / (actualSampleRate * bytesPerSample) * 1000.0;

            // Allow for rounding, should be close to requested duration
            Assert.InRange(actualDurationMs, durationMs * 0.99, durationMs * 1.01);
        }
        finally
        {
            // Cleanup
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void WriteFromPcmBuffer_Should_CreateValidWavFile()
    {
        // Arrange
        string outputPath = Path.Combine(Path.GetTempPath(), $"test-buffer-{Guid.NewGuid()}.wav");
        int sampleRate = 44100;
        int channels = 1;
        
        // Create a simple buffer with some samples (sine wave)
        int numSamples = sampleRate; // 1 second
        short[] buffer = new short[numSamples];
        for (int i = 0; i < numSamples; i++)
        {
            double time = (double)i / sampleRate;
            buffer[i] = (short)(short.MaxValue * 0.3 * Math.Sin(2.0 * Math.PI * 440.0 * time));
        }

        try
        {
            // Act
            WavFileWriter.WriteFromPcmBuffer(outputPath, buffer, sampleRate, channels);

            // Assert
            Assert.True(File.Exists(outputPath));
            
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 128);

            // Verify header
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
            // Cleanup
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void WritePcm16Silent_Should_UseAtomicWrite()
    {
        // Arrange
        string outputPath = Path.Combine(Path.GetTempPath(), $"test-atomic-{Guid.NewGuid()}.wav");
        string tempPath = outputPath + ".tmp";

        try
        {
            // Act
            WavFileWriter.WritePcm16Silent(outputPath, durationMs: 250, sampleRate: 48000, channels: 2);

            // Assert - temp file should not exist after successful write
            Assert.False(File.Exists(tempPath), "Temp file should be cleaned up after atomic write");
            Assert.True(File.Exists(outputPath), "Final file should exist");
            
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 128, "Final file should be valid and >128 bytes");
        }
        finally
        {
            // Cleanup
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void WritePcm16Silent_Should_HandleZeroDuration()
    {
        // Arrange
        string outputPath = Path.Combine(Path.GetTempPath(), $"test-zero-{Guid.NewGuid()}.wav");

        try
        {
            // Act
            WavFileWriter.WritePcm16Silent(outputPath, durationMs: 0, sampleRate: 48000, channels: 2);

            // Assert - should still create a valid WAV file (just with minimal samples)
            Assert.True(File.Exists(outputPath));
            
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length >= 44, "Should at least have WAV header (44 bytes)");
            
            // Verify it's still a valid WAV
            using var stream = File.OpenRead(outputPath);
            using var reader = new BinaryReader(stream);
            
            var riff = new string(reader.ReadChars(4));
            Assert.Equal("RIFF", riff);
        }
        finally
        {
            // Cleanup
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void WritePcm16Silent_Should_SupportMono()
    {
        // Arrange
        string outputPath = Path.Combine(Path.GetTempPath(), $"test-mono-{Guid.NewGuid()}.wav");

        try
        {
            // Act
            WavFileWriter.WritePcm16Silent(outputPath, durationMs: 500, sampleRate: 44100, channels: 1);

            // Assert
            Assert.True(File.Exists(outputPath));
            
            using var stream = File.OpenRead(outputPath);
            using var reader = new BinaryReader(stream);

            // Skip to fmt chunk
            reader.BaseStream.Seek(12, SeekOrigin.Begin);
            var fmt = new string(reader.ReadChars(4));
            int fmtSize = reader.ReadInt32();
            reader.ReadInt16(); // Audio format
            short numChannels = reader.ReadInt16();
            
            Assert.Equal(1, numChannels);
        }
        finally
        {
            // Cleanup
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void WritePcm16Silent_Should_ThrowOnInvalidParameters()
    {
        // Arrange
        string outputPath = Path.Combine(Path.GetTempPath(), $"test-invalid-{Guid.NewGuid()}.wav");

        // Act & Assert - negative duration
        Assert.Throws<ArgumentException>(() => 
            WavFileWriter.WritePcm16Silent(outputPath, durationMs: -1, sampleRate: 48000, channels: 2));

        // Act & Assert - invalid sample rate
        Assert.Throws<ArgumentException>(() => 
            WavFileWriter.WritePcm16Silent(outputPath, durationMs: 100, sampleRate: 0, channels: 2));

        // Act & Assert - invalid channels
        Assert.Throws<ArgumentException>(() => 
            WavFileWriter.WritePcm16Silent(outputPath, durationMs: 100, sampleRate: 48000, channels: 0));

        Assert.Throws<ArgumentException>(() => 
            WavFileWriter.WritePcm16Silent(outputPath, durationMs: 100, sampleRate: 48000, channels: 3));
    }

    [Fact]
    public void WriteFromPcmBuffer_Should_ThrowOnInvalidParameters()
    {
        // Arrange
        string outputPath = Path.Combine(Path.GetTempPath(), $"test-invalid-buffer-{Guid.NewGuid()}.wav");
        short[] validBuffer = new short[100];

        // Act & Assert - null buffer
        Assert.Throws<ArgumentException>(() => 
            WavFileWriter.WriteFromPcmBuffer(outputPath, null!, sampleRate: 48000, channels: 2));

        // Act & Assert - empty buffer
        Assert.Throws<ArgumentException>(() => 
            WavFileWriter.WriteFromPcmBuffer(outputPath, Array.Empty<short>(), sampleRate: 48000, channels: 2));

        // Act & Assert - invalid sample rate
        Assert.Throws<ArgumentException>(() => 
            WavFileWriter.WriteFromPcmBuffer(outputPath, validBuffer, sampleRate: -1, channels: 2));

        // Act & Assert - invalid channels
        Assert.Throws<ArgumentException>(() => 
            WavFileWriter.WriteFromPcmBuffer(outputPath, validBuffer, sampleRate: 48000, channels: 5));
    }
}
