using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Aura.Core.Runtime;

/// <summary>
/// Result of a managed process execution
/// </summary>
public record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError
);

/// <summary>
/// Managed process runner that ensures proper tracking, timeout, and cleanup
/// </summary>
public class ManagedProcessRunner
{
    private readonly ProcessRegistry _registry;
    private readonly ILogger<ManagedProcessRunner> _logger;

    public ManagedProcessRunner(
        ProcessRegistry registry,
        ILogger<ManagedProcessRunner> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Run a process with proper tracking, timeout, and cancellation support
    /// </summary>
    public async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        string? jobId = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null,
        Func<StreamWriter, Task>? writeToStdin = null)
    {
        if (startInfo == null)
        {
            throw new ArgumentNullException(nameof(startInfo));
        }

        // Default timeout is 30 minutes
        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(30);

        using var process = new Process { StartInfo = startInfo };

        // Ensure output redirection is set up
        if (onStdOut != null || onStdErr != null)
        {
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
        }

        // Set up stdin redirection if writeToStdin is provided
        if (writeToStdin != null)
        {
            process.StartInfo.RedirectStandardInput = true;
        }

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        // Set up output handlers
        if (process.StartInfo.RedirectStandardOutput)
        {
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    stdoutBuilder.AppendLine(e.Data);
                    onStdOut?.Invoke(e.Data);
                }
            };
        }

        if (process.StartInfo.RedirectStandardError)
        {
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    stderrBuilder.AppendLine(e.Data);
                    onStdErr?.Invoke(e.Data);
                }
            };
        }

        // Start the process
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
        }

        // Register with registry for tracking
        var tracked = _registry.Register(process, jobId);

        // Create linked cancellation token source
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, tracked.Cts.Token);

        try
        {
            // Write to stdin if provided (must be done before BeginOutputReadLine)
            if (writeToStdin != null && process.StartInfo.RedirectStandardInput)
            {
                try
                {
                    await writeToStdin(process.StandardInput).ConfigureAwait(false);
                    process.StandardInput.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error writing to stdin for process {Pid}", process.Id);
                    // Continue execution even if stdin write fails
                }
            }

            // Begin reading output streams
            if (process.StartInfo.RedirectStandardOutput)
            {
                process.BeginOutputReadLine();
            }

            if (process.StartInfo.RedirectStandardError)
            {
                process.BeginErrorReadLine();
            }

            // Wait for process completion or timeout
            var timeoutTask = Task.Delay(effectiveTimeout, linkedCts.Token);
            var processTask = process.WaitForExitAsync(linkedCts.Token);

            var completed = await Task.WhenAny(timeoutTask, processTask).ConfigureAwait(false);

            if (completed == timeoutTask)
            {
                _logger.LogWarning(
                    "Process {Name} (PID: {Pid}) timed out after {Timeout}",
                    process.ProcessName, process.Id, effectiveTimeout);

                // Kill the process tree
                await KillProcessTreeAsync(process).ConfigureAwait(false);

                throw new TimeoutException(
                    $"Process {process.ProcessName} exceeded timeout of {effectiveTimeout}");
            }

            // Wait for output streams to finish reading
            if (process.StartInfo.RedirectStandardOutput || process.StartInfo.RedirectStandardError)
            {
                // Give a short time for async output reading to complete
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
            }

            return new ProcessResult(
                process.ExitCode,
                stdoutBuilder.ToString(),
                stderrBuilder.ToString());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Process {Name} (PID: {Pid}) cancelled", process.ProcessName, process.Id);

            // Kill the process tree on cancellation
            await KillProcessTreeAsync(process).ConfigureAwait(false);

            throw;
        }
        finally
        {
            // Unregister from registry (will happen automatically on exit, but ensure it)
            if (!process.HasExited)
            {
                _registry.Unregister(process.Id);
            }
        }
    }

    /// <summary>
    /// Kill a process tree using platform-specific methods
    /// </summary>
    private async Task KillProcessTreeAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            // Validate process ID (defensive programming against potential invalid states)
            if (process.Id <= 0)
            {
                _logger.LogWarning("Invalid process ID {Pid}, cannot kill process tree", process.Id);
                return;
            }

            // On Windows, use taskkill for more reliable process tree termination
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    _logger.LogDebug("Attempting Windows taskkill for PID {Pid}", process.Id);
                    
                    var taskkillProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            // Process.Id is a validated integer, safe to interpolate
                            Arguments = $"/T /F /PID {process.Id}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    taskkillProcess.Start();
                    await taskkillProcess.WaitForExitAsync().ConfigureAwait(false);

                    if (taskkillProcess.ExitCode == 0)
                    {
                        _logger.LogDebug("Successfully killed process tree via taskkill for PID {Pid}", process.Id);
                        return;
                    }

                    _logger.LogWarning("taskkill returned exit code {ExitCode} for PID {Pid}", 
                        taskkillProcess.ExitCode, process.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to use taskkill for PID {Pid}, falling back to Process.Kill", process.Id);
                }
            }

            // Fallback to standard .NET method
            if (!process.HasExited)
            {
                _logger.LogDebug("Using Process.Kill(entireProcessTree: true) for PID {Pid}", process.Id);
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error killing process {Pid}", process.Id);
        }
    }
}

