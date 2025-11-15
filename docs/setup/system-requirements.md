# System Requirements

This document outlines the hardware and software requirements for running Aura Video Studio.

## Quick Navigation

- [Minimum Requirements](#minimum-requirements)
- [Recommended Requirements](#recommended-requirements)
- [OS Support](#operating-system-support)
- [GPU Requirements](#gpu-requirements)
- [Storage Requirements](#storage-requirements)

---

## Minimum Requirements

### Basic Video Generation (720p)

| Component | Requirement |
|-----------|-------------|
| **CPU** | Dual-core 2.0 GHz or faster |
| **RAM** | 4 GB |
| **Storage** | 5 GB free space |
| **GPU** | Not required (software rendering) |
| **OS** | Windows 10, macOS 11, or Linux (Ubuntu 22.04+) |
| **Network** | Broadband internet (for AI providers) |

**What you can do with minimum specs**:
- Generate short videos (up to 5 minutes)
- 720p output resolution
- Software-based rendering (slower)
- Basic effects and transitions

**Limitations**:
- Rendering will be slow (< 0.5x real-time)
- May struggle with multiple concurrent operations
- Limited to simpler projects

---

## Recommended Requirements

### Professional Video Generation (1080p/4K)

| Component | Requirement |
|-----------|-------------|
| **CPU** | Quad-core 3.0 GHz or faster (e.g., Intel i5/i7, AMD Ryzen 5/7) |
| **RAM** | 16 GB or more |
| **Storage** | 50 GB free space (SSD recommended) |
| **GPU** | NVIDIA GTX 1060 / AMD RX 580 / Intel Iris Xe or better |
| **VRAM** | 4 GB (for local image generation) |
| **OS** | Windows 11, macOS 12+, or Linux (Ubuntu 22.04+ LTS) |
| **Network** | High-speed broadband (10+ Mbps) |

**What you can do with recommended specs**:
- Generate long videos (up to 30+ minutes)
- 1080p or 4K output resolution
- Hardware-accelerated rendering (faster)
- Complex effects and transitions
- Simultaneous operations (generation + preview)

**Performance**:
- Rendering: 1-2x real-time (1 minute video = 0.5-1 minute render)
- Smooth UI and preview
- Quick export times

---

## High-Performance Setup

### Content Creator / Enterprise

| Component | Requirement |
|-----------|-------------|
| **CPU** | 8+ cores, 3.5 GHz or faster (Intel i9, AMD Ryzen 9, Threadripper) |
| **RAM** | 32 GB or more (64 GB for 4K+) |
| **Storage** | 500 GB+ NVMe SSD |
| **GPU** | NVIDIA RTX 3060+ / AMD RX 6700 XT+ (8+ GB VRAM) |
| **VRAM** | 8+ GB (for local ML models) |
| **OS** | Latest Windows 11, macOS, or Linux |
| **Network** | Gigabit (1000 Mbps) or faster |

**Benefits**:
- 4K/8K rendering
- Real-time preview
- Local AI model support
- Batch processing
- Multiple simultaneous renders

**Performance**:
- Rendering: 2-5x real-time (1 minute video = 12-30 seconds render)
- Instant UI response
- Fast asset generation

---

## Operating System Support

### Windows

| Version | Support Status | Notes |
|---------|----------------|-------|
| Windows 11 | ✅ Fully Supported | Recommended |
| Windows 10 (21H2+) | ✅ Fully Supported | |
| Windows 10 (older) | ⚠️ May Work | Update recommended |
| Windows 8.1 | ❌ Not Supported | |
| Windows 7 | ❌ Not Supported | |

**Requirements**:
- 64-bit processor and OS
- DirectX 12 compatible graphics
- .NET 8.0 Runtime
- Windows Defender up to date

**Recommended**:
- Windows 11 22H2 or later
- All updates installed
- NVIDIA/AMD/Intel graphics drivers up to date

### macOS

| Version | Support Status | Notes |
|---------|----------------|-------|
| macOS 14 Sonoma | ✅ Fully Supported | Recommended |
| macOS 13 Ventura | ✅ Fully Supported | |
| macOS 12 Monterey | ✅ Fully Supported | |
| macOS 11 Big Sur | ⚠️ Limited Support | Older hardware |
| macOS 10.15 Catalina | ❌ Not Supported | |

**Requirements**:
- Intel or Apple Silicon (M1/M2/M3)
- 64-bit processor
- Metal graphics support
- .NET 8.0 Runtime

**Apple Silicon Notes**:
- Native ARM64 support
- Excellent performance
- Some x86 dependencies via Rosetta 2
- Hardware encoding via VideoToolbox

### Linux

| Distribution | Support Status | Notes |
|--------------|----------------|-------|
| Ubuntu 22.04+ LTS | ✅ Fully Supported | Recommended |
| Ubuntu 20.04 LTS | ✅ Fully Supported | |
| Debian 11+ | ✅ Fully Supported | |
| Fedora 38+ | ✅ Fully Supported | |
| Arch Linux | ⚠️ Community Support | Rolling release |
| CentOS/RHEL 8+ | ⚠️ Community Support | |
| Other | ⚠️ May Work | Community support |

**Requirements**:
- x86_64 architecture
- Kernel 5.4 or newer
- glibc 2.31 or newer
- .NET 8.0 Runtime
- X11 or Wayland display server

**Recommended**:
- Ubuntu 22.04 LTS or 24.04 LTS
- NVIDIA proprietary drivers (for GPU acceleration)
- PulseAudio or PipeWire for audio

---

## GPU Requirements

### For Hardware-Accelerated Rendering

**NVIDIA (Recommended)**:
- GTX 1050 or newer (Kepler+ architecture)
- 2+ GB VRAM minimum, 4+ GB recommended
- CUDA 11.0+ support
- Latest NVIDIA drivers (525+)

**Supported Encoders**:
- H.264: `h264_nvenc`
- H.265/HEVC: `hevc_nvenc`

**Benefits**:
- 3-5x faster rendering
- Lower CPU usage
- Better quality at same bitrate

**AMD**:
- RX 460 or newer (Polaris+ architecture)
- 2+ GB VRAM minimum
- Latest AMD drivers

**Supported Encoders**:
- H.264: `h264_amf`
- H.265/HEVC: `hevc_amf`

**Intel**:
- Intel HD Graphics 530 or newer (6th gen+)
- Integrated graphics supported
- Latest Intel graphics drivers

**Supported Encoders**:
- H.264: `h264_qsv`
- H.265/HEVC: `hevc_qsv`

### For Local Image Generation

**Higher VRAM requirements**:

| Model | Minimum VRAM | Recommended VRAM |
|-------|-------------|------------------|
| Stable Diffusion 1.5 | 4 GB | 6 GB |
| Stable Diffusion XL | 6 GB | 8 GB |
| SDXL Turbo | 8 GB | 10 GB |

**GPU Recommendations**:
- NVIDIA RTX 3060 (12 GB) or better
- NVIDIA RTX 4060 Ti (16 GB) for SDXL
- AMD RX 6700 XT (12 GB) or better

### Without GPU

Aura works fine without a GPU:
- Software rendering (CPU-only)
- Slower render times (0.3-0.5x real-time)
- Cloud-based AI (no local generation)
- All features still available

---

## Storage Requirements

### Disk Space

**Application**: ~500 MB
**Dependencies** (FFmpeg, .NET, etc.): ~200-500 MB
**Cache**: 1-5 GB (configurable)
**Per Project**: 100 MB - 10 GB

**Total Recommended**: 50-100 GB free

### Storage Type

**SSD (Solid State Drive)** - Strongly Recommended:
- Faster loading times
- Quicker rendering
- Better preview performance
- Instant saves

**HDD (Hard Disk Drive)** - Works but slower:
- Longer render times
- Slower preview generation
- Noticeable lag on large projects

### Disk Speed Requirements

| Use Case | Recommended Speed |
|----------|------------------|
| Basic (720p) | HDD (7200 RPM) |
| Standard (1080p) | SATA SSD (500 MB/s) |
| Professional (4K) | NVMe SSD (2000+ MB/s) |

### Space Planning

**Example: 10-minute 1080p video**:
- Source assets (images, audio): 200-500 MB
- Temporary files during render: 2-5 GB
- Final output: 500 MB - 2 GB
- Cache: 100-500 MB
- **Total**: 3-8 GB per project

**Recommended space by usage**:
- **Casual** (few projects): 20-50 GB
- **Regular** (multiple projects): 100-200 GB
- **Professional** (many projects): 500 GB - 2 TB

---

## Memory (RAM) Requirements

### By Resolution

| Resolution | Minimum RAM | Recommended RAM |
|------------|-------------|-----------------|
| 480p | 2 GB | 4 GB |
| 720p | 4 GB | 8 GB |
| 1080p | 8 GB | 16 GB |
| 4K | 16 GB | 32 GB |
| 8K | 32 GB | 64 GB |

### By Operation

| Operation | RAM Usage |
|-----------|-----------|
| Application baseline | 200-500 MB |
| Script generation (LLM) | 500 MB - 1 GB |
| Image generation (cloud) | 200-500 MB |
| Image generation (local) | 2-8 GB (VRAM) |
| Audio generation | 200-500 MB |
| Video preview | 500 MB - 2 GB |
| Video rendering | 2-8 GB |

### Swap/Virtual Memory

**Not a substitute for RAM**, but helps:
- Windows: 8-16 GB page file recommended
- Linux: 8-16 GB swap partition
- Mac: Managed automatically

---

## Network Requirements

### Internet Connection

**Required for**:
- AI provider APIs (OpenAI, Anthropic, etc.)
- Downloading models and updates
- Fetching online assets

**Minimum**: 5 Mbps down, 1 Mbps up  
**Recommended**: 25+ Mbps down, 5+ Mbps up

### Bandwidth Usage

**Typical AI API calls**:
- Script generation: 10-50 KB per request
- Image generation: 500 KB - 2 MB per image
- TTS generation: 100-500 KB per audio file

**Per video project**:
- Script: 20-100 KB
- 5 images: 2.5-10 MB
- Audio: 500 KB - 2 MB
- **Total**: 3-12 MB per video (excluding output)

### Offline Capabilities

**Works offline**:
- Project editing
- Timeline editing
- Local rendering (FFmpeg)
- Previewing existing content

**Requires internet**:
- Script generation (LLM)
- Cloud image generation
- Cloud TTS
- App updates

---

## CPU Requirements

### Minimum

**Dual-core 2.0 GHz**:
- Intel Core i3 (6th gen+)
- AMD Ryzen 3
- Apple M1

**Use case**: Basic video generation, light editing

### Recommended

**Quad-core 3.0 GHz**:
- Intel Core i5 (8th gen+)
- AMD Ryzen 5
- Apple M1 Pro

**Use case**: Standard video production, 1080p rendering

### Professional

**8+ cores, 3.5 GHz**:
- Intel Core i9 (10th gen+)
- AMD Ryzen 9
- AMD Threadripper
- Apple M1 Max/M2 Ultra

**Use case**: 4K production, batch processing, professional work

### CPU Features

**Beneficial features**:
- **AVX2**: Faster video encoding (Intel 4th gen+, AMD Excavator+)
- **Multiple cores**: Parallel processing
- **High clock speed**: Single-threaded operations
- **Large cache**: Better performance

---

## Browser Requirements

### For Web UI

**Supported browsers**:
- ✅ Chrome 90+
- ✅ Firefox 88+
- ✅ Edge 90+
- ✅ Safari 14+
- ⚠️ Opera 76+
- ⚠️ Brave (Chromium-based)

**Recommended**: Chrome or Edge (Chromium)

**Required browser features**:
- JavaScript enabled
- Cookies enabled
- WebSocket support
- HTML5 video
- CSS Grid support

---

## Development Requirements

### For Building from Source

**Additional requirements**:
- .NET 8.0 SDK (not just runtime)
- Node.js 20+ with npm
- Git 2.30+
- Text editor/IDE (VS Code, Visual Studio, Rider)

**Recommended specs**:
- 16+ GB RAM
- SSD storage
- Quad-core+ CPU

See [Build Guide](../../BUILD_GUIDE.md) for details.

---

## Compatibility Matrix

### Tested Configurations

| OS | CPU | RAM | GPU | Status |
|----|-----|-----|-----|--------|
| Windows 11 | Intel i7-12700 | 16 GB | RTX 3070 | ✅ Excellent |
| Windows 10 | AMD Ryzen 5 5600 | 8 GB | GTX 1660 | ✅ Good |
| macOS 14 | Apple M2 | 16 GB | Integrated | ✅ Excellent |
| macOS 12 | Intel i5-8500 | 8 GB | Intel UHD | ⚠️ Acceptable |
| Ubuntu 22.04 | AMD Ryzen 7 5800X | 32 GB | RTX 3080 | ✅ Excellent |
| Ubuntu 20.04 | Intel i5-9400 | 16 GB | None | ⚠️ Acceptable |

### Unsupported Configurations

❌ **32-bit operating systems**  
❌ **ARM Windows** (Windows on ARM)  
❌ **ChromeOS / ChromeBooks**  
❌ **Android / iOS** (no mobile support)  
❌ **Windows Server** (untested)  
❌ **Wine / Proton** (untested)

---

## Performance Expectations

### Render Times (1080p 30fps, 1-minute video)

| Configuration | Software Rendering | Hardware Rendering |
|---------------|-------------------|-------------------|
| Minimum | 3-5 minutes | N/A |
| Recommended | 1-2 minutes | 30-60 seconds |
| High-Performance | 30-60 seconds | 12-30 seconds |

### Responsiveness

| Configuration | UI Latency | Preview Latency |
|---------------|-----------|----------------|
| Minimum | 100-500ms | 1-2 seconds |
| Recommended | 50-100ms | 200-500ms |
| High-Performance | <50ms | <200ms |

---

## Recommendations by Use Case

### Hobbyist / Beginner

- **CPU**: Quad-core 3.0 GHz
- **RAM**: 8 GB
- **Storage**: 50 GB SSD
- **GPU**: Optional (GTX 1650 if available)
- **Network**: 10 Mbps

### Content Creator

- **CPU**: 6-8 cores, 3.5 GHz
- **RAM**: 16 GB
- **Storage**: 250 GB NVMe SSD
- **GPU**: RTX 3060 or equivalent
- **Network**: 25+ Mbps

### Professional / Studio

- **CPU**: 8+ cores, 4.0 GHz
- **RAM**: 32-64 GB
- **Storage**: 1 TB NVMe SSD
- **GPU**: RTX 4070+ or equivalent
- **Network**: Gigabit

---

## Related Documentation

- [Installation Guide](../getting-started/INSTALLATION.md)
- [Dependencies Setup](dependencies.md)
- [Performance Optimization](../PERFORMANCE_BENCHMARKS.md)
- [Hardware Acceleration](../../ADVANCED_RENDERING_GUIDE.md)

## Need Help?

If your system doesn't meet requirements:
1. Try with minimum specs - may still work
2. Use cloud-based providers (reduce local processing)
3. Render at lower resolution
4. Disable hardware acceleration
5. Consider system upgrades
6. Check [GitHub Discussions](https://github.com/Coffee285/aura-video-studio/discussions) for optimization tips
