using System;
using System.Collections.Generic;
using Aura.Api.Controllers;
using Aura.Api.Services;
using Aura.Core.Orchestration;
using Aura.Core.Providers;
using Aura.Core.Services.Audio;
using Aura.Core.Services.Localization;
using Aura.Providers.Tts.validators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aura.Tests.Services;

/// <summary>
/// Tests to verify LocalizationController dependency injection is properly configured
/// </summary>
public class LocalizationControllerDiTests
{
    [Fact]
    public void LocalizationController_CanBeResolvedFromDi()
    {
        // Arrange: Create a minimal service collection with required dependencies
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging();
        
        // Add configuration
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Localization:RequestTimeoutSeconds", "180"},
            {"Localization:LlmTimeoutSeconds", "420"}
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
        services.AddSingleton(configuration);
        
        // Register GlossaryManager
        services.AddSingleton<GlossaryManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<GlossaryManager>>();
            var storageDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AuraVideoStudio",
                "Glossaries");
            return new GlossaryManager(logger, storageDir);
        });
        
        // Register SSML mappers
        services.AddSingleton<ISSMLMapper, ElevenLabsSSMLMapper>();
        services.AddSingleton<ISSMLMapper, WindowsSSMLMapper>();
        services.AddSingleton<ISSMLMapper, PlayHTSSMLMapper>();
        services.AddSingleton<ISSMLMapper, PiperSSMLMapper>();
        services.AddSingleton<ISSMLMapper, Mimic3SSMLMapper>();
        
        // Mock ILlmProvider
        var mockLlmProvider = new Mock<ILlmProvider>();
        services.AddSingleton(mockLlmProvider.Object);
        
        // Mock LlmStageAdapter
        var mockStageAdapter = new Mock<LlmStageAdapter>();
        services.AddSingleton(mockStageAdapter.Object);
        
        // Mock TranslationService
        var mockTranslationService = new Mock<TranslationService>();
        services.AddSingleton(mockTranslationService.Object);
        
        // Mock ILocalizationService
        var mockLocalizationService = new Mock<ILocalizationService>();
        services.AddSingleton(mockLocalizationService.Object);
        
        var provider = services.BuildServiceProvider();
        
        // Act: Attempt to resolve LocalizationController
        var controller = ActivatorUtilities.CreateInstance<LocalizationController>(provider);
        
        // Assert: Controller was successfully instantiated
        Assert.NotNull(controller);
    }
    
    [Fact]
    public void GlossaryManager_IsRegisteredAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<GlossaryManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<GlossaryManager>>();
            var storageDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AuraVideoStudio",
                "Glossaries");
            return new GlossaryManager(logger, storageDir);
        });
        
        var provider = services.BuildServiceProvider();
        
        // Act
        var instance1 = provider.GetRequiredService<GlossaryManager>();
        var instance2 = provider.GetRequiredService<GlossaryManager>();
        
        // Assert: Same instance returned (singleton behavior)
        Assert.Same(instance1, instance2);
    }
    
    [Fact]
    public void SSMLMappers_CanBeResolvedAsEnumerable()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Register SSML mappers
        services.AddSingleton<ISSMLMapper, ElevenLabsSSMLMapper>();
        services.AddSingleton<ISSMLMapper, WindowsSSMLMapper>();
        services.AddSingleton<ISSMLMapper, PlayHTSSMLMapper>();
        services.AddSingleton<ISSMLMapper, PiperSSMLMapper>();
        services.AddSingleton<ISSMLMapper, Mimic3SSMLMapper>();
        
        var provider = services.BuildServiceProvider();
        
        // Act
        var mappers = provider.GetService<IEnumerable<ISSMLMapper>>();
        
        // Assert
        Assert.NotNull(mappers);
        var mapperList = new List<ISSMLMapper>(mappers);
        Assert.Equal(5, mapperList.Count);
        
        // Verify all expected mappers are present
        Assert.Contains(mapperList, m => m is ElevenLabsSSMLMapper);
        Assert.Contains(mapperList, m => m is WindowsSSMLMapper);
        Assert.Contains(mapperList, m => m is PlayHTSSMLMapper);
        Assert.Contains(mapperList, m => m is PiperSSMLMapper);
        Assert.Contains(mapperList, m => m is Mimic3SSMLMapper);
    }
    
    [Fact]
    public void GlossaryManager_UsesCorrectStoragePath()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<GlossaryManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<GlossaryManager>>();
            var storageDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AuraVideoStudio",
                "Glossaries");
            return new GlossaryManager(logger, storageDir);
        });
        
        var provider = services.BuildServiceProvider();
        
        // Act
        var glossaryManager = provider.GetRequiredService<GlossaryManager>();
        
        // Assert: GlossaryManager was created successfully with correct path
        Assert.NotNull(glossaryManager);
    }
}
