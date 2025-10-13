using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Piper TTS provider - lightweight, offline, fast CLI-based TTS
/// https://github.com/rhasspy/piper
/// </summary>
public class PiperTtsProvider : ITtsProvider
{
    private readonly ILogger<PiperTtsProvider> _logger;
    private readonly string _piperExecutable;
    private readonly string _voiceModelPath;
    private readonly string _outputDirectory;

    public PiperTtsProvider(
        ILogger<PiperTtsProvider> logger,
        string piperExecutable,
        string voiceModelPath)
    {
        _logger = logger;
        _piperExecutable = piperExecutable;
        _voiceModelPath = voiceModelPath;
        _outputDirectory = Path.Combine(Path.GetTempPath(), "AuraVideoStudio", "TTS", "Piper");

        if (!Directory.Exists(_outputDirectory))
        {
            Directory.CreateDirectory(_outputDirectory);
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
    {
        await Task.CompletedTask;
        
        // In a full implementation, this would scan the voice models directory
        // For now, return the configured voice
        var voiceName = Path.GetFileNameWithoutExtension(_voiceModelPath);
        return new List<string> { voiceName };
    }

    public async Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec spec, CancellationToken ct)
    {
        _logger.LogInformation("Synthesizing speech with Piper TTS using voice model {Model}", _voiceModelPath);

        if (!File.Exists(_piperExecutable))
        {
            throw new FileNotFoundException($"Piper executable not found at {_piperExecutable}");
        }

        if (!File.Exists(_voiceModelPath))
        {
            throw new FileNotFoundException($"Voice model not found at {_voiceModelPath}");
        }

        var linesList = lines.ToList();
        var segmentPaths = new List<string>();

        try
        {
            // Synthesize each line separately
            for (int i = 0; i < linesList.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var line = linesList[i];
                var outputPath = Path.Combine(_outputDirectory, $"segment_{i:D4}.wav");

                _logger.LogDebug("Synthesizing line {Index}: {Text}", i, line.Text);

                // Run Piper CLI: echo "text" | piper --model voice.onnx --output_file output.wav
                var success = await RunPiperAsync(line.Text, outputPath, ct);

                if (!success)
                {
                    _logger.LogWarning("Failed to synthesize line {Index}, creating silence", i);
                    CreateSilenceWav(outputPath, (int)line.Duration.TotalSeconds);
                }

                segmentPaths.Add(outputPath);
            }

            // Merge all segments into final narration file
            var finalPath = Path.Combine(_outputDirectory, $"narration_{DateTime.Now:yyyyMMddHHmmss}.wav");
            _logger.LogInformation("Merging {Count} segments into {Path}", segmentPaths.Count, finalPath);

            MergeWavFiles(segmentPaths, finalPath);

            // Clean up segment files
            foreach (var segment in segmentPaths)
            {
                try
                {
                    if (File.Exists(segment))
                    {
                        File.Delete(segment);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary segment {Path}", segment);
                }
            }

            return finalPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Piper TTS synthesis");
            
            // Clean up on error
            foreach (var segment in segmentPaths)
            {
                try
                {
                    if (File.Exists(segment))
                    {
                        File.Delete(segment);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            throw;
        }
    }

    private async Task<bool> RunPiperAsync(string text, string outputPath, CancellationToken ct)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = _piperExecutable,
                Arguments = $"--model \"{_voiceModelPath}\" --output_file \"{outputPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            
            process.Start();

            // Write text to stdin
            await process.StandardInput.WriteLineAsync(text);
            process.StandardInput.Close();

            // Wait for completion with cancellation support
            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                return true;
            }

            var error = await process.StandardError.ReadToEndAsync(ct);
            _logger.LogWarning("Piper process failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running Piper process");
            return false;
        }
    }

    private void CreateSilenceWav(string outputPath, int durationSeconds)
    {
        // Use the atomic write utility for creating silent WAV files
        int durationMs = durationSeconds * 1000;
        WavFileWriter.WritePcm16Silent(outputPath, durationMs, sampleRate: 22050, channels: 1, logger: _logger);
    }

    private void MergeWavFiles(List<string> inputFiles, string outputFile)
    {
        if (inputFiles.Count == 0)
        {
            throw new ArgumentException("No input files to merge");
        }

        if (inputFiles.Count == 1)
        {
            File.Copy(inputFiles[0], outputFile, overwrite: true);
            return;
        }

        // Simple concatenation for WAV files with same format
        // Read first file to get header
        using var firstFile = new FileStream(inputFiles[0], FileMode.Open, FileAccess.Read);
        byte[] header = new byte[44];
        firstFile.Read(header, 0, 44);

        // Calculate total data size
        long totalDataSize = 0;
        foreach (var file in inputFiles)
        {
            var info = new FileInfo(file);
            totalDataSize += info.Length - 44; // Subtract WAV header
        }

        // Write output file
        using var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(output);

        // Write RIFF header with updated size
        writer.Write(header, 0, 4); // "RIFF"
        writer.Write((int)(totalDataSize + 36)); // File size - 8
        writer.Write(header, 8, 36); // Rest of header

        // Copy data from all input files
        byte[] buffer = new byte[8192];
        foreach (var file in inputFiles)
        {
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read);
            input.Seek(44, SeekOrigin.Begin); // Skip header
            
            int bytesRead;
            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.Write(buffer, 0, bytesRead);
            }
        }
    }
}
