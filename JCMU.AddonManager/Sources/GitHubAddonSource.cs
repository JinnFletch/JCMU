using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.AddonManager.Models;
using JinnDev.JCMU.AddonManager.Utilities;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.AddonManager.Sources;

/// <summary>
/// Implements the Addon Source by querying the public GitHub REST API.
/// </summary>
public class GitHubAddonSource : IAddonSource
{
    private readonly HttpClient _httpClient;
    private const string GitHubApiBase = "https://api.github.com";

    public GitHubAddonSource()
    {
        _httpClient = new HttpClient();
        // GitHub API requires a User-Agent header.
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JCMU.AddonManager", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public async Task<Maybe<AddonPagedResult>> SearchAsync(string? query, int page = 1)
    {
        return await Maybe.TryAsync<AddonPagedResult>(async () =>
        {
            // If query is null/empty, we just search the topic globally
            // Enforce searching only for repos tagged with our specific ecosystem topic
            var searchTerm = string.IsNullOrWhiteSpace(query) ? "topic:jcmu-addon" : $"{query} topic:jcmu-addon";
            var encodedQuery = Uri.EscapeDataString(searchTerm);

            var requestUrl = $"{GitHubApiBase}/search/repositories?q={encodedQuery}&sort=stars&order=desc&per_page=10&page={page}";

            using var response = await _httpClient.GetAsync(requestUrl).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GitHubSearchResponse>(json)
                         ?? throw new Exception("Failed to deserialize GitHub search response.");

            var mappedResults = result.Items.Select(repo => new AddonSearchResult
            {
                AddonId = repo.Name,
                RepositoryUrl = repo.HtmlUrl,
                Description = repo.Description,
                Author = repo.Owner?.Login,
                Tags = repo.Topics ?? []
            }).ToList();

            return new AddonPagedResult(mappedResults, result.TotalCount);
        }).ConfigureAwait(false);
    }

    public async Task<Maybe<IReadOnlyList<AddonVersionInfo>>> GetVersionsAsync(string repositoryUrl)
    {
        return await Maybe.TryAsync<IReadOnlyList<AddonVersionInfo>>(async () =>
        {
            // Convert 'https://github.com/author/repo' to 'author/repo'
            if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) || uri.Segments.Length < 3)
                throw new ArgumentException($"Invalid GitHub repository URL: {repositoryUrl}");

            var owner = uri.Segments[1].Trim('/');
            var repo = uri.Segments[2].Trim('/');
            var requestUrl = $"{GitHubApiBase}/repos/{owner}/{repo}/tags";

            using var response = await _httpClient.GetAsync(requestUrl).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var tags = JsonSerializer.Deserialize<List<GitHubTagResponse>>(json)
                       ?? throw new Exception("Failed to deserialize GitHub tags response.");

            var mappedVersions = tags.Select(tag => new AddonVersionInfo
            {
                VersionName = tag.Name,
                CommitHash = tag.Commit?.Sha ?? string.Empty,
                IsPreRelease = tag.Name.Contains("-alpha", StringComparison.OrdinalIgnoreCase) ||
                               tag.Name.Contains("-beta", StringComparison.OrdinalIgnoreCase)
            }).ToList();

            return mappedVersions;
        }).ConfigureAwait(false);
    }

    public async Task<Maybe> DownloadSourceAsync(string repositoryUrl, string version, string targetTempPath)
    {
        return await Maybe.TryAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
                throw new ArgumentException("Repository URL cannot be empty.");

            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("Version cannot be empty.");

            if (string.IsNullOrWhiteSpace(targetTempPath))
                throw new ArgumentException("Target temp path cannot be empty.");

            // Use shallow clone (--depth 1) to grab only the specific tag/branch without full commit history
            var arguments = new[] { "clone", "--branch", version, "--depth", "1", repositoryUrl, targetTempPath };
            var cloneResult = await CliRunner.RunAsync("git", arguments).ConfigureAwait(false);

            // If the CliRunner monad failed, we throw to let the outer TryAsync catch and propagate it
            if (!cloneResult.HasValue)
            {
                throw new Exception($"Failed to clone repository: {cloneResult.Message}", cloneResult.Exception);
            }
        }).ConfigureAwait(false);
    }

    public async Task<Maybe<PluginManifest>> GetRemoteManifestAsync(string repositoryUrl)
    {
        return await Maybe.TryAsync<PluginManifest>(async () =>
        {
            if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) || uri.Segments.Length < 3)
                throw new ArgumentException($"Invalid GitHub repository URL: {repositoryUrl}");

            var owner = uri.Segments[1].Trim('/');
            var repo = uri.Segments[2].Trim('/');

            // Convention 1: Root of the repo (manifest.json)
            // Convention 2: Subfolder named after the repo (RepoName/manifest.json)
            var possiblePaths = new[] { "manifest.json", $"{repo}/manifest.json" };

            string? manifestJson = null;

            foreach (var path in possiblePaths)
            {
                var requestUrl = $"{GitHubApiBase}/repos/{owner}/{repo}/contents/{path}";
                using var response = await _httpClient.GetAsync(requestUrl).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var contentDto = JsonSerializer.Deserialize<GitHubContentResponse>(jsonResponse);
                    if (contentDto != null)
                    {
                        var base64 = contentDto.Content.Replace("\n", "").Replace("\r", "");
                        manifestJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                        break; // Found it!
                    }
                }
            }

            if (string.IsNullOrEmpty(manifestJson))
            {
                throw new Exception("manifest.json not found in the repository root or expected subfolders. " +
                                    "Ensure your manifest is in the root or a folder matching the repository name.");
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson, options)
                           ?? throw new Exception("Failed to parse the remote manifest.json.");

            var validation = manifest.Validate();
            if (!validation.HasValue)
                throw new Exception($"Remote manifest validation failed: {validation.Message}");

            return manifest;
        }).ConfigureAwait(false);
    }

    #region Private DTOs for GitHub JSON Mapping

    private record GitHubSearchResponse(
        [property: JsonPropertyName("total_count")] int TotalCount,
        [property: JsonPropertyName("items")] List<GitHubRepoDto> Items);

    private record GitHubRepoDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("owner")] GitHubOwnerDto? Owner,
        [property: JsonPropertyName("topics")] List<string>? Topics);

    private record GitHubOwnerDto([property: JsonPropertyName("login")] string Login);

    private record GitHubTagResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("commit")] GitHubCommitDto? Commit);

    private record GitHubCommitDto([property: JsonPropertyName("sha")] string Sha);

    private record GitHubContentResponse([property: JsonPropertyName("content")] string Content);

    #endregion
}