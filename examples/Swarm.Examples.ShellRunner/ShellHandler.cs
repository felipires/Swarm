using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swarm.Sdk.Abstractions;

namespace Swarm.Examples.ShellRunner;

/// <summary>
/// Runs an arbitrary shell command and returns stdout, stderr, and exit code.
///
/// Config shape (TaskDefinition.ConfigJson):
/// {
///   "command": "echo {param:message}",
///   "shell": "bash",
///   "workingDirectory": "/tmp",
///   "timeoutSeconds": 30,
///   "env": { "MY_VAR": "value" },
///   "successExitCodes": [0]
/// }
///
/// Result JSON:
/// { "exitCode": 0, "stdout": "hello\n", "stderr": "" }
/// </summary>
public sealed class ShellHandler : TaskHandler<ShellHandler.ShellConfig>
{
    public override string TaskType => "shell@1";

    public override HandlerSchema Schema { get; } = new()
    {
        JsonSchema = """
            {
              "type": "object",
              "required": ["command"],
              "properties": {
                "command":          { "type": "string" },
                "shell":            { "type": "string", "description": "bash | sh | cmd | powershell. Defaults to OS default." },
                "workingDirectory": { "type": "string" },
                "timeoutSeconds":   { "type": "integer", "minimum": 1 },
                "env":              { "type": "object", "additionalProperties": { "type": "string" } },
                "successExitCodes": { "type": "array", "items": { "type": "integer" } }
              }
            }
            """,
        RequiredParams = [],
    };

    protected override async Task<TaskResult> HandleAsync(ShellConfig config, TaskContext context)
    {
        if (string.IsNullOrWhiteSpace(config.Command))
            return new TaskResult(false, ErrorMessage: "CONFIG_INVALID: command is required");

        var (exe, args) = ResolveShell(config.Shell, config.Command);

        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        if (!string.IsNullOrEmpty(config.WorkingDirectory))
            psi.WorkingDirectory = config.WorkingDirectory;

        if (config.Env is { Count: > 0 })
            foreach (var (k, v) in config.Env)
                psi.Environment[k] = v;

        using var process = new Process { StartInfo = psi };
        process.Start();

        var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 30);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return new TaskResult(false, ErrorMessage: "SHELL_TIMEOUT");
        }

        var stdout   = await process.StandardOutput.ReadToEndAsync();
        var stderr   = await process.StandardError.ReadToEndAsync();
        var exitCode = process.ExitCode;

        context.Logger.LogInformation("Shell exited {ExitCode}: {Command}", exitCode, config.Command);

        var successCodes = config.SuccessExitCodes is { Length: > 0 } ? config.SuccessExitCodes : [0];
        var resultJson   = JsonSerializer.Serialize(new { exitCode, stdout, stderr });

        return successCodes.Contains(exitCode)
            ? new TaskResult(true, ResultJson: resultJson)
            : new TaskResult(false, ResultJson: resultJson, ErrorMessage: $"SHELL_NONZERO_EXIT: {exitCode}");
    }

    private static (string exe, string args) ResolveShell(string? shell, string command) =>
        shell?.ToLowerInvariant() switch
        {
            "bash"       => ("bash",            $"-c \"{Esc(command)}\""),
            "sh"         => ("sh",              $"-c \"{Esc(command)}\""),
            "powershell" => ("powershell.exe",  $"-Command {command}"),
            "pwsh"       => ("pwsh",            $"-Command {command}"),
            "cmd"        => ("cmd.exe",         $"/c {command}"),
            _ when OperatingSystem.IsWindows() => ("cmd.exe", $"/c {command}"),
            _            => ("sh",              $"-c \"{Esc(command)}\""),
        };

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public sealed class ShellConfig
    {
        public string? Command          { get; set; }
        public string? Shell            { get; set; }
        public string? WorkingDirectory { get; set; }
        public int     TimeoutSeconds   { get; set; }
        public Dictionary<string, string>? Env { get; set; }
        public int[]?  SuccessExitCodes { get; set; }
    }
}
