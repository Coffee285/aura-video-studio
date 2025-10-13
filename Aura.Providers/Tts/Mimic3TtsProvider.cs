using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Audio;
using Aura.Core.Models;
using Aura.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Tts;

/// <summary>
/// Mimic3 TTS provider - offline HTTP server-based TTS
/// https://github.com/MycroftAI/mimic3
/// </summary>
public class Mimic3TtsProvider : ITtsProvider
{
    private readonly ILogger<Mimic3TtsProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _outputDirectory;

    public Mimic3TtsProvider(
        ILogger<Mimic3TtsProvider> logger,
        HttpClient httpClient,
        string baseUrl = "http://127.0.0.1:59125")
    {
        _logger = logger;
        _httpClient = httpClient;
        _baseUrl = baseUrl;
        _outputDirectory = Path.Combine(Path.GetTempPath(), "AuraVideoStudio", "TTS", "Mimic3");

        if (!Directory.Exists(_outputDirectory))
        {
            Directory.CreateDirectory(_outputDirectory);
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
    {
        try
        {
            _logger.LogInformation("Fetching available voices from Mimic3 at {Url}", _baseUrl);

            var response = await _httpClient.GetAsync($"{_baseUrl}/api/voices");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch voices from Mimic3: {StatusCode}", response.StatusCode);
                return new List<string> { "en_US/vctk_low" }; // Default voice
            }

            var json = await response.Content.ReadAsStringAsync();
            var voices = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            if (voices != null && voices.Count > 0)
            {
                return voices.Keys.ToList();
            }

            return new List<string> { "en_US/vctk_low" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching voices from Mimic3, using default");
            return new List<string> { "en_US/vctk_low" };
        }
    }

    public async Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec spec, CancellationToken ct)
    {
        _logger.LogInformation("Synthesizing speech with Mimic3 TTS using voice {Voice}", spec.VoiceName);

        // Check if server is reachable
        if (!await IsServerHealthyAsync(ct))
        {
            throw new InvalidOperationException($"Mimic3 server is not reachable at {_baseUrl}");
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

                var success = await SynthesizeLineAsync(line.Text, spec.VoiceName, outputPath, ct);

                if (!success)
                {
                    _logger.LogWarning("Failed to synthesize line {Index}", i);
                    throw new InvalidOperationException($"Failed to synthesize line {i}");
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
            _logger.LogError(ex, "Error during Mimic3 TTS synthesis");
            
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

    public async Task<bool> IsServerHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await _httpClient.GetAsync($"{_baseUrl}/api/voices", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Mimic3 health check failed");
            return false;
        }
    }

    private async Task<bool> SynthesizeLineAsync(string text, string voice, string outputPath, CancellationToken ct)
    {
        try
        {
            // Mimic3 API: POST /api/tts?voice=voice_name with text in body
            var url = $"{_baseUrl}/api/tts?voice={Uri.EscapeDataString(voice)}";
            
            var content = new StringContent(text, Encoding.UTF8, "text/plain");
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync(url, content, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Mimic3 synthesis failed: {StatusCode} - {Error}", response.StatusCode, error);
                return false;
            }

            // Save WAV data to file
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            await response.Content.CopyToAsync(fs, ct);

            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error synthesizing with Mimic3");
            return false;
        }
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
