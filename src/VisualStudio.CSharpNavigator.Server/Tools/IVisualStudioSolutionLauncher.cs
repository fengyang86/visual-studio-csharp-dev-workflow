using System.Diagnostics;

namespace VisualStudio.CSharpNavigator.Server.Tools;

public interface IVisualStudioSolutionLauncher
{
    string? FindDevenvPath();

    VisualStudioLaunchResult LaunchSolution(string devenvPath, string solutionPath);
}

public sealed class VisualStudioLaunchResult
{
    public int ProcessId { get; init; }

    public string Diagnostic { get; init; } = string.Empty;

    public string[] Diagnostics { get; init; } = Array.Empty<string>();
}

public sealed class VisualStudioSolutionLauncher : IVisualStudioSolutionLauncher
{
    public string? FindDevenvPath()
    {
        var vswherePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");
        if (File.Exists(vswherePath))
        {
            var result = RunVswhere(vswherePath);
            if (!string.IsNullOrWhiteSpace(result) && File.Exists(result))
            {
                return result;
            }
        }

        var pathResult = FindOnPath("devenv.exe");
        if (!string.IsNullOrWhiteSpace(pathResult))
        {
            return pathResult;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var commonCandidates = new[]
        {
            Path.Combine(programFiles, "Microsoft Visual Studio", "2026", "Enterprise", "Common7", "IDE", "devenv.exe"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2026", "Professional", "Common7", "IDE", "devenv.exe"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2026", "Community", "Common7", "IDE", "devenv.exe"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Enterprise", "Common7", "IDE", "devenv.exe"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Professional", "Common7", "IDE", "devenv.exe"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Community", "Common7", "IDE", "devenv.exe"),
        };
        return commonCandidates.FirstOrDefault(File.Exists);
    }

    public VisualStudioLaunchResult LaunchSolution(string devenvPath, string solutionPath)
    {
        var workingDirectory = Path.GetDirectoryName(devenvPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = devenvPath,
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory,
        };
        EnsureWindowsEnvironment(startInfo);
        startInfo.ArgumentList.Add(solutionPath);

        var process = Process.Start(startInfo);
        if (process is null)
        {
            return new VisualStudioLaunchResult
            {
                Diagnostic = "Process.Start returned null when launching Visual Studio.",
            };
        }

        return new VisualStudioLaunchResult
        {
            ProcessId = process.Id,
            Diagnostics = BuildLaunchDiagnostics(startInfo),
        };
    }

    private static void EnsureWindowsEnvironment(ProcessStartInfo startInfo)
    {
        var windowsDirectory = FirstNonEmpty(
            Environment.GetEnvironmentVariable("windir"),
            Environment.GetEnvironmentVariable("WINDIR"),
            Environment.GetEnvironmentVariable("SystemRoot"),
            Environment.GetEnvironmentVariable("windir", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("windir", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("SystemRoot", EnvironmentVariableTarget.Machine),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        if (!string.IsNullOrWhiteSpace(windowsDirectory))
        {
            startInfo.Environment["windir"] = windowsDirectory;
            startInfo.Environment["WINDIR"] = windowsDirectory;
            startInfo.Environment["SystemRoot"] = windowsDirectory;
        }

        var comSpec = FirstNonEmpty(
            Environment.GetEnvironmentVariable("ComSpec"),
            Environment.GetEnvironmentVariable("ComSpec", EnvironmentVariableTarget.Machine));
        if (!string.IsNullOrWhiteSpace(comSpec))
        {
            startInfo.Environment["ComSpec"] = comSpec;
        }
    }

    private static string[] BuildLaunchDiagnostics(ProcessStartInfo startInfo)
    {
        var diagnostics = new List<string>();
        if (startInfo.Environment.TryGetValue("windir", out var windir) &&
            !string.IsNullOrWhiteSpace(windir))
        {
            diagnostics.Add($"VisualStudioLaunchEnvironment: windir={windir}; useShellExecute={startInfo.UseShellExecute}.");
        }
        else
        {
            diagnostics.Add("VisualStudioLaunchEnvironmentWarning: windir is missing from the devenv.exe launch environment. WPF package initialization can fail with UriFormatException.");
        }

        if (startInfo.Environment.TryGetValue("SystemRoot", out var systemRoot) &&
            !string.IsNullOrWhiteSpace(systemRoot))
        {
            diagnostics.Add($"VisualStudioLaunchEnvironment: SystemRoot={systemRoot}.");
        }
        else
        {
            diagnostics.Add("VisualStudioLaunchEnvironmentWarning: SystemRoot is missing from the devenv.exe launch environment.");
        }

        return diagnostics.ToArray();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? RunVswhere(string vswherePath)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = vswherePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.StartInfo.ArgumentList.Add("-latest");
            process.StartInfo.ArgumentList.Add("-products");
            process.StartInfo.ArgumentList.Add("*");
            process.StartInfo.ArgumentList.Add("-requires");
            process.StartInfo.ArgumentList.Add("Microsoft.VisualStudio.Component.CoreEditor");
            process.StartInfo.ArgumentList.Add("-property");
            process.StartInfo.ArgumentList.Add("productPath");

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return output;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (PathTooLongException)
            {
            }
        }

        return null;
    }
}
