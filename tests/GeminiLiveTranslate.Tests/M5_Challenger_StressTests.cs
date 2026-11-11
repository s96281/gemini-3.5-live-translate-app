using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GeminiLiveTranslate.Tests.TestHelpers;
using Xunit;

namespace GeminiLiveTranslate.Tests;

/// <summary>
/// Milestone 5 Challenger 1 Adversarial Stress &amp; Edge-Case Test Suite.
/// Verifies mutation detection on build.yml via WorkflowYamlValidator and structural checks,
/// YAML syntax/security invariants, action pinning, permissions, and repository path cross-checks.
/// </summary>
public class M5_Challenger_StressTests
{
    private static readonly string SolutionRoot = ResolveSolutionRoot();
    private static readonly string WorkflowFilePath = Path.Combine(SolutionRoot, ".github", "workflows", "build.yml");

    private static string ResolveSolutionRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "GeminiLiveTranslate.sln")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current) ?? string.Empty;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    }

    private static string LoadRawWorkflow()
    {
        Assert.True(File.Exists(WorkflowFilePath), $"Workflow file missing at {WorkflowFilePath}");
        return File.ReadAllText(WorkflowFilePath);
    }

    #region 1. In-Memory Mutation Detection Tests (Adversarial Fault Injection)

    [Fact]
    public void Mutation_01_RemovingPublishSingleFile_IsCaughtByValidator()
    {
        string yaml = LoadRawWorkflow();
        string mutated = yaml.Replace("-p:PublishSingleFile=true", "-p:PublishSingleFile=false");
        var validator = new WorkflowYamlValidator(mutated);
        Assert.False(validator.HasWinX64SingleFilePublish());
    }

    [Theory]
    [InlineData("-r linux-x64")]
    [InlineData("-r osx-arm64")]
    [InlineData("-r win-x86")]
    [InlineData("-r win-arm64")]
    [InlineData("")]
    public void Mutation_02_ChangingRuntimeIdentifierFromWinX64_IsCaughtByValidator(string replacementRid)
    {
        string yaml = LoadRawWorkflow();
        string mutated = yaml.Replace("-r win-x64", replacementRid);
        var validator = new WorkflowYamlValidator(mutated);
        Assert.False(validator.HasWinX64SingleFilePublish());
    }

    [Theory]
    [InlineData("dotnet-version: '8.0.x'")]
    [InlineData("dotnet-version: '7.0.x'")]
    [InlineData("dotnet-version: '6.0.x'")]
    [InlineData("dotnet-version: 8.0.100")]
    public void Mutation_03_DowngradingDotNetVersionFrom9_IsCaughtByValidator(string downgradedVersion)
    {
        string yaml = LoadRawWorkflow();
        string mutated = yaml.Replace("dotnet-version: '9.0.x'", downgradedVersion);
        var validator = new WorkflowYamlValidator(mutated);
        Assert.False(validator.HasDotNet9Setup());
    }

    [Theory]
    [InlineData("runs-on: ubuntu-latest")]
    [InlineData("runs-on: macos-latest")]
    [InlineData("runs-on: ubuntu-22.04")]
    [InlineData("runs-on: self-hosted")]
    public void Mutation_04_ChangingRunnerToNonWindows_IsCaughtByValidator(string nonWindowsRunner)
    {
        string yaml = LoadRawWorkflow();
        string mutated = yaml.Replace("runs-on: windows-latest", nonWindowsRunner);
        var validator = new WorkflowYamlValidator(mutated);
        Assert.False(validator.HasWindowsRunner());
    }

    [Fact]
    public void Mutation_05_Windows2022RunnerAlternative_IsAcceptedByValidator()
    {
        string yaml = LoadRawWorkflow();
        string mutated = yaml.Replace("runs-on: windows-latest", "runs-on: windows-2022");
        var validator = new WorkflowYamlValidator(mutated);
        Assert.True(validator.HasWindowsRunner());
    }

    [Fact]
    public void Mutation_06_RemovingVersionTagTriggers_IsCaughtByValidator()
    {
        string yaml = LoadRawWorkflow();
        string mutated = yaml
            .Replace("tags: [ 'v*' ]", "tags: [ 'release-*' ]")
            .Replace("refs/tags/v", "refs/tags/release-");
        var validator = new WorkflowYamlValidator(mutated);
        Assert.False(validator.HasTagReleaseTrigger());
    }

    [Fact]
    public void Mutation_07_BreakingExecutableAndArtifactNaming_IsCaughtByValidator()
    {
        string yaml = LoadRawWorkflow();
        string mutated = yaml.Replace("GeminiLiveTranslate", "WrongAppName");
        var validator = new WorkflowYamlValidator(mutated);
        Assert.False(validator.HasValidArtifactNaming());
    }

    [Fact]
    public void Mutation_08_RemovingSelfContainedFlag_IsCaughtByValidator()
    {
        string yaml = LoadRawWorkflow();
        string mutated = yaml.Replace("--self-contained true", "--no-self-contained");
        var validator = new WorkflowYamlValidator(mutated);
        Assert.False(validator.HasWinX64SingleFilePublish());
    }

    [Fact]
    public void Mutation_09_RemovingDotNetPublishCommand_IsCaughtByValidator()
    {
        string yaml = LoadRawWorkflow();
        string mutated = yaml.Replace("dotnet publish", "dotnet build");
        var validator = new WorkflowYamlValidator(mutated);
        Assert.False(validator.HasWinX64SingleFilePublish());
    }

    [Fact]
    public void Mutation_10_NullOrEmptyYamlContent_FailsAllValidatorPredicates()
    {
        var emptyValidator = new WorkflowYamlValidator(string.Empty);
        Assert.False(emptyValidator.HasWindowsRunner());
        Assert.False(emptyValidator.HasDotNet9Setup());
        Assert.False(emptyValidator.HasWinX64SingleFilePublish());
        Assert.False(emptyValidator.HasTagReleaseTrigger());
        Assert.False(emptyValidator.HasValidArtifactNaming());

        var nullValidator = new WorkflowYamlValidator(null!);
        Assert.Empty(nullValidator.Lines);
        Assert.False(nullValidator.HasWindowsRunner());
    }

    [Fact]
    public void Mutation_11_FromFileWithNonExistentPath_ThrowsFileNotFoundException()
    {
        string missingPath = Path.Combine(SolutionRoot, ".github", "workflows", "nonexistent_workflow_12345.yml");
        Assert.Throws<FileNotFoundException>(() => WorkflowYamlValidator.FromFile(missingPath));
    }

    #endregion

    #region 2. Structural Integrity, Quotes, Expressions & Security Invariants

    [Fact]
    public void Structure_01_SingleLineQuotesAreBalancedAcrossEntireWorkflow()
    {
        string[] lines = LoadRawWorkflow().Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            // Count unescaped double quotes on each line
            int doubleQuotes = 0;
            for (int c = 0; c < line.Length; c++)
            {
                if (line[c] == '"' && (c == 0 || line[c - 1] != '\\'))
                {
                    doubleQuotes++;
                }
            }
            Assert.True(doubleQuotes % 2 == 0, $"Unbalanced double quotes on line {i + 1}: {line}");

            // Outside of double-quoted strings, single quotes should also be balanced
            if (doubleQuotes == 0)
            {
                int singleQuotes = line.Count(ch => ch == '\'');
                Assert.True(singleQuotes % 2 == 0, $"Unbalanced single quotes on line {i + 1}: {line}");
            }
        }
    }

    [Fact]
    public void Structure_02_GitHubExpressionsHaveBalancedBraces()
    {
        string yaml = LoadRawWorkflow();
        int openCount = Regex.Matches(yaml, @"\$\{\{").Count;
        int closeCount = Regex.Matches(yaml, @"\}\}").Count;
        Assert.True(openCount >= 3, "Expected at least 3 GitHub Actions ${{ ... }} expressions in build.yml");
        Assert.Equal(openCount, closeCount);
    }

    [Fact]
    public void Structure_03_NoDeprecatedActionVersionsOrUnpinnedBranches()
    {
        string[] lines = LoadRawWorkflow().Replace("\r\n", "\n").Split('\n');
        var usesLines = lines
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("uses:"))
            .Select(l => l.Substring("uses:".Length).Trim().Trim('\'', '"'))
            .ToList();

        Assert.Equal(4, usesLines.Count);

        foreach (string actionRef in usesLines)
        {
            // Must include @version tag
            Assert.Contains("@", actionRef);
            string[] parts = actionRef.Split('@');
            Assert.Equal(2, parts.Length);
            string actionName = parts[0];
            string version = parts[1];

            // Disallow mutable branch names like @main, @master, @HEAD
            Assert.NotEqual("main", version, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("master", version, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("HEAD", version, StringComparer.OrdinalIgnoreCase);

            // Disallow deprecated v1/v2/v3 for actions/checkout, actions/setup-dotnet, actions/upload-artifact
            if (actionName.StartsWith("actions/", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal("v4", version);
            }
            else if (actionName.Equals("softprops/action-gh-release", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal("v2", version);
            }
        }
    }

    [Fact]
    public void Structure_04_NoHardcodedApiKeysOrPlaintextSecretsInWorkflow()
    {
        string yaml = LoadRawWorkflow();
        // Google Gemini API keys start with AIzaSy
        Assert.DoesNotContain("AIzaSy", yaml, StringComparison.OrdinalIgnoreCase);
        // GitHub Personal Access Tokens start with ghp_ or github_pat_
        Assert.DoesNotContain("ghp_", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("github_pat_", yaml, StringComparison.OrdinalIgnoreCase);

        // GITHUB_TOKEN must only come from secrets.GITHUB_TOKEN
        Assert.Contains("GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}", yaml);
    }

    [Fact]
    public void Structure_05_ReleaseIfCondition_HandlesBothTagPushAndManualDispatchSafely()
    {
        string yaml = LoadRawWorkflow();
        Assert.Contains("if: startsWith(github.ref, 'refs/tags/v') || (github.event_name == 'workflow_dispatch' && github.event.inputs.tag_name != '')", yaml);
        Assert.Contains("tag_name: ${{ github.event.inputs.tag_name || github.ref_name }}", yaml);
    }

    [Fact]
    public void Structure_06_IndentationIsStrictlyMultiplesOfTwoSpacesForYamlKeys()
    {
        string[] lines = LoadRawWorkflow().Replace("\r\n", "\n").Split('\n');
        bool inMultilineBlock = false;
        int multilineBaseIndent = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            int indent = line.Length - line.TrimStart(' ').Length;
            if (inMultilineBlock)
            {
                if (indent > multilineBaseIndent)
                {
                    continue;
                }
                inMultilineBlock = false;
            }

            Assert.True(indent % 2 == 0, $"Line {i + 1} has odd indentation ({indent} spaces): '{line}'");

            if (line.TrimEnd().EndsWith('|'))
            {
                inMultilineBlock = true;
                multilineBaseIndent = indent;
            }
        }
    }

    #endregion

    #region 3. Repository Disk Cross-Check & Executable Normalization Verification

    [Fact]
    public void CrossCheck_01_AllReferencedSolutionAndProjectPathsExistOnDisk()
    {
        string yaml = LoadRawWorkflow();

        // Extract all .sln and .csproj paths referenced in build.yml
        MatchCollection pathMatches = Regex.Matches(yaml, @"[\w./\\-]+\.(sln|csproj)");
        Assert.NotEmpty(pathMatches);

        foreach (Match match in pathMatches)
        {
            string relativePath = match.Value.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(SolutionRoot, relativePath);
            Assert.True(File.Exists(fullPath), $"Referenced path '{match.Value}' in build.yml does not exist on disk at '{fullPath}'");
        }
    }

    [Fact]
    public void CrossCheck_02_AppCsprojAssemblyName_MatchesVerifyStepNormalizationSource()
    {
        string csprojPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "GeminiLiveTranslate.App.csproj");
        Assert.True(File.Exists(csprojPath));

        XDocument doc = XDocument.Load(csprojPath);
        string? assemblyName = doc.Descendants("AssemblyName").FirstOrDefault()?.Value;
        string effectiveExeName = string.IsNullOrWhiteSpace(assemblyName)
            ? "GeminiLiveTranslate.App.exe"
            : $"{assemblyName}.exe";

        string yaml = LoadRawWorkflow();
        // Either the csproj produces GeminiLiveTranslate.exe directly OR build.yml moves <AssemblyName>.exe to GeminiLiveTranslate.exe
        if (!string.Equals(effectiveExeName, "GeminiLiveTranslate.exe", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Contains($"publish/{effectiveExeName}", yaml);
            Assert.Contains("Move-Item -Path \"publish/GeminiLiveTranslate.App.exe\" -Destination \"publish/GeminiLiveTranslate.exe\" -Force", yaml);
        }
    }

    [Fact]
    public void CrossCheck_03_SolutionFileIncludesCoreAppAndTestsProjects()
    {
        string slnPath = Path.Combine(SolutionRoot, "GeminiLiveTranslate.sln");
        Assert.True(File.Exists(slnPath));
        string slnContent = File.ReadAllText(slnPath);

        Assert.Contains("GeminiLiveTranslate.Core.csproj", slnContent);
        Assert.Contains("GeminiLiveTranslate.App.csproj", slnContent);
        Assert.Contains("GeminiLiveTranslate.Tests.csproj", slnContent);
    }

    [Fact]
    public void CrossCheck_04_UploadArtifactAndReleaseSteps_ReferenceIdenticalOutputFiles()
    {
        string yaml = LoadRawWorkflow();

        // Both Upload Build Artifact and Publish GitHub Release must package the exact same two files
        int exeOccurrences = Regex.Matches(yaml, @"publish/GeminiLiveTranslate\.exe").Count;
        int zipOccurrences = Regex.Matches(yaml, @"GeminiLiveTranslate-Windows-x64\.zip").Count;

        // Verify step (3+1) + Upload step (1+1) + Release step (1+1)
        Assert.True(exeOccurrences >= 5, $"Expected publish/GeminiLiveTranslate.exe in verify, upload, and release steps (found {exeOccurrences})");
        Assert.True(zipOccurrences >= 3, $"Expected GeminiLiveTranslate-Windows-x64.zip in verify, upload, and release steps (found {zipOccurrences})");
    }

    #endregion
}
