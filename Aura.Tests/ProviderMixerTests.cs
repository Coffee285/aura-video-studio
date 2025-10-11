using System.Collections.Generic;
using Aura.Core.Models;
using Aura.Core.Orchestrator;
using Aura.Core.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Aura.Tests;

public class ProviderMixerTests
{
    [Fact]
    public void SelectLlmProvider_Should_PreferProWhenAvailable()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var providers = new Dictionary<string, ILlmProvider>
        {
            ["OpenAI"] = Mock.Of<ILlmProvider>(),
            ["RuleBased"] = Mock.Of<ILlmProvider>()
        };

        // Act
        var selection = mixer.SelectLlmProvider(providers, "Pro");

        // Assert
        Assert.Equal("OpenAI", selection.SelectedProvider);
        Assert.False(selection.IsFallback);
        Assert.Equal("Script", selection.Stage);
    }

    [Fact]
    public void SelectLlmProvider_Should_FallbackToFreeWhenProUnavailable()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var providers = new Dictionary<string, ILlmProvider>
        {
            ["RuleBased"] = Mock.Of<ILlmProvider>()
        };

        // Act
        var selection = mixer.SelectLlmProvider(providers, "Pro");

        // Assert
        Assert.Equal("RuleBased", selection.SelectedProvider);
        Assert.True(selection.IsFallback);
        Assert.NotNull(selection.FallbackFrom);
    }

    [Fact]
    public void SelectLlmProvider_Should_PreferOllamaOverRuleBased()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var providers = new Dictionary<string, ILlmProvider>
        {
            ["Ollama"] = Mock.Of<ILlmProvider>(),
            ["RuleBased"] = Mock.Of<ILlmProvider>()
        };

        // Act
        var selection = mixer.SelectLlmProvider(providers, "Free");

        // Assert
        Assert.Equal("Ollama", selection.SelectedProvider);
        Assert.False(selection.IsFallback);
    }

    [Fact]
    public void SelectLlmProvider_Should_UseProIfAvailableLogic()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        // Pro available
        var providersWithPro = new Dictionary<string, ILlmProvider>
        {
            ["OpenAI"] = Mock.Of<ILlmProvider>(),
            ["Ollama"] = Mock.Of<ILlmProvider>()
        };

        var selection1 = mixer.SelectLlmProvider(providersWithPro, "ProIfAvailable");
        Assert.Equal("OpenAI", selection1.SelectedProvider);
        Assert.False(selection1.IsFallback);

        // Pro not available
        var providersWithoutPro = new Dictionary<string, ILlmProvider>
        {
            ["Ollama"] = Mock.Of<ILlmProvider>()
        };

        var selection2 = mixer.SelectLlmProvider(providersWithoutPro, "ProIfAvailable");
        Assert.Equal("Ollama", selection2.SelectedProvider);
        Assert.True(selection2.IsFallback);
    }

    [Fact]
    public void SelectTtsProvider_Should_PreferProWhenAvailable()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var providers = new Dictionary<string, ITtsProvider>
        {
            ["ElevenLabs"] = Mock.Of<ITtsProvider>(),
            ["Windows"] = Mock.Of<ITtsProvider>()
        };

        // Act
        var selection = mixer.SelectTtsProvider(providers, "Pro");

        // Assert
        Assert.Equal("ElevenLabs", selection.SelectedProvider);
        Assert.False(selection.IsFallback);
        Assert.Equal("TTS", selection.Stage);
    }

    [Fact]
    public void SelectTtsProvider_Should_FallbackToWindowsWhenProUnavailable()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var providers = new Dictionary<string, ITtsProvider>
        {
            ["Windows"] = Mock.Of<ITtsProvider>()
        };

        // Act
        var selection = mixer.SelectTtsProvider(providers, "Pro");

        // Assert
        Assert.Equal("Windows", selection.SelectedProvider);
        Assert.True(selection.IsFallback);
        Assert.Equal("Pro/Local TTS", selection.FallbackFrom);
    }

    [Fact]
    public void SelectVisualProvider_Should_RequireNvidiaForSD()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var providers = new Dictionary<string, object>
        {
            ["StableDiffusion"] = new object(),
            ["Stock"] = new object()
        };

        // NVIDIA GPU with sufficient VRAM - should use SD
        var selection1 = mixer.SelectVisualProvider(providers, "StockOrLocal", isNvidiaGpu: true, vramGB: 10);
        Assert.Equal("StableDiffusion", selection1.SelectedProvider);

        // AMD GPU with sufficient VRAM - should NOT use SD
        var selection2 = mixer.SelectVisualProvider(providers, "StockOrLocal", isNvidiaGpu: false, vramGB: 16);
        Assert.Equal("Stock", selection2.SelectedProvider);

        // NVIDIA GPU with insufficient VRAM - should NOT use SD
        var selection3 = mixer.SelectVisualProvider(providers, "StockOrLocal", isNvidiaGpu: true, vramGB: 4);
        Assert.Equal("Stock", selection3.SelectedProvider);
    }

    [Fact]
    public void SelectVisualProvider_Should_FallbackToSlideshow()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var providers = new Dictionary<string, object>(); // No providers

        // Act
        var selection = mixer.SelectVisualProvider(providers, "Pro", isNvidiaGpu: false, vramGB: 0);

        // Assert
        Assert.Equal("Slideshow", selection.SelectedProvider);
        Assert.True(selection.IsFallback);
    }

    [Fact]
    public void ProviderProfile_Should_HaveCorrectPresets()
    {
        // Assert
        var freeOnly = ProviderProfile.FreeOnly;
        Assert.Equal("Free-Only", freeOnly.Name);
        Assert.Equal("Free", freeOnly.Stages["Script"]);
        Assert.Equal("Windows", freeOnly.Stages["TTS"]);
        Assert.Equal("Stock", freeOnly.Stages["Visuals"]);

        var balanced = ProviderProfile.BalancedMix;
        Assert.Equal("Balanced Mix", balanced.Name);
        Assert.Equal("ProIfAvailable", balanced.Stages["Script"]);
        Assert.Equal("StockOrLocal", balanced.Stages["Visuals"]);

        var proMax = ProviderProfile.ProMax;
        Assert.Equal("Pro-Max", proMax.Name);
        Assert.Equal("Pro", proMax.Stages["Script"]);
        Assert.Equal("Pro", proMax.Stages["TTS"]);
        Assert.Equal("Pro", proMax.Stages["Visuals"]);
    }

    [Fact]
    public void SelectLlmProvider_Should_NeverThrowException_EmptyProviders()
    {
        // Arrange - This is the critical fix for "No LLM providers available" error
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var emptyProviders = new Dictionary<string, ILlmProvider>(); // NO providers at all

        // Act - Should NOT throw, should return RuleBased as guaranteed fallback
        var selection = mixer.SelectLlmProvider(emptyProviders, "Pro");

        // Assert
        Assert.NotNull(selection);
        Assert.Equal("RuleBased", selection.SelectedProvider);
        Assert.True(selection.IsFallback);
        Assert.Contains("guaranteed", selection.Reason, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectTtsProvider_Should_NeverThrowException_EmptyProviders()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var emptyProviders = new Dictionary<string, ITtsProvider>(); // NO providers at all

        // Act - Should NOT throw, should return Windows as guaranteed fallback
        var selection = mixer.SelectTtsProvider(emptyProviders, "Pro");

        // Assert
        Assert.NotNull(selection);
        Assert.Equal("Windows", selection.SelectedProvider);
        Assert.True(selection.IsFallback);
        Assert.Contains("guaranteed", selection.Reason, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectLlmProvider_Should_UseSpecificProviderWhenRequested()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var providers = new Dictionary<string, ILlmProvider>
        {
            ["OpenAI"] = Mock.Of<ILlmProvider>(),
            ["Ollama"] = Mock.Of<ILlmProvider>(),
            ["RuleBased"] = Mock.Of<ILlmProvider>()
        };

        // Act - Request specific provider by name
        var selection1 = mixer.SelectLlmProvider(providers, "Ollama");
        var selection2 = mixer.SelectLlmProvider(providers, "OpenAI");
        var selection3 = mixer.SelectLlmProvider(providers, "RuleBased");

        // Assert
        Assert.Equal("Ollama", selection1.SelectedProvider);
        Assert.False(selection1.IsFallback);

        Assert.Equal("OpenAI", selection2.SelectedProvider);
        Assert.False(selection2.IsFallback);

        Assert.Equal("RuleBased", selection3.SelectedProvider);
        Assert.False(selection3.IsFallback);
    }

    [Theory]
    [InlineData("Pro")]
    [InlineData("ProIfAvailable")]
    [InlineData("Free")]
    [InlineData("")]
    [InlineData(null)]
    public void ProviderMixer_AlwaysReturnsProvider_NeverThrows(string preferredTier)
    {
        // Arrange - This is the critical acceptance test
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var emptyLlmProviders = new Dictionary<string, ILlmProvider>();
        var emptyTtsProviders = new Dictionary<string, ITtsProvider>();
        var emptyVisualProviders = new Dictionary<string, object>();

        // Act & Assert - Should NEVER throw, always returns a fallback
        var llmSelection = mixer.SelectLlmProvider(emptyLlmProviders, preferredTier ?? "Free");
        Assert.NotNull(llmSelection);
        Assert.NotNull(llmSelection.SelectedProvider);
        Assert.Equal("RuleBased", llmSelection.SelectedProvider);

        var ttsSelection = mixer.SelectTtsProvider(emptyTtsProviders, preferredTier ?? "Free");
        Assert.NotNull(ttsSelection);
        Assert.NotNull(ttsSelection.SelectedProvider);
        Assert.Equal("Windows", ttsSelection.SelectedProvider);

        var visualSelection = mixer.SelectVisualProvider(emptyVisualProviders, preferredTier ?? "Free", false, 0);
        Assert.NotNull(visualSelection);
        Assert.NotNull(visualSelection.SelectedProvider);
        Assert.True(visualSelection.SelectedProvider == "Slideshow"); // Ultimate fallback
    }

    [Fact]
    public void SelectLlmProvider_Should_ReturnRuleBasedForAllTiers_WhenNoProvidersAvailable()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);
        var emptyProviders = new Dictionary<string, ILlmProvider>();

        // Act - Test all tier options
        var proSelection = mixer.SelectLlmProvider(emptyProviders, "Pro");
        var proIfAvailableSelection = mixer.SelectLlmProvider(emptyProviders, "ProIfAvailable");
        var freeSelection = mixer.SelectLlmProvider(emptyProviders, "Free");
        var emptySelection = mixer.SelectLlmProvider(emptyProviders, "");
        var nullSelection = mixer.SelectLlmProvider(emptyProviders, null!);

        // Assert - All should return RuleBased as guaranteed fallback
        Assert.Equal("RuleBased", proSelection.SelectedProvider);
        Assert.True(proSelection.IsFallback);
        Assert.Equal("All providers", proSelection.FallbackFrom);

        Assert.Equal("RuleBased", proIfAvailableSelection.SelectedProvider);
        Assert.True(proIfAvailableSelection.IsFallback);

        Assert.Equal("RuleBased", freeSelection.SelectedProvider);
        Assert.True(freeSelection.IsFallback);

        Assert.Equal("RuleBased", emptySelection.SelectedProvider);
        Assert.True(emptySelection.IsFallback);

        Assert.Equal("RuleBased", nullSelection.SelectedProvider);
        Assert.True(nullSelection.IsFallback);
    }

    [Fact]
    public void SelectTtsProvider_Should_ReturnWindowsForAllTiers_WhenNoProvidersAvailable()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);
        var emptyProviders = new Dictionary<string, ITtsProvider>();

        // Act
        var proSelection = mixer.SelectTtsProvider(emptyProviders, "Pro");
        var proIfAvailableSelection = mixer.SelectTtsProvider(emptyProviders, "ProIfAvailable");
        var freeSelection = mixer.SelectTtsProvider(emptyProviders, "Free");

        // Assert - All should return Windows as guaranteed fallback
        Assert.Equal("Windows", proSelection.SelectedProvider);
        Assert.True(proSelection.IsFallback);
        Assert.Equal("All TTS providers", proSelection.FallbackFrom);

        Assert.Equal("Windows", proIfAvailableSelection.SelectedProvider);
        Assert.True(proIfAvailableSelection.IsFallback);

        Assert.Equal("Windows", freeSelection.SelectedProvider);
        Assert.True(freeSelection.IsFallback);
    }

    [Fact]
    public void SelectVisualProvider_Should_ReturnSlideshowForAllTiers_WhenNoProvidersAvailable()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);
        var emptyProviders = new Dictionary<string, object>();

        // Act
        var proSelection = mixer.SelectVisualProvider(emptyProviders, "Pro", false, 0);
        var proIfAvailableSelection = mixer.SelectVisualProvider(emptyProviders, "ProIfAvailable", false, 0);
        var freeSelection = mixer.SelectVisualProvider(emptyProviders, "Free", false, 0);
        var stockOrLocalSelection = mixer.SelectVisualProvider(emptyProviders, "StockOrLocal", false, 0);

        // Assert - All should return Slideshow as guaranteed fallback
        Assert.Equal("Slideshow", proSelection.SelectedProvider);
        Assert.True(proSelection.IsFallback);
        Assert.Equal("All visual providers", proSelection.FallbackFrom);

        Assert.Equal("Slideshow", proIfAvailableSelection.SelectedProvider);
        Assert.True(proIfAvailableSelection.IsFallback);

        Assert.Equal("Slideshow", freeSelection.SelectedProvider);
        Assert.True(freeSelection.IsFallback);

        Assert.Equal("Slideshow", stockOrLocalSelection.SelectedProvider);
        Assert.True(stockOrLocalSelection.IsFallback);
    }

    [Fact]
    public void SelectLlmProvider_Should_PreferHigherTierWhenAvailable()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        var allProviders = new Dictionary<string, ILlmProvider>
        {
            ["OpenAI"] = Mock.Of<ILlmProvider>(),
            ["Azure"] = Mock.Of<ILlmProvider>(),
            ["Gemini"] = Mock.Of<ILlmProvider>(),
            ["Ollama"] = Mock.Of<ILlmProvider>(),
            ["RuleBased"] = Mock.Of<ILlmProvider>()
        };

        // Act - Request Pro tier with all providers available
        var selection = mixer.SelectLlmProvider(allProviders, "Pro");

        // Assert - Should select OpenAI (highest priority Pro provider)
        Assert.Equal("OpenAI", selection.SelectedProvider);
        Assert.False(selection.IsFallback);
    }

    [Fact]
    public void SelectLlmProvider_Should_FollowFallbackChain_ProTier()
    {
        // Arrange
        var config = new ProviderMixingConfig { LogProviderSelection = false };
        var mixer = new ProviderMixer(NullLogger<ProviderMixer>.Instance, config);

        // Test fallback chain: OpenAI → Azure → Gemini → Ollama → RuleBased
        
        // Only Ollama and RuleBased available
        var providers1 = new Dictionary<string, ILlmProvider>
        {
            ["Ollama"] = Mock.Of<ILlmProvider>(),
            ["RuleBased"] = Mock.Of<ILlmProvider>()
        };
        var selection1 = mixer.SelectLlmProvider(providers1, "Pro");
        Assert.Equal("Ollama", selection1.SelectedProvider);
        Assert.True(selection1.IsFallback);

        // Only RuleBased available
        var providers2 = new Dictionary<string, ILlmProvider>
        {
            ["RuleBased"] = Mock.Of<ILlmProvider>()
        };
        var selection2 = mixer.SelectLlmProvider(providers2, "Pro");
        Assert.Equal("RuleBased", selection2.SelectedProvider);
        Assert.True(selection2.IsFallback);

        // No providers available - should still return RuleBased
        var providers3 = new Dictionary<string, ILlmProvider>();
        var selection3 = mixer.SelectLlmProvider(providers3, "Pro");
        Assert.Equal("RuleBased", selection3.SelectedProvider);
        Assert.True(selection3.IsFallback);
    }
}
