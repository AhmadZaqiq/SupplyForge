using System.Text.RegularExpressions;

namespace SupplyForge.ArchitectureTests;

/// <summary>
/// PLT-022 endpoint coverage: every Host endpoint must declare an explicit
/// perm:* policy, the create-user policy, or explicit AllowAnonymous.
/// Bare RequireAuthorization() / bare IAuthorizeData is not sufficient.
/// Primary control is the fail-closed Fallback/Default policy; this is the
/// additional safeguard.
/// </summary>
public partial class EndpointAuthorizationCoverageTests
{
    [GeneratedRegex(@"\.RequireAuthorization\s*\(\s*\)")]
    private static partial Regex BareRequireAuthorization();

    [GeneratedRegex(@"\.Map(?:Get|Post|Put|Delete|Patch)\s*\(")]
    private static partial Regex MapCall();

    [Fact]
    public void PLT_022_BareRequireAuthorization_RejectedByCoverageTest()
    {
        const string sample = """
            app.MapGet("/x", () => Results.Ok()).RequireAuthorization();
            """;
        Assert.True(HasCoverageViolation(sample), "bare RequireAuthorization() must be a coverage violation");
    }

    [Fact]
    public void PLT_022_NoMetadata_Endpoint_RejectedByCoverageTest()
    {
        const string sample = """
            app.MapGet("/x", () => Results.Ok());
            """;
        Assert.True(HasCoverageViolation(sample), "metadata-less endpoint must be a coverage violation");
    }

    [Fact]
    public void PLT_022_AllEndpoints_DeclarePermissionCreateUserOrAnonymous()
    {
        var files = HostEndpointSourceFiles();
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (var violation in FindEndpointViolations(source, Path.GetFileName(file)))
            {
                violations.Add(violation);
            }
        }

        Assert.True(
            violations.Count == 0,
            "PLT-022 coverage violations (need perm:*, create-user, or AllowAnonymous):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static bool HasCoverageViolation(string source) =>
        FindEndpointViolations(source, "sample").Count > 0;

    /// <summary>
    /// Scans Map* endpoint registrations. A violation is bare RequireAuthorization()
    /// or a Map* statement that never reaches AllowAnonymous / RequireAuthorization("...").
    /// </summary>
    internal static List<string> FindEndpointViolations(string source, string sourceName)
    {
        var violations = new List<string>();

        if (BareRequireAuthorization().IsMatch(source))
        {
            violations.Add($"{sourceName}: bare .RequireAuthorization() — use perm:*, create-user, or AllowAnonymous");
        }

        foreach (Match map in MapCall().Matches(source))
        {
            var statement = ExtractStatement(source, map.Index);
            if (statement.Length == 0)
            {
                continue;
            }

            var hasBareRequire = BareRequireAuthorization().IsMatch(statement);
            // Any non-bare RequireAuthorization(...) — string policy name or
            // constant (e.g. SupplyForgeAuthorization.CreateUserPolicy).
            var hasNamedRequire = !hasBareRequire
                && statement.Contains(".RequireAuthorization(", StringComparison.Ordinal);
            var hasAllowAnonymous = statement.Contains(".AllowAnonymous(", StringComparison.Ordinal);

            if (hasBareRequire)
            {
                // Already reported globally; still flag the endpoint line.
                violations.Add($"{sourceName}: endpoint at offset {map.Index} uses bare RequireAuthorization()");
                continue;
            }

            if (!hasNamedRequire && !hasAllowAnonymous)
            {
                violations.Add(
                    $"{sourceName}: endpoint at offset {map.Index} has no perm:*, create-user, or AllowAnonymous metadata");
            }
        }

        return violations;
    }

    /// <summary>
    /// Extracts from ".MapX(" through the terminating ';' at brace/paren depth 0
    /// (lambda bodies keep depth above zero so inner ';' do not end the statement).
    /// </summary>
    private static string ExtractStatement(string source, int mapStart)
    {
        var open = source.IndexOf('(', mapStart);
        if (open < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;
        var verbatim = false;

        for (var i = open; i < source.Length; i++)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (!verbatim && c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                    verbatim = false;
                }

                continue;
            }

            if (inChar)
            {
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (c == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '@' && next == '"')
            {
                inString = true;
                verbatim = true;
                i++;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '\'')
            {
                inChar = true;
                continue;
            }

            if (c is '(' or '{' or '[')
            {
                depth++;
            }
            else if (c is ')' or '}' or ']')
            {
                depth--;
            }
            else if (c == ';' && depth == 0)
            {
                return source[mapStart..(i + 1)];
            }
        }

        return source[mapStart..];
    }

    private static IEnumerable<string> HostEndpointSourceFiles()
    {
        var root = FindRepoRoot();
        var host = Path.Combine(root, "src", "Host", "SupplyForge.Host");
        Assert.True(Directory.Exists(host), $"Host source not found at {host}");

        yield return Path.Combine(host, "Program.cs");
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(host, "Endpoints"), "*.cs", SearchOption.AllDirectories))
        {
            yield return file;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SupplyForge.slnx"))
                || File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
