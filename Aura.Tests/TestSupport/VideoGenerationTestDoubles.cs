using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Dependencies;
using Aura.Core.Hardware;
using Aura.Core.Models;
using Aura.Core.Models.Audio;
using Aura.Core.Models.Generation;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Aura.Core.Services.Audio;
using Aura.Core.Services.Generation;

namespace Aura.Tests.TestSupport;

/// <summary>
/// Shared test doubles used across video generation pipeline tests.
/// </summary>
internal sealed class MockHardwareDetector : IHardwareDetector
{
    public Task<SystemProfile> DetectSystemAsync()
    {
        var profile = new SystemProfile
        {
            AutoDetect = true,
            LogicalCores = Environment.ProcessorCount,
            PhysicalCores = Math.Max(1, Environment.ProcessorCount / 2),
            RamGB = 16,
            Gpu = new GpuInfo("MockGPU", "Mock Model", 8, "10"),
            Tier = HardwareTier.B,
            EnableNVENC = true,
            EnableSD = true,
            OfflineOnly = false
        };

        return Task.FromResult(profile);
    }

    public SystemProfile ApplyManualOverrides(SystemProfile detected, HardwareOverrides overrides)
    {
        return detected;
    }

    public Task RunHardwareProbeAsync()
    {
        return Task.CompletedTask;
    }
}

internal sealed class MockFfmpegLocator : IFfmpegLocator
{
    private readonly string _mockPath;

    public MockFfmpegLocator()
    {
        // Create a temporary file to simulate FFmpeg binary
        _mockPath = Path.Combine(Path.GetTempPath(), $"ffmpeg-mock-{Guid.NewGuid():N}.exe");
        File.WriteAllText(_mockPath, "mock");
    }

    public Task<string> GetEffectiveFfmpegPathAsync(string? configuredPath = null, CancellationToken ct = default)
    {
        return Task.FromResult(_mockPath);
    }

    public Task<FfmpegValidationResult> CheckAllCandidatesAsync(string? configuredPath = null, CancellationToken ct = default)
    {
        return Task.FromResult(new FfmpegValidationResult
        {
            Found = true,
            FfmpegPath = _mockPath,
            VersionString = "mock-4.4.0",
            ValidationOutput = "mock ffmpeg",
            Reason = "Mock FFmpeg locator",
            HasX264 = true,
            Source = "mock"
        });
    }

    public Task<FfmpegValidationResult> ValidatePathAsync(string ffmpegPath, CancellationToken ct = default)
    {
        return Task.FromResult(new FfmpegValidationResult
        {
            Found = true,
            FfmpegPath = ffmpegPath,
            VersionString = "mock-4.4.0",
            ValidationOutput = "mock ffmpeg",
            Reason = "Mock validation",
            HasX264 = true,
            Source = "mock"
        });
    }
}

internal sealed class MockLlmProvider : ILlmProvider
{
    public bool SupportsStreaming => false;

    public LlmProviderCharacteristics GetCharacteristics()
    {
        return new LlmProviderCharacteristics
        {
            IsLocal = false,
            ExpectedFirstTokenMs = 100,
            ExpectedTokensPerSec = 50,
            SupportsStreaming = false,
            ProviderTier = "Test",
            CostPer1KTokens = 0m
        };
    }

    public async IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(
        Brief brief, 
        PlanSpec spec, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await DraftScriptAsync(brief, spec, ct).ConfigureAwait(false);
        yield return new LlmStreamChunk
        {
            ProviderName = "Mock",
            Content = result,
            AccumulatedContent = result,
            TokenIndex = 1,
            IsFinal = true
        };
    }

    public bool DraftScriptCalled { get; private set; }

    public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        DraftScriptCalled = true;

        // Provide a markdown-formatted script with two scenes to satisfy validators
        var builder = new StringBuilder();
        builder.AppendLine($"# {brief.Topic ?? "Aura Video"}");
        builder.AppendLine();
        builder.AppendLine("## Scene 1");
        builder.AppendLine("Aura Video Studio streamlines video creation with guided workflows and smart defaults.");
        builder.AppendLine("The interface walks creators from initial brief through script, visuals, and final render.");
        builder.AppendLine();
        builder.AppendLine("## Scene 2");
        builder.AppendLine("With local providers like Ollama and powerful cloud APIs, teams can adapt generation to any budget.");
        builder.AppendLine("Aura handles narration, assets, and rendering so storytellers can stay focused on message.");

        return Task.FromResult(builder.ToString());
    }

    public Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        return Task.FromResult("Mock completion response");
    }

    public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        return Task.FromResult<SceneAnalysisResult?>(null);
    }

    public Task<VisualPromptResult?> GenerateVisualPromptAsync(
        string sceneText,
        string? previousSceneText,
        string videoTone,
        VisualStyle targetStyle,
        CancellationToken ct)
    {
        return Task.FromResult<VisualPromptResult?>(null);
    }

    public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        return Task.FromResult<ContentComplexityAnalysisResult?>(null);
    }

    public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        return Task.FromResult<SceneCoherenceResult?>(null);
    }

    public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        return Task.FromResult<NarrativeArcResult?>(null);
    }

    public Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        return Task.FromResult<string?>(null);
    }
}

internal sealed class TestMockTtsProvider : ITtsProvider
{
    public bool SynthesizeCalled { get; private set; }

    public Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
    {
        IReadOnlyList<string> voices = new List<string> { "en-US-AriaNeural" };
        return Task.FromResult(voices);
    }

    public async Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec spec, CancellationToken ct)
    {
        SynthesizeCalled = true;

        var outputPath = Path.Combine(Path.GetTempPath(), $"aura-tts-{Guid.NewGuid():N}.wav");
        await GenerateSilentWaveAsync(outputPath, durationSeconds: 1.5);
        return outputPath;
    }

    private static async Task GenerateSilentWaveAsync(string path, double durationSeconds)
    {
        const int sampleRate = 44100;
        const short channels = 1;
        const short bitsPerSample = 16;
        var numSamples = (int)(sampleRate * durationSeconds);
        var dataSize = numSamples * channels * (bitsPerSample / 8);

        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: false);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        for (var i = 0; i < numSamples; i++)
        {
            writer.Write((short)0);
        }

        writer.Flush();
    }
}

internal sealed class MockVideoComposer : IVideoComposer
{
    public bool RenderCalled { get; private set; }

    public Task<string> RenderAsync(Aura.Core.Providers.Timeline timeline, RenderSpec spec, IProgress<RenderProgress> progress, CancellationToken ct)
    {
        RenderCalled = true;

        progress?.Report(new RenderProgress(100f, TimeSpan.FromSeconds(1), TimeSpan.Zero, "Complete"));

        var outputPath = Path.Combine(Path.GetTempPath(), $"aura-video-{Guid.NewGuid():N}.mp4");
        File.WriteAllText(outputPath, "mock-video");
        return Task.FromResult(outputPath);
    }
}

internal sealed class MockImageProvider : IImageProvider
{
    public async Task<IReadOnlyList<Asset>> FetchOrGenerateAsync(Scene scene, VisualSpec spec, CancellationToken ct)
    {
        var imagePath = Path.Combine(Path.GetTempPath(), $"aura-image-{Guid.NewGuid():N}.jpg");
        var minimalJpeg = new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x48,
            0x00, 0x48, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x03, 0x02, 0x02, 0x03, 0x02, 0x02, 0x03,
            0x03, 0x03, 0x03, 0x04, 0x03, 0x03, 0x04, 0x05, 0x08, 0x05, 0x05, 0x04, 0x04, 0x05, 0x0A, 0x07,
            0x07, 0x06, 0x08, 0x0C, 0x0A, 0x0C, 0x0C, 0x0B, 0x0A, 0x0B, 0x0B, 0x0D, 0x0E, 0x12, 0x10, 0x0D,
            0x0E, 0x11, 0x0E, 0x0B, 0x0B, 0x10, 0x16, 0x10, 0x11, 0x13, 0x14, 0x15, 0x15, 0x15, 0x0C, 0x0F,
            0x17, 0x18, 0x16, 0x14, 0x18, 0x12, 0x14, 0x15, 0x14, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
            0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0xFF, 0xC4, 0x00, 0x14,
            0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0xD2, 0xCF, 0x20, 0xFF,
            0xD9
        };

        await File.WriteAllBytesAsync(imagePath, minimalJpeg, ct);

        IReadOnlyList<Asset> assets = new List<Asset>
        {
            new Asset("image", imagePath, "CC0", null)
        };

        return assets;
    }
}
