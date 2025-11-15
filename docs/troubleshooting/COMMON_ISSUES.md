# Common Issues and Solutions

This guide covers the most frequently encountered issues and their solutions when using Aura Video Studio.

## Table of Contents

- [Installation Issues](#installation-issues)
- [First Run Issues](#first-run-issues)
- [Video Generation Issues](#video-generation-issues)
- [Provider Issues](#provider-issues)
- [Performance Issues](#performance-issues)
- [UI Issues](#ui-issues)
- [Database Issues](#database-issues)
- [FFmpeg Issues](#ffmpeg-issues)
- [Network Issues](#network-issues)

## Installation Issues

### Issue: .NET 8 SDK Not Found

**Symptoms**: Error message "The required .NET SDK version is not available"

**Solution**:
1. Download and install .NET 8 SDK from https://dot.net/
2. Verify installation:
   ```bash
   dotnet --version
   # Should show 8.0.x
   ```
3. Restart your terminal
4. Try building again

### Issue: Node.js Version Mismatch

**Symptoms**: npm install fails with version errors

**Solution**:
1. Check required version:
   ```bash
   cat Aura.Web/.nvmrc
   # Shows required Node version (20.x)
   ```
2. Install correct version:
   ```bash
   # Using nvm
   nvm install 20.x
   nvm use 20.x
   
   # Or download from nodejs.org
   ```
3. Verify:
   ```bash
   node --version
   npm --version
   ```

### Issue: Permission Denied on Scripts

**Symptoms**: "Permission denied" when running setup scripts

**Solution**:
```bash
# Make scripts executable
chmod +x scripts/*.sh
chmod +x scripts/**/*.sh

# Or specifically
chmod +x scripts/setup-local.sh
```

## First Run Issues

### Issue: First Run Wizard Won't Start

**Symptoms**: Application opens but wizard doesn't appear

**Solution**:
1. Check if first run already completed:
   ```bash
   # Look for settings file
   ls ~/.local/share/AuraVideoStudio/  # Linux/macOS
   ls %LOCALAPPDATA%\AuraVideoStudio\   # Windows
   ```
2. Reset first run state:
   ```bash
   # Delete configuration
   rm -rf ~/.local/share/AuraVideoStudio/
   # Or on Windows
   rmdir /s %LOCALAPPDATA%\AuraVideoStudio\
   ```
3. Restart application

### Issue: API Keys Not Saving

**Symptoms**: API keys don't persist after restart

**Solution**:
1. Check file permissions:
   ```bash
   ls -la ~/.local/share/AuraVideoStudio/secrets.dat
   ```
2. Ensure directory is writable:
   ```bash
   chmod 700 ~/.local/share/AuraVideoStudio/
   chmod 600 ~/.local/share/AuraVideoStudio/secrets.dat
   ```
3. Check encryption status in Settings > Security

### Issue: FFmpeg Not Detected

**Symptoms**: "FFmpeg not found" error in first run wizard

**Solution**:

**Windows**:
```powershell
# Run FFmpeg installer
.\scripts\ffmpeg\install-ffmpeg-windows.ps1

# Or download manually
# Extract to C:\ffmpeg and add to PATH
```

**Linux**:
```bash
# Ubuntu/Debian
sudo apt update
sudo apt install ffmpeg

# Or use installer script
./scripts/ffmpeg/install-ffmpeg-linux.sh
```

**Verify**:
```bash
ffmpeg -version
```

## Video Generation Issues

### Issue: "No LLM Provider Configured"

**Symptoms**: Script generation fails immediately

**Solution**:
1. Configure at least one LLM provider:
   - Go to Settings > Providers
   - Add OpenAI API key, or
   - Enable local provider (if available)
2. Test provider:
   ```bash
   curl -X POST http://localhost:5005/api/v1/providers/test \
     -H "Content-Type: application/json" \
     -d '{"providerId": "openai"}'
   ```

### Issue: Video Generation Stuck at 0%

**Symptoms**: Progress bar doesn't move, no errors

**Diagnostic**:
```bash
# Check logs
tail -f logs/aura-api-*.log

# Check job status
curl http://localhost:5005/api/v1/jobs/{jobId}

# Check FFmpeg process
ps aux | grep ffmpeg
```

**Solutions**:
1. **If logs show errors**: Address specific error (see error messages below)
2. **If no logs**: Service might be frozen
   ```bash
   # Restart API
   systemctl restart aura-api
   # Or
   docker-compose restart api
   ```
3. **If FFmpeg hung**: Kill FFmpeg process
   ```bash
   pkill -9 ffmpeg
   ```

### Issue: "Out of Memory" During Rendering

**Symptoms**: Video generation fails with OOM error

**Solution**:
1. Reduce video resolution:
   - Settings > Video > Resolution (try 720p instead of 1080p)
2. Reduce video length:
   - Shorter videos use less memory
3. Close other applications
4. Increase system RAM if possible
5. Enable proxy media:
   - Settings > Performance > Enable Proxy Media

### Issue: Audio/Video Out of Sync

**Symptoms**: Generated video has desynchronized audio

**Solution**:
1. Check audio duration matches video:
   ```bash
   # Get durations
   ffprobe -v error -show_entries format=duration \
     -of default=noprint_wrappers=1:nokey=1 output/audio.mp3
   
   ffprobe -v error -show_entries format=duration \
     -of default=noprint_wrappers=1:nokey=1 output/video.mp4
   ```
2. Regenerate with adjusted timing:
   - Settings > Advanced > Manual Timing Adjustment
3. Use different TTS provider (some are more accurate)

## Provider Issues

### Issue: OpenAI API Rate Limit Exceeded

**Symptoms**: "429 Too Many Requests" error

**Solution**:
1. Check your OpenAI usage: https://platform.openai.com/usage
2. Wait and retry (automatic retry with backoff is built-in)
3. Upgrade OpenAI plan for higher limits
4. Enable rate limiting in Aura:
   - Settings > Providers > OpenAI > Rate Limit (requests/minute)

### Issue: ElevenLabs "Insufficient Quota"

**Symptoms**: TTS fails with quota error

**Solution**:
1. Check ElevenLabs quota: https://elevenlabs.io/
2. Upgrade plan or wait for quota reset
3. Use alternative TTS provider:
   - Settings > Providers > TTS > Select different provider
4. Enable local TTS (free but lower quality):
   - Settings > Providers > Local TTS > Enable

### Issue: Provider Connection Timeout

**Symptoms**: "Connection timeout" or "Network error"

**Diagnostic**:
```bash
# Test internet connectivity
ping api.openai.com
ping api.elevenlabs.io

# Test DNS resolution
nslookup api.openai.com

# Test API directly
curl https://api.openai.com/v1/models \
  -H "Authorization: Bearer YOUR_API_KEY"
```

**Solutions**:
1. Check internet connection
2. Check firewall/proxy settings
3. Verify API keys are correct
4. Try different network (e.g., disable VPN)

## Performance Issues

### Issue: Slow Video Generation

**Symptoms**: Video takes very long to generate

**Diagnostic**:
```bash
# Check CPU usage
top

# Check disk I/O
iotop

# Check memory
free -h

# Check GPU usage (if available)
nvidia-smi
```

**Solutions**:
1. **CPU bound**:
   - Close background applications
   - Reduce video quality/length
   - Enable hardware acceleration (if available)
2. **Disk bound**:
   - Use SSD instead of HDD
   - Free up disk space
   - Reduce temporary file usage
3. **Network bound**:
   - Use faster internet connection
   - Enable caching for repeated API calls
   - Use local providers when possible

### Issue: High Memory Usage

**Symptoms**: System runs out of memory, swapping occurs

**Solution**:
1. Check memory usage:
   ```bash
   # Process memory
   ps aux --sort=-%mem | head
   
   # Detailed memory info
   cat /proc/meminfo
   ```
2. Reduce memory usage:
   - Settings > Performance > Memory Limit
   - Enable proxy media
   - Reduce concurrent operations
3. Increase swap space:
   ```bash
   # Linux
   sudo fallocate -l 8G /swapfile
   sudo chmod 600 /swapfile
   sudo mkswap /swapfile
   sudo swapon /swapfile
   ```

### Issue: UI Freezing/Lagging

**Symptoms**: Interface becomes unresponsive

**Solutions**:
1. Disable resource-intensive features:
   - Settings > UI > Disable Animations
   - Settings > UI > Reduce Preview Quality
2. Clear cache:
   - Settings > Advanced > Clear Cache
3. Restart application
4. Check for memory leaks:
   ```bash
   # Monitor memory over time
   watch -n 1 'ps aux | grep -E "(Aura|dotnet|node)"'
   ```

## UI Issues

### Issue: Blank White Screen

**Symptoms**: Application window is completely white

**Diagnostic**:
```bash
# Check console logs (F12 in browser)
# Look for JavaScript errors

# Check API connectivity
curl http://localhost:5005/api/v1/health

# Check browser console for CORS errors
```

**Solutions**:
1. Hard refresh: Ctrl+Shift+R (or Cmd+Shift+R on Mac)
2. Clear browser cache
3. Check API is running:
   ```bash
   ps aux | grep dotnet
   # Or
   docker ps | grep aura-api
   ```
4. Check CORS configuration in API settings

### Issue: "Cannot connect to API" Error

**Symptoms**: Red banner showing connection error

**Solution**:
1. Verify API is running:
   ```bash
   curl http://localhost:5005/api/v1/health
   ```
2. Check API port (default 5005):
   ```bash
   netstat -an | grep 5005
   ```
3. Check firewall isn't blocking:
   ```bash
   # Linux
   sudo ufw status
   
   # Windows
   Get-NetFirewallRule | Where DisplayName -like "*Aura*"
   ```
4. Restart API service

### Issue: Dark Mode Not Working

**Symptoms**: Theme doesn't change or looks broken

**Solution**:
1. Check theme setting:
   - Settings > Appearance > Theme
2. Clear browser storage:
   ```javascript
   // In browser console
   localStorage.clear();
   sessionStorage.clear();
   ```
3. Hard refresh: Ctrl+Shift+R

## Database Issues

### Issue: "Database is locked"

**Symptoms**: Operations fail with "database is locked" error

**Solution**:
1. Check for concurrent access:
   ```bash
   lsof aura.db  # Linux/macOS
   ```
2. Stop all Aura processes:
   ```bash
   pkill -f Aura
   pkill -f dotnet
   ```
3. Remove lock file:
   ```bash
   rm aura.db-shm aura.db-wal
   ```
4. Restart application

### Issue: Database Corruption

**Symptoms**: "Database disk image is malformed"

**Diagnostic**:
```bash
# Check database integrity
sqlite3 aura.db "PRAGMA integrity_check;"
```

**Solution**:
1. If integrity check passes, rebuild:
   ```bash
   sqlite3 aura.db "VACUUM;"
   ```
2. If corrupted, restore from backup:
   ```bash
   cp backup/aura-YYYYMMDD.db aura.db
   ```
3. If no backup, try recovery:
   ```bash
   sqlite3 aura.db ".recover" | sqlite3 aura-recovered.db
   mv aura-recovered.db aura.db
   ```

### Issue: Slow Database Queries

**Symptoms**: Operations take long time

**Solution**:
1. Vacuum database:
   ```bash
   sqlite3 aura.db "VACUUM;"
   ```
2. Analyze and optimize:
   ```bash
   sqlite3 aura.db "ANALYZE;"
   ```
3. Check database size:
   ```bash
   ls -lh aura.db
   ```
4. If too large, archive old projects:
   - Settings > Database > Archive Old Projects

## FFmpeg Issues

### Issue: FFmpeg Command Failed

**Symptoms**: Video rendering fails with FFmpeg error

**Diagnostic**:
```bash
# Check FFmpeg is available
which ffmpeg
ffmpeg -version

# Test FFmpeg directly
ffmpeg -i input.mp4 -c copy output.mp4

# Check logs for exact FFmpeg command
grep "ffmpeg" logs/aura-api-*.log
```

**Solutions**:
1. Reinstall FFmpeg:
   ```bash
   # Ubuntu/Debian
   sudo apt remove ffmpeg
   sudo apt install ffmpeg
   
   # Windows
   .\scripts\ffmpeg\install-ffmpeg-windows.ps1
   ```
2. Update FFmpeg to latest version
3. Check file permissions on input files
4. Verify disk space for output

### Issue: Codec Not Supported

**Symptoms**: "Unknown encoder" or "Codec not found"

**Solution**:
1. Check installed codecs:
   ```bash
   ffmpeg -codecs | grep -i h264
   ```
2. Install full FFmpeg build with all codecs:
   ```bash
   # Ubuntu/Debian
   sudo apt install ffmpeg ffmpeg-extra
   
   # Or build from source with all features
   ```
3. Use different codec:
   - Settings > Video > Codec (try H.264 instead of H.265)

## Network Issues

### Issue: Proxy Authentication Failed

**Symptoms**: Cannot connect through corporate proxy

**Solution**:
1. Configure proxy settings:
   ```bash
   # Set environment variables
   export HTTP_PROXY=http://proxy.company.com:8080
   export HTTPS_PROXY=http://proxy.company.com:8080
   export NO_PROXY=localhost,127.0.0.1
   ```
2. Or configure in application:
   - Settings > Network > Proxy Settings
3. If authentication required:
   ```bash
   export HTTP_PROXY=http://username:password@proxy:8080
   ```

### Issue: SSL Certificate Verification Failed

**Symptoms**: "SSL certificate problem" errors

**Solution**:
1. Update CA certificates:
   ```bash
   # Ubuntu/Debian
   sudo apt install ca-certificates
   sudo update-ca-certificates
   ```
2. If using corporate proxy with SSL inspection:
   - Import corporate root CA certificate
   - Or disable SSL verification (not recommended):
     ```bash
     export NODE_TLS_REJECT_UNAUTHORIZED=0
     ```

## Getting Additional Help

If your issue isn't covered here:

1. **Check logs**: `logs/aura-api-*.log` and browser console
2. **Search existing issues**: https://github.com/yourusername/aura-video-studio/issues
3. **Ask in discussions**: https://github.com/yourusername/aura-video-studio/discussions
4. **Report a bug**: Use bug report template

### Information to Include

When asking for help, include:

- Operating system and version
- Aura Video Studio version
- Exact error message
- Steps to reproduce
- Relevant log excerpts
- Screenshots if applicable

### Log Collection

```bash
# Collect logs for support
tar -czf aura-logs-$(date +%Y%m%d).tar.gz \
  logs/ \
  aura.db \
  appsettings.json

# Create diagnostics bundle
curl http://localhost:5005/api/v1/diagnostics/bundle \
  -o diagnostics-$(date +%Y%m%d).zip
```

## Related Documentation

- [Main Troubleshooting Guide](../../TROUBLESHOOTING.md)
- [Operational Runbooks](../operations/runbooks/README.md)
- [API Error Codes](../api/errors.md)
- [Development Guide](../../DEVELOPMENT.md)

---

**Last Updated**: 2024-11-10  
**Maintained by**: Support Team
