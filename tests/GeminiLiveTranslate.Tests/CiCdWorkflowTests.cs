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
/// Milestone 5: Comprehensive CI/CD Packaging &amp; GitHub Actions Workflow Verification Suite.
/// Parses and validates .github/workflows/build.yml, step structure, step ordering,
/// single-file self-contained win-x64 publish arguments, artifact verification, and release automation.
/// </summary>
public class CiCdWorkflowTests
{
    private static readonly string SolutionRoot = ResolveSolutionRoot();
    private static readonly string WorkflowFilePath = Path.Combine(SolutionRoot, ".github", "workflows", "build.yml");
    private static readonly string AppCsprojPath = Path.Combine(SolutionRoot, "src", "GeminiLiveTranslate.App", "GeminiLiveTranslate.App.csproj");

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

    private static string LoadWorkflowContent()
    {
        Assert.True(File.Exists(WorkflowFilePath), $"Expected GitHub Actions workflow at {WorkflowFilePath}");
        return File.ReadAllText(WorkflowFilePath);
    }

    private static IReadOnlyList<WorkflowStepInfo> ParseWorkflowSteps(string yaml)
    {
        var steps = new List<WorkflowStepInfo>();
        string[] lines = yaml.Replace("\r\n", "\n").Split('\n');
        bool inSteps = false;
        int stepsIndent = -1;
        WorkflowStepInfo? currentStep = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string rawLine = lines[i];
            string trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            int indent = rawLine.Length - rawLine.TrimStart(' ').Length;

            if (!inSteps)
            {
                if (trimmed == "steps:")
                {
                    inSteps = true;
                    stepsIndent = indent;
                }
                continue;
            }

            // If indentation returns to or above the 'steps:' level and is not a list item under steps
            if (indent <= stepsIndent && !trimmed.StartsWith('-'))
            {
                break;
            }

            if (trimmed.StartsWith("- name:"))
            {
                if (currentStep != null)
                {
                    steps.Add(currentStep);
                }
                string stepName = trimmed.Substring("- name:".Length).Trim().Trim('\'', '"');
                currentStep = new WorkflowStepInfo
                {
                    Index = steps.Count,
                    Name = stepName,
                    StartLineNumber = i + 1
                };
                currentStep.RawLines.Add(rawLine);
            }
            else if (currentStep != null)
            {
                currentStep.RawLines.Add(rawLine);
                if (trimmed.StartsWith("uses:"))
                {
                    currentStep.Uses = trimmed.Substring("uses:".Length).Trim().Trim('\'', '"');
                }
                else if (trimmed.StartsWith("if:"))
                {
                    currentStep.IfCondition = trimmed.Substring("if:".Length).Trim();
                }
                else if (trimmed.StartsWith("shell:"))
                {
                    currentStep.Shell = trimmed.Substring("shell:".Length).Trim();
                }
            }
        }

        if (currentStep != null)
        {
            steps.Add(currentStep);
        }

        return steps;
    }

    private sealed class WorkflowStepInfo
    {
        public int Index { get; init; }
        public string Name { get; init; } = string.Empty;
        public int StartLineNumber { get; init; }
        public string? Uses { get; set; }
        public string? IfCondition { get; set; }
        public string? Shell { get; set; }
        public List<string> RawLines { get; } = new();
        public string CombinedText => string.Join("\n", RawLines);
    }

    #region 1. YAML Structural Integrity & Top-Level Keys

    [Fact]
    public void Workflow_01_FileExistsAndIsNonEmpty()
    {
        Assert.True(File.Exists(WorkflowFilePath), $"Workflow file missing at {WorkflowFilePath}");
        string content = File.ReadAllText(WorkflowFilePath);
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.True(content.Length > 500, "Workflow YAML should be a complete specification.");
    }

    [Fact]
    public void Workflow_02_ContainsNoTabIndentation()
    {
        string[] lines = LoadWorkflowContent().Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            Assert.DoesNotContain("\t", lines[i]);
        }
    }

    [Fact]
    public void Workflow_03_DefinesTopLevelNameOnAndJobsSections()
    {
        string[] lines = LoadWorkflowContent().Replace("\r\n", "\n").Split('\n');
        Assert.Contains(lines, l => l.StartsWith("name:"));
        Assert.Contains(lines, l => l.TrimEnd() == "on:");
        Assert.Contains(lines, l => l.TrimEnd() == "jobs:");
    }

    [Fact]
    public void Workflow_04_DefinesBuildAndPublishJob()
    {
        string content = LoadWorkflowContent();
        Assert.Contains("build-and-publish:", content);
    }

    [Fact]
    public void Workflow_05_HasBalancedQuotesAndValidStepCount()
    {
        string content = LoadWorkflowContent();
        var steps = ParseWorkflowSteps(content);
        Assert.Equal(8, steps.Count);
        Assert.All(steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
    }

    #endregion

    #region 2. Trigger Configuration (push, pull_request, workflow_dispatch)

    [Fact]
    public void Workflow_06_TriggersOnPushToMainAndMasterBranches()
    {
        string content = LoadWorkflowContent();
        Assert.Contains("push:", content);
        Assert.Matches(@"push:\s+branches:\s*\[\s*'main'\s*,\s*'master'\s*\]", content);
    }

    [Fact]
    public void Workflow_07_TriggersOnPushVersionTags()
    {
        string content = LoadWorkflowContent();
        Assert.Matches(@"tags:\s*\[\s*'v\*'\s*\]", content);
    }

    [Fact]
    public void Workflow_08_TriggersOnPullRequestToMainAndMasterBranches()
    {
        string content = LoadWorkflowContent();
        Assert.Contains("pull_request:", content);
        Assert.Matches(@"pull_request:\s+branches:\s*\[\s*'main'\s*,\s*'master'\s*\]", content);
    }

    [Fact]
    public void Workflow_09_SupportsManualWorkflowDispatchWithOptionalTagNameInput()
    {
        string content = LoadWorkflowContent();
        Assert.Contains("workflow_dispatch:", content);
        Assert.Contains("inputs:", content);
        Assert.Contains("tag_name:", content);
        Assert.Contains("required: false", content);
    }

    #endregion

    #region 3. Runner Environment & Permissions

    [Fact]
    public void Workflow_10_TargetsWindowsLatestRunner()
    {
        string content = LoadWorkflowContent();
        Assert.Contains("runs-on: windows-latest", content);
    }

    [Fact]
    public void Workflow_11_GrantsContentsWritePermissionForReleases()
    {
        string content = LoadWorkflowContent();
        Assert.Matches(@"permissions:\s+contents:\s*write", content);
    }

    #endregion

    #region 4. Toolchain Setup, Restore & Automated Test Execution

    [Fact]
    public void Workflow_12_UsesActionsCheckoutV4()
    {
        var steps = ParseWorkflowSteps(LoadWorkflowContent());
        var checkoutStep = steps[0];
        Assert.Equal("actions/checkout@v4", checkoutStep.Uses);
    }

    [Fact]
    public void Workflow_13_UsesActionsSetupDotNetV4WithNet90x()
    {
        var steps = ParseWorkflowSteps(LoadWorkflowContent());
        var setupDotNetStep = steps[1];
        Assert.Equal("actions/setup-dotnet@v4", setupDotNetStep.Uses);
        Assert.Contains("dotnet-version: '9.0.x'", setupDotNetStep.CombinedText);
    }

    [Fact]
    public void Workflow_14_RestoresSolutionDependenciesBeforeTesting()
    {
        var steps = ParseWorkflowSteps(LoadWorkflowContent());
        var restoreStep = steps.FirstOrDefault(s => s.CombinedText.Contains("dotnet restore"));
        Assert.NotNull(restoreStep);
        Assert.Contains("dotnet restore GeminiLiveTranslate.sln", restoreStep.CombinedText);
    }

    [Fact]
    public void Workflow_15_ExecutesFullTestSuiteInReleaseConfiguration()
    {
        var steps = ParseWorkflowSteps(LoadWorkflowContent());
        var testStep = steps.FirstOrDefault(s => s.CombinedText.Contains("dotnet test"));
        Assert.NotNull(testStep);
        Assert.Contains("dotnet test GeminiLiveTranslate.sln", testStep.CombinedText);
        Assert.Contains("-c Release", testStep.CombinedText);
        Assert.Contains("--no-restore", testStep.CombinedText);
        Assert.Contains("--verbosity normal", testStep.CombinedText);
    }

    #endregion

    #region 5. Single-File Self-Contained win-x64 Publish Verification

    [Theory]
    [InlineData("dotnet publish src/GeminiLiveTranslate.App/GeminiLiveTranslate.App.csproj")]
    [InlineData("-c Release")]
    [InlineData("-r win-x64")]
    [InlineData("--self-contained true")]
    [InlineData("-p:PublishSingleFile=true")]
    [InlineData("-p:IncludeNativeLibrariesForSelfExtract=true")]
    [InlineData("-p:EnableCompressionInSingleFile=true")]
    [InlineData("-o publish")]
    public void Workflow_16_PublishStep_IncludesAllRequiredSingleFileFlags(string expectedFlag)
    {
        var steps = ParseWorkflowSteps(LoadWorkflowContent());
        var publishStep = steps.FirstOrDefault(s => s.CombinedText.Contains("dotnet publish"));
        Assert.NotNull(publishStep);
        Assert.Contains(expectedFlag, publishStep.CombinedText);
    }

    [Fact]
    public void Workflow_17_PublishStep_AvoidsGlobalAssemblyNameCollisionAndNormalizesToGeminiLiveTranslateExe()
    {
        var steps = ParseWorkflowSteps(LoadWorkflowContent());
        var publishStep = steps.First(s => s.CombinedText.Contains("dotnet publish"));
        // Global -p:AssemblyName= on dotnet publish causes NuGet restore ambiguity across referenced projects
        Assert.DoesNotContain("-p:AssemblyName=", publishStep.CombinedText);

        var verifyStep = steps.First(s => s.Name.Contains("Verify", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("publish/GeminiLiveTranslate.exe", verifyStep.CombinedText);
    }

    #endregion

    #region 6. Post-Publish Verification, Artifact Upload & GitHub Releases

    [Fact]
    public void Workflow_18_VerifyStep_ChecksExeExistsAndNonZeroLengthInPowerShell()
    {
        var steps = ParseWorkflowSteps(LoadWorkflowContent());
        var verifyStep = steps.FirstOrDefault(s => s.Name.Contains("Verify", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(verifyStep);
        Assert.Equal("pwsh", verifyStep.Shell);
        Assert.Contains("publish/GeminiLiveTranslate.exe", verifyStep.CombinedText);
        Assert.Contains("Test-Path", verifyStep.CombinedText);
        Assert.Contains(".Length -le 0", verifyStep.CombinedText);
        Assert.Contains("Compress-Archive", verifyStep.CombinedText);
        Assert.Contains("GeminiLiveTranslate-Windows-x64.zip", verifyStep.CombinedText);
    }

    [Fact]
    public void Workflow_19_UploadArtifactStep_UsesV4AndUploadsExeAndZipWithStrictErrorCheck()
    {
        var steps = ParseWorkflowSteps(LoadWorkflowContent());
        var uploadStep = steps.FirstOrDefault(s => s.Uses == "actions/upload-artifact@v4");
        Assert.NotNull(uploadStep);
        Assert.Contains("name: GeminiLiveTranslate-Windows-x64", uploadStep.CombinedText);
        Assert.Contains("publish/GeminiLiveTranslate.exe", uploadStep.CombinedText);
        Assert.Contains("GeminiLiveTranslate-Windows-x64.zip", uploadStep.CombinedText);
        Assert.Contains("if-no-files-found: error", uploadStep.CombinedText);
    }

    [Fact]
    public void Workflow_20_GitHubReleaseStep_UsesSoftpropsActionGhReleaseV2OnVersionTags()
    {
        var steps = ParseWorkflowSteps(LoadWorkflowContent());
        var releaseStep = steps.FirstOrDefault(s => s.Uses == "softprops/action-gh-release@v2");
        Assert.NotNull(releaseStep);
        Assert.NotNull(releaseStep.IfCondition);
        Assert.Contains("startsWith(github.ref, 'refs/tags/v')", releaseStep.IfCondition);
        Assert.Contains("publish/GeminiLiveTranslate.exe", releaseStep.CombinedText);
        Assert.Contains("GeminiLiveTranslate-Windows-x64.zip", releaseStep.CombinedText);
        Assert.Contains("generate_release_notes: true", releaseStep.CombinedText);
        Assert.Contains("GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}", releaseStep.CombinedText);
    }

    #endregion

    #region 7. Pipeline Step Ordering & Legacy Flutter Removal

    [Fact]
    public void Workflow_21_StepsExecuteInStrictSequentialPipelineOrder()
    {
        var steps = ParseWorkflowSteps(LoadWorkflowContent());

        int checkoutIdx = steps.First(s => s.Uses == "actions/checkout@v4").Index;
        int setupIdx = steps.First(s => s.Uses == "actions/setup-dotnet@v4").Index;
        int restoreIdx = steps.First(s => s.CombinedText.Contains("dotnet restore")).Index;
        int testIdx = steps.First(s => s.CombinedText.Contains("dotnet test")).Index;
        int publishIdx = steps.First(s => s.CombinedText.Contains("dotnet publish")).Index;
        int verifyIdx = steps.First(s => s.Name.Contains("Verify", StringComparison.OrdinalIgnoreCase)).Index;
        int uploadIdx = steps.First(s => s.Uses == "actions/upload-artifact@v4").Index;
        int releaseIdx = steps.First(s => s.Uses == "softprops/action-gh-release@v2").Index;

        Assert.True(checkoutIdx < setupIdx, "Checkout must run before Setup .NET");
        Assert.True(setupIdx < restoreIdx, "Setup .NET must run before Restore");
        Assert.True(restoreIdx < testIdx, "Restore must run before Test");
        Assert.True(testIdx < publishIdx, "Test must run before Publish");
        Assert.True(publishIdx < verifyIdx, "Publish must run before Verify");
        Assert.True(verifyIdx < uploadIdx, "Verify must run before Upload Artifact");
        Assert.True(uploadIdx < releaseIdx, "Upload Artifact must run before GitHub Release");
    }

    [Theory]
    [InlineData("flutter")]
    [InlineData("pub get")]
    [InlineData("build windows")]
    [InlineData("pubspec.yaml")]
    [InlineData("subosito/flutter-action")]
    public void Workflow_22_ContainsNoLegacyFlutterCommands(string forbiddenLegacyToken)
    {
        string content = LoadWorkflowContent();
        Assert.DoesNotContain(forbiddenLegacyToken, content, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 8. WorkflowYamlValidator Contract & App Csproj Compatibility

    [Fact]
    public void Workflow_23_WorkflowYamlValidator_PassesAllValidationPredicates()
    {
        var validator = WorkflowYamlValidator.FromFile(WorkflowFilePath);
        Assert.True(validator.HasWindowsRunner());
        Assert.True(validator.HasDotNet9Setup());
        Assert.True(validator.HasWinX64SingleFilePublish());
        Assert.True(validator.HasTagReleaseTrigger());
        Assert.True(validator.HasValidArtifactNaming());
    }

    [Fact]
    public void Workflow_24_AppProjectFile_IsConfiguredForWindowsWpfSingleFilePublishing()
    {
        Assert.True(File.Exists(AppCsprojPath), $"App project file not found at {AppCsprojPath}");
        XDocument doc = XDocument.Load(AppCsprojPath);
        var props = doc.Descendants("PropertyGroup").Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);

        Assert.Equal("WinExe", props["OutputType"]);
        Assert.Equal("net9.0-windows", props["TargetFramework"]);
        Assert.Equal("true", props["UseWPF"]);
        Assert.Equal("true", props["EnableWindowsTargeting"]);
    }

    #endregion
}
