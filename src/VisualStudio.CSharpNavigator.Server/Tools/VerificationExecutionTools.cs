using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class VerificationExecutionTools
{
    [McpServerTool(Name = "execute_csharp_verification", ReadOnly = false, Idempotent = false)]
    [Description("Run a bounded structured dotnet build or test for one solution/project path, capture bounded output, and return ranked failure evidence. Result shape: items[] (typed results), diagnostics[] (string notes), isPartial (bool, true when truncated), succeeded (bool, false only on rejection), errorCode (optional string, from the first diagnostic code prefix).")]
    public async Task<WorkspaceQueryResult<CSharpVerificationExecutionResult>> ExecuteCSharpVerification(
        string targetPath,
        CSharpVerificationCommandKind kind = CSharpVerificationCommandKind.Build,
        string configuration = "Release",
        string? testFilter = null,
        bool noRestore = true,
        int timeoutMilliseconds = 300000,
        int maxOutputCharacters = 100000,
        string[]? includePathPatterns = null,
        string[]? excludePathPatterns = null,
        string[]? changedFiles = null,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(
            targetPath,
            kind,
            configuration,
            testFilter,
            timeoutMilliseconds,
            maxOutputCharacters);
        if (validation is not null)
        {
            return new WorkspaceQueryResult<CSharpVerificationExecutionResult>
            {
                IsPartial = true,
                Diagnostics = new[] { validation },
            };
        }

        var fullTargetPath = Path.GetFullPath(targetPath);
        var arguments = new List<string>
        {
            kind == CSharpVerificationCommandKind.Test ? "test" : "build",
            fullTargetPath,
            "--configuration",
            configuration,
            "--verbosity",
            "minimal",
            "--nologo",
        };
        if (noRestore)
        {
            arguments.Add("--no-restore");
        }

        if (kind == CSharpVerificationCommandKind.Test && !string.IsNullOrWhiteSpace(testFilter))
        {
            arguments.Add("--filter");
            arguments.Add(testFilter.Trim());
        }

        var commandSummary = "dotnet " + string.Join(" ", arguments.Select(Quote));
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMilliseconds);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = Path.GetDirectoryName(fullTargetPath) ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";

        try
        {
            if (!process.Start())
            {
                return Failure("VerificationProcessStartFailed: dotnet process could not be started.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                TryKill(process);
                await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
                stopwatch.Stop();
                return CreateResult(
                    kind,
                    fullTargetPath,
                    commandSummary,
                    output: standardOutput.Result + Environment.NewLine + standardError.Result,
                    exitCode: -1,
                    timedOut: true,
                    stopwatch.ElapsedMilliseconds,
                    maxOutputCharacters,
                    includePathPatterns,
                    excludePathPatterns,
                    changedFiles);
            }

            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
            stopwatch.Stop();
            return CreateResult(
                kind,
                fullTargetPath,
                commandSummary,
                standardOutput.Result + Environment.NewLine + standardError.Result,
                process.ExitCode,
                timedOut: false,
                stopwatch.ElapsedMilliseconds,
                maxOutputCharacters,
                includePathPatterns,
                excludePathPatterns,
                changedFiles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(process);
            return Failure("VerificationProcessFailed: " + ex.Message);
        }
    }

    private static string? Validate(
        string targetPath,
        CSharpVerificationCommandKind kind,
        string configuration,
        string? testFilter,
        int timeoutMilliseconds,
        int maxOutputCharacters)
    {
        if (!Enum.IsDefined(kind))
        {
            return "VerificationCommandKind must be Build or Test.";
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return "TargetPath is required.";
        }

        var extension = Path.GetExtension(targetPath);
        if (!File.Exists(targetPath)
            || !string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return "TargetPath must point to an existing .sln, .slnx, or .csproj file.";
        }

        if (string.IsNullOrWhiteSpace(configuration)
            || configuration.Any(char.IsWhiteSpace)
            || configuration.Any(char.IsControl))
        {
            return "Configuration must be a non-empty single token.";
        }

        if (kind == CSharpVerificationCommandKind.Build && !string.IsNullOrWhiteSpace(testFilter))
        {
            return "TestFilter is only valid for Test verification.";
        }

        if (timeoutMilliseconds is < 1000 or > 1800000)
        {
            return "TimeoutMilliseconds must be between 1000 and 1800000.";
        }

        if (maxOutputCharacters is < 1000 or > 1000000)
        {
            return "MaxOutputCharacters must be between 1000 and 1000000.";
        }

        return null;
    }

    private static WorkspaceQueryResult<CSharpVerificationExecutionResult> CreateResult(
        CSharpVerificationCommandKind kind,
        string targetPath,
        string commandSummary,
        string output,
        int exitCode,
        bool timedOut,
        long elapsedMilliseconds,
        int maxOutputCharacters,
        string[]? includePathPatterns,
        string[]? excludePathPatterns,
        string[]? changedFiles)
    {
        var isOutputTruncated = output.Length > maxOutputCharacters;
        // MSBuild prints the error summary at the end; keep the tail so failing
        // builds do not lose every error line to truncation.
        var boundedOutput = isOutputTruncated ? output[^maxOutputCharacters..] : output;
        var triage = BuildLogTriage.Analyze(
            boundedOutput,
            includePathPatterns ?? Array.Empty<string>(),
            excludePathPatterns ?? Array.Empty<string>(),
            changedFiles ?? Array.Empty<string>(),
            maxResults: 50);
        var diagnostics = triage.Diagnostics.ToList();
        if (isOutputTruncated)
        {
            diagnostics.Add($"VerificationOutputTruncated: kept the last {maxOutputCharacters} of {output.Length} characters.");
        }
        if (timedOut)
        {
            diagnostics.Add($"VerificationTimedOut: timeout elapsed after {elapsedMilliseconds} milliseconds.");
        }

        return new WorkspaceQueryResult<CSharpVerificationExecutionResult>
        {
            Items = new[]
            {
                new CSharpVerificationExecutionResult
                {
                    Kind = kind,
                    TargetPath = targetPath,
                    CommandSummary = commandSummary,
                    Succeeded = !timedOut && exitCode == 0,
                    TimedOut = timedOut,
                    ExitCode = exitCode,
                    ElapsedMilliseconds = (int)Math.Min(int.MaxValue, elapsedMilliseconds),
                    Output = boundedOutput,
                    IsOutputTruncated = isOutputTruncated,
                    Triage = triage.Items.FirstOrDefault() ?? new BuildTriageReport(),
                },
            },
            Diagnostics = diagnostics,
            IsPartial = timedOut || isOutputTruncated || triage.IsPartial,
        };
    }

    private static WorkspaceQueryResult<CSharpVerificationExecutionResult> Failure(string diagnostic)
    {
        return new WorkspaceQueryResult<CSharpVerificationExecutionResult>
        {
            IsPartial = true,
            Diagnostics = new[] { diagnostic },
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static string Quote(string value)
    {
        return value.Contains(' ') ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
    }
}
