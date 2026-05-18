using System.Text.Json;
using System.Text.RegularExpressions;
using JinnDev.JCMU.AddonManager.Models;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.CoreTools.Tools;

public class PublishTool : ICoreTool
{
    private readonly IStatelessRunner _cmdRunner;

    public string ToolId => "Core.Publish";

    public MenuDefinition Menu => new MenuDefinition
    {
        MenuItemName = "Publish Addon to GitHub",
        IconPath = "imageres.dll,-104", // Standard sync/update icon
        Ordinal = 30,
        RunInBackground = false // Needs visible console for prompts and output
    };

    public PublishTool(IStatelessRunner cmdRunner)
    {
        _cmdRunner = cmdRunner;
    }

    public async Task<Maybe> ExecuteAsync(string targetDirectory)
    {
        Console.WriteLine("\n--- Initializing JCMU Publish Pipeline ---");

        var contextMaybe = await Maybe.Try<PublishContext>(() => new PublishContext { TargetDirectory = targetDirectory.Trim('"') })
            .BindAsync(LocateProjectFileAsync)
            .BindAsync(ValidateJcmuSdkDependencyAsync)
            .BindAsync(ExtractManifestAsync)
            .BindAsync(ExtractVersionAsync)
            .BindAsync(GetGitHubOwnerRepoAsync)
            .BindAsync(EnsureGitHubTopicAsync)
            .BindAsync(CheckExistingReleaseAsync)
            .BindAsync(PromptForVersionOverrideAsync)
            .BindAsync(EnsureGitIntegrityAsync)
            .BindAsync(ExecuteDotnetBuildAsync)
            .BindAsync(CreateGitHubReleaseAsync)
            .ConfigureAwait(false);

        return contextMaybe.Bind(ctx => Maybe.SUCCESS); // Temporary terminator for Step 2
    }

    private async Task<Maybe<PublishContext>> GetGitHubOwnerRepoAsync(PublishContext ctx)
    {
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        var request = CommandBuilder.Create("git")
            .WithArgument("remote get-url origin")
            .InDirectory(projectDir)
            .Build();

        return await _cmdRunner.RunBufferedAsync(request)
            .EnsureSuccessAsync("Failed to read git remote. Ensure the project is a Git repository.")
            .BindAsync(cmdResult =>
            {
                var remoteUrl = cmdResult.StandardOutput.Trim();

                // Handle both HTTPS (https://github.com/owner/repo.git) and SSH (git@github.com:owner/repo.git)
                var match = Regex.Match(remoteUrl, @"github\.com[:/](.+?)/(.+?)(\.git)?$", RegexOptions.IgnoreCase);

                if (!match.Success)
                    return Maybe.None<PublishContext>($"Could not parse GitHub Owner/Repo from remote URL: {remoteUrl}");

                var owner = match.Groups[1].Value;
                var repo = match.Groups[2].Value;

                Console.WriteLine($"[Valid] Remote: {owner}/{repo}");

                return Maybe.Some(ctx with { Owner = owner, Repo = repo });
            }).ConfigureAwait(false);
    }

    private async Task<Maybe<PublishContext>> EnsureGitHubTopicAsync(PublishContext ctx)
    {
        Console.WriteLine("\n--- Checking GitHub Discoverability ---");
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        // 1. Check existing topics
        var checkRequest = CommandBuilder.Create("gh")
            .WithArgument($"api repos/{ctx.Owner}/{ctx.Repo}/topics")
            .InDirectory(projectDir)
            .Build();

        return await _cmdRunner.RunBufferedAsync(checkRequest)
            .EnsureSuccessAsync("GitHub API check failed. Ensure GitHub CLI (gh.exe) is installed and authenticated.")
            .BindAsync(async cmdResult => {
                var json = cmdResult.StandardOutput.Trim();

                // Quick string check rather than full JSON deserialization for this simple payload
                if (json.Contains("\"jcmu-addon\"", StringComparison.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[OK] Repository is correctly tagged with 'jcmu-addon'.");
                    Console.ResetColor();
                    return Maybe.Some(ctx);
                }

                // 2. Inject missing topic
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[INFO] Missing 'jcmu-addon' topic. Injecting to ensure store discoverability...");
                Console.ResetColor();

                var putRequest = CommandBuilder.Create("gh")
                    .WithArgument($"api -X PUT repos/{ctx.Owner}/{ctx.Repo}/topics")
                    .WithArgument("-f names[]=jcmu-addon")
                    .InDirectory(projectDir)
                    .Build();

                return await _cmdRunner.RunBufferedAsync(putRequest)
                    .EnsureSuccessAsync("Failed to update GitHub topics. Ensure you have admin rights to the repo.")
                    .BindAsync(putResult =>
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[SUCCESS] Topic added.");
                        Console.ResetColor();

                        return Maybe.Some(ctx);
                    })
                    .ConfigureAwait(false);
            })
            .ConfigureAwait(false);
    }

    private async Task<Maybe<PublishContext>> CheckExistingReleaseAsync(PublishContext ctx)
    {
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        var request = CommandBuilder.Create("gh")
            .WithArgument($"release view v{ctx.InitialVersion}")
            .InDirectory(projectDir)
            .Build();

        // We AWAIT the command result separately so its failure doesn't 
        // propagate into the main tool's pipeline.
        await _cmdRunner.RunBufferedAsync(request)
            .TapAsync(cmd =>
            {
                // In THIS check, ExitCode 0 is the "Warning" state
                if (cmd.ExitCode == 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"\n[WARNING] GitHub Release 'v{ctx.InitialVersion}' already exists!");
                    Console.WriteLine("If you proceed with this version, the release creation step will fail.");
                    Console.ResetColor();
                }
            })
            // If gh.exe is missing or git fails, TapNone would be good for logging
            .TapNoneAsync(err => Console.WriteLine($"[DEBUG] Release check skipped: {err.Message}"))
            .ConfigureAwait(false);

        // ALWAYS return the context. The train must go on!
        return Maybe.Some(ctx);
    }

    private Task<Maybe<PublishContext>> LocateProjectFileAsync(PublishContext ctx)
    {
        if (!Directory.Exists(ctx.TargetDirectory))
            return Task.FromResult(Maybe.None<PublishContext>($"Target directory does not exist: {ctx.TargetDirectory}"));

        // Get all .csproj files within 2 directory levels
        var allCsprojs = Directory.GetFiles(ctx.TargetDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(f =>
            {
                var relativePath = Path.GetRelativePath(ctx.TargetDirectory, f);
                var depth = relativePath.Split(Path.DirectorySeparatorChar).Length;
                return depth <= 2;
            })
            .ToList();

        if (allCsprojs.Count == 0)
            return Task.FromResult(Maybe.None<PublishContext>("No .csproj file found in the target directory (or 1 level deep)."));

        if (allCsprojs.Count > 1)
            return Task.FromResult(Maybe.None<PublishContext>($"Multiple .csproj files found. The publish tool requires exactly one to resolve context.\nFound: {string.Join(", ", allCsprojs)}"));

        return Task.FromResult(Maybe.Some(ctx with { ProjectFilePath = allCsprojs[0] }));
    }

    private async Task<Maybe<PublishContext>> ValidateJcmuSdkDependencyAsync(PublishContext ctx)
    {
        var csprojContent = await File.ReadAllTextAsync(ctx.ProjectFilePath).ConfigureAwait(false);

        if (!csprojContent.Contains("<PackageReference Include=\"JinnDev.JCMU.SDK\"", StringComparison.OrdinalIgnoreCase))
        {
            return Maybe.None<PublishContext>("The target project does not appear to be a JCMU Addon (Missing JinnDev.JCMU.SDK PackageReference).");
        }

        return Maybe.Some(ctx);
    }

    private async Task<Maybe<PublishContext>> ExtractManifestAsync(PublishContext ctx)
    {
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;
        var manifestPath = Path.Combine(projectDir, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return Maybe.None<PublishContext>("No manifest.json found adjacent to the .csproj file.");
        }

        var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (manifest == null || string.IsNullOrWhiteSpace(manifest.AddonId))
        {
            return Maybe.None<PublishContext>("Failed to parse manifest.json or missing AddonId.");
        }

        return Maybe.Some(ctx with { ManifestFilePath = manifestPath, Manifest = manifest });
    }

    private async Task<Maybe<PublishContext>> ExtractVersionAsync(PublishContext ctx)
    {
        var csprojContent = await File.ReadAllTextAsync(ctx.ProjectFilePath).ConfigureAwait(false);

        // Matches <Version>1.0.0</Version> or <PackageVersion>1.0.0</PackageVersion>
        var match = Regex.Match(csprojContent, @"<(?:Package)?Version>(.+?)<\/(?:Package)?Version>", RegexOptions.IgnoreCase);

        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
        {
            return Maybe.None<PublishContext>("Could not extract <Version> from the .csproj. Ensure the project explicitly defines a version.");
        }

        var extractedVersion = match.Groups[1].Value.Trim();

        Console.WriteLine($"[Valid] Project: {Path.GetFileName(ctx.ProjectFilePath)}");
        Console.WriteLine($"[Valid] AddonId: {ctx.Manifest!.AddonId}");
        Console.WriteLine($"[Valid] Version: {extractedVersion}");

        return Maybe.Some(ctx with { InitialVersion = extractedVersion, FinalVersion = extractedVersion });
    }

    private Task<Maybe<PublishContext>> PromptForVersionOverrideAsync(PublishContext ctx)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"Current version is {ctx.InitialVersion}. Enter new version or press Enter to keep: ");
        Console.ResetColor();

        var input = Console.ReadLine()?.Trim();

        var finalVersion = string.IsNullOrWhiteSpace(input) ? ctx.InitialVersion : input;

        return Task.FromResult(Maybe.Some(ctx with { FinalVersion = finalVersion }));
    }

    private async Task<Maybe<PublishContext>> EnsureGitIntegrityAsync(PublishContext ctx)
    {
        // If the user kept the original version, we assume they know what they are doing
        // and skip the strict git enforcement loop.
        if (ctx.InitialVersion.Equals(ctx.FinalVersion, StringComparison.OrdinalIgnoreCase))
            return Maybe.Some(ctx);

        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        // We initialize our state for the loop
        Maybe<PublishContext> result = Maybe.Some(ctx);
        bool shouldLoop = true;

        while (shouldLoop)
        {
            var branchReq = CommandBuilder.Create("git").WithArgument("branch --show-current").InDirectory(projectDir).Build();
            var statusReq = CommandBuilder.Create("git").WithArgument("status --porcelain").InDirectory(projectDir).Build();

            // 1. Combine both git commands into a single (Branch, Status) tuple
            // Using EnsureSuccessAsync to treat any git error as a pipeline failure
            var gitCheckResult = await _cmdRunner.RunBufferedAsync(branchReq)
                .EnsureSuccessAsync("Git branch check failed.")
                .BindAsync(b => _cmdRunner.RunBufferedAsync(statusReq)
                    .EnsureSuccessAsync("Git status check failed.")
                    .MapAsync(s => (Branch: b.StandardOutput.Trim(), Status: s.StandardOutput.Trim()))).ConfigureAwait(false);

            // 2. Use MatchAsync to process the result or the error
            var iteration = await gitCheckResult.MatchAsync(
                someAsync: async state =>
                {
                    bool isMainOrMaster = state.Branch.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                                          state.Branch.Equals("master", StringComparison.OrdinalIgnoreCase);
                    bool isClean = string.IsNullOrWhiteSpace(state.Status);

                    if (isMainOrMaster && isClean)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[OK] Repository is clean and on the main branch.");
                        Console.ResetColor();

                        shouldLoop = false; // We are done!
                        return Maybe.SUCCESS;
                    }

                    // UI Logic
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    if (!isMainOrMaster) Console.WriteLine($"[WARNING] Not on main/master branch (Current: '{state.Branch}').");
                    if (!isClean) Console.WriteLine("[WARNING] Repository has uncommitted changes.");

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("Fix and [Enter] to re-check, or [C] to proceed anyway: ");
                    Console.ResetColor();

                    var input = Console.ReadLine()?.Trim();

                    if (input?.Equals("C", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine("Overriding strict git requirements. Proceeding...");
                        Console.ResetColor();
                        shouldLoop = false; // User forced continuation
                    }

                    return Maybe.SUCCESS;
                },
                noneAsync: async err =>
                {
                    // If a command failed, we update the result and stop the loop
                    result = Maybe.None<PublishContext>(err.Message);
                    shouldLoop = false;
                    return Maybe.SUCCESS;
                }
            ).ConfigureAwait(false);

            // If the Match itself failed (e.g. unexpected exception), propagate the error
            if (!iteration.HasValue) return Maybe.None<PublishContext>(iteration.Message);
        }

        return result;
    }

    private async Task<Maybe<PublishContext>> ExecuteDotnetBuildAsync(PublishContext ctx)
    {
        Console.WriteLine($"\n--- Building Project (v{ctx.FinalVersion}) ---");
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        var buildRequest = CommandBuilder.Create("dotnet")
            .WithArgument("build")
            .WithArgument("-c Release")
            // Inject the version in-memory to avoid mutating the source file
            .WithArgument($"-p:Version={ctx.FinalVersion}")
            .InDirectory(projectDir)
            .Build();

        return await _cmdRunner.RunBufferedAsync(buildRequest)
            .EnsureSuccessAsync(cmd => $"Build failed:\n{cmd.StandardOutput}")
            .BindAsync(_ =>
            {
                // EnsureSuccess automatically appends cmd.StandardError to whatever string you return above!
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[SUCCESS] Build completed cleanly.");
                Console.ResetColor();

                return Maybe.Some(ctx);
            }).ConfigureAwait(false);
    }

    private async Task<Maybe<PublishContext>> CreateGitHubReleaseAsync(PublishContext ctx)
    {
        Console.WriteLine($"\n--- Creating GitHub Release (v{ctx.FinalVersion}) ---");
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        var releaseRequest = CommandBuilder.Create("gh")
            .WithArgument("release create")
            .WithArgument($"v{ctx.FinalVersion}")
            .WithArgument("--title")
            .WithQuotedArgument($"v{ctx.FinalVersion}")
            .WithArgument("--generate-notes")
            .InDirectory(projectDir)
            .Build();

        return await _cmdRunner.RunBufferedAsync(releaseRequest)
            .EnsureSuccessAsync(cmd =>
            {
                // Inspect the CLI error to provide a better prefix
                if (cmd.StandardError.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Release creation failed: The tag 'v{ctx.FinalVersion}' already exists on GitHub.";
                }

                return "Failed to create GitHub release. Check your connection or 'gh' authentication.";
            })
            .BindAsync(_ =>
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SUCCESS] GitHub Release v{ctx.FinalVersion} created successfully!");
                Console.ResetColor();
                return Maybe.Some(ctx);
            }).ConfigureAwait(false);
    }
}