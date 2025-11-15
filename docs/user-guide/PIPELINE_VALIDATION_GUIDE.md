# Pipeline Validation Guide (Pull #188 / PR 32)

This guide provides instructions for validating the complete video generation pipeline from brief submission to downloadable video.

## Overview

The pipeline validation tests ensure that:
1. Complete pipeline executes from brief to video
2. Generated video file plays with audio and video
3. Progress updates appear during generation
4. Error handling works gracefully for all failure scenarios
5. Temporary files are cleaned up properly
6. Logs capture all pipeline events and errors

## Prerequisites

### Required
- .NET 8.0 SDK
- Node.js 20+ and npm
- FFmpeg installed and in PATH

### Provider Configuration (At least one of each)
- **LLM Provider**: Ollama (local) or OpenAI (cloud)
- **TTS Provider**: System TTS (local) or ElevenLabs (cloud)
- **Image Provider**: Stock images (built-in) or Stable Diffusion (local)

For offline testing, Ollama is recommended for LLM and system TTS for audio.

## Running the Validation

### Method 1: Automated Script (Recommended)

This script tests the complete pipeline through the running API:

```bash
# Start the backend API
cd Aura.Api
dotnet run &
API_PID=$!
cd ..

# Wait for API to be ready
sleep 5

# Start the frontend (optional)
cd Aura.Web
npm install
npm run dev &
FRONTEND_PID=$!
cd ..

# Wait for frontend to be ready
sleep 5

# Run validation script
./scripts/validate_pipeline.sh

# Cleanup
kill $API_PID $FRONTEND_PID
```

The script will:
- Test API health
- Submit a Quick Demo video generation request
- Monitor job progress through all stages
- Verify script generation completes
- Verify TTS generates valid audio
- Verify image generation produces images
- Verify FFmpeg assembles the final video
- Download and verify the video file
- Test job cancellation
- Check temporary file cleanup
- Verify logging captures events

### Method 2: Unit Tests

Run the E2E test suite:

```bash
cd Aura.E2E
dotnet test --configuration Release
```

Available test filters:
```bash
# Run only pipeline validation tests
dotnet test --filter "FullyQualifiedName~PipelineValidationTests"

# Run specific tests
dotnet test --filter "FullyQualifiedName~TemporaryFiles"
dotnet test --filter "FullyQualifiedName~Logging"
```

### Method 3: Manual UI Testing

1. **Start Backend**:
   ```bash
   cd Aura.Api
   dotnet run
   ```

2. **Start Frontend**:
   ```bash
   cd Aura.Web
   npm run dev
   ```

3. **Access UI**: Open browser to `http://localhost:5173`

4. **Submit Quick Demo**:
   - Navigate to Quick Demo section
   - Fill in brief details:
     - Topic: "Pipeline Validation Test"
     - Audience: "Developers"
     - Goal: "Test pipeline"
     - Tone: "Professional"
   - Click "Generate Video"

5. **Monitor Progress**:
   - Watch progress bar advance through stages
   - Verify stage names appear (Script, Audio, Visuals, Render)
   - Note progress percentages update

6. **Download Video**:
   - Wait for completion
   - Click download button
   - Verify video downloads
   - Play video in media player to verify audio and video

7. **Test Cancellation**:
   - Start a new video generation
   - Click "Cancel" button during generation
   - Verify job stops and shows cancelled status

## Test Coverage

### Task 1-2: Service Startup
- ✅ Backend starts successfully
- ✅ Frontend starts successfully
- ✅ API health check passes
- ✅ Frontend serves UI

### Task 3-4: Job Creation and Progress
- ✅ Quick Demo request submitted
- ✅ Job ID returned
- ✅ Progress tracking works
- ✅ Progress updates received

### Task 5: Script Generation
- ✅ LLM provider generates script
- ✅ Script contains valid content
- ✅ Script stage completes

### Task 6: TTS Generation
- ✅ TTS generates WAV files
- ✅ WAV files > 128 bytes
- ✅ Audio stage completes

### Task 7: Image Generation
- ✅ Images generated for scenes
- ✅ Valid image files created
- ✅ Visual stage completes

### Task 8: FFmpeg Assembly
- ✅ FFmpeg command executes
- ✅ Video file created
- ✅ Render stage completes

### Task 9: Video Download
- ✅ Video file downloadable
- ✅ Video plays with audio
- ✅ Video quality acceptable

### Task 10: Job Cancellation
- ✅ Cancel button works
- ✅ Job stops execution
- ✅ Status updates to cancelled

### Task 11: LLM Provider Error Handling
- ⚠️ Requires manual test (stop Ollama)
- ✅ Error message displayed
- ✅ User-friendly message

### Task 12: TTS Provider Error Handling
- ⚠️ Requires manual test (disable TTS)
- ✅ Error message displayed
- ✅ User-friendly message

### Task 13: Image Provider Error Handling
- ⚠️ Requires manual test (disable image gen)
- ✅ Error message displayed
- ✅ User-friendly message

### Task 14: Error Message UX
- ✅ Errors show in UI
- ✅ Messages are user-friendly
- ✅ No stack traces in UI

### Task 15: Temporary File Cleanup
- ✅ Temp files created during generation
- ✅ Temp files cleaned after completion
- ✅ No file leaks

### Task 16: Logging
- ✅ All stages logged
- ✅ Errors captured in logs
- ✅ Progress events logged
- ✅ Provider selection logged

## Error Handling Tests

To test error handling manually:

### Test LLM Provider Unavailable
1. Stop Ollama service (if using)
2. Or remove OpenAI API key from config
3. Submit video generation
4. Verify user-friendly error message appears
5. Check logs for detailed error

### Test TTS Provider Unavailable
1. Disable system TTS service
2. Or remove ElevenLabs API key
3. Submit video generation
4. Verify error message during audio stage
5. Check logs for error details

### Test Image Provider Unavailable
1. Configure to use Stable Diffusion
2. Stop Stable Diffusion service
3. Submit video generation
4. Verify error message during visual stage
5. Check logs for error details

## Success Criteria

✅ **Complete Pipeline**: Video generated end-to-end without errors
✅ **Video Quality**: Generated video plays with both audio and video
✅ **Progress Updates**: UI shows progress through all stages
✅ **Error Handling**: Graceful failures with user-friendly messages
✅ **Cleanup**: No temporary files left after completion
✅ **Logging**: All events and errors captured in logs

## Troubleshooting

### API Not Starting
- Check port 5000 is not in use
- Verify .NET 8.0 SDK installed
- Check appsettings.json configuration

### Frontend Not Starting
- Check port 5173 is not in use
- Run `npm install` in Aura.Web
- Verify Node.js 20+ installed

### Video Generation Fails
- Check FFmpeg is installed and in PATH
- Verify at least one LLM provider configured
- Verify at least one TTS provider configured
- Check logs in Aura.Api/logs or console output

### Job Hangs
- Check provider services are running (Ollama, etc.)
- Verify network connectivity for cloud providers
- Check system resources (CPU, memory, disk space)
- Review logs for stuck operations

### Cancellation Doesn't Work
- Verify cancel endpoint is accessible
- Check job is in Running state before cancelling
- Review JobRunner logs for cancellation handling

## Output Files

After running validation:
- `test-output/pipeline-validation/job_id.txt` - Job ID used
- `test-output/pipeline-validation/stages_seen.txt` - Stages detected
- `test-output/pipeline-validation/test_video.mp4` - Generated video
- `test-output/pipeline-validation/api_logs.txt` - API logs (if available)

## Additional Testing

### Performance Testing
- Time complete pipeline execution
- Monitor CPU/memory usage during generation
- Test with different video lengths (10s, 30s, 60s)

### Stress Testing
- Submit multiple jobs concurrently
- Verify queue handling
- Check resource cleanup under load

### Platform Testing
- Test on Windows, macOS, Linux
- Verify portable distribution works
- Test with different FFmpeg versions

## Support

For issues or questions:
1. Check logs in Aura.Api console output
2. Review browser console for frontend errors
3. Check GitHub Issues for known problems
4. Create new issue with logs and error details
