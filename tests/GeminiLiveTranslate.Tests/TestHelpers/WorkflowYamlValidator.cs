using System;
using System.Collections.Generic;
using System.IO;

namespace GeminiLiveTranslate.Tests.TestHelpers;

/// <summary>
/// Parser and validator for GitHub Actions CI/CD workflow YAML files.
/// Validates build, self-contained publish, single-file win-x64 executable packaging, and release steps.
/// </summary>
public class WorkflowYamlValidator
{
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines;

    public WorkflowYamlValidator(string yamlContent)
    {
        if (yamlContent != null)
        {
            using var reader = new StringReader(yamlContent);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                _lines.Add(line);
            }
        }
    }

    public static WorkflowYamlValidator FromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Workflow file not found: {filePath}");
        }
        string content = File.ReadAllText(filePath);
        return new WorkflowYamlValidator(content);
    }

    /// <summary>
    /// Checks if workflow contains a specific text or command fragment.
    /// </summary>
    public bool ContainsText(string fragment, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        foreach (var line in _lines)
        {
            if (line.IndexOf(fragment, comparison) >= 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Validates whether the workflow uses a Windows runner (windows-latest or windows-2022).
    /// </summary>
    public bool HasWindowsRunner()
    {
        return ContainsText("runs-on: windows-latest") || ContainsText("runs-on: windows-2022");
    }

    /// <summary>
    /// Validates whether the workflow specifies .NET 9 setup.
    /// </summary>
    public bool HasDotNet9Setup()
    {
        bool hasSetupAction = ContainsText("actions/setup-dotnet");
        bool hasVersion9 = ContainsText("dotnet-version: '9.") || ContainsText("dotnet-version: 9.");
        return hasSetupAction && hasVersion9;
    }

    /// <summary>
    /// Validates whether the workflow contains the required standalone win-x64 publish command.
    /// </summary>
    public bool HasWinX64SingleFilePublish()
    {
        return ContainsText("dotnet publish") &&
               ContainsText("-r win-x64") &&
               ContainsText("--self-contained") &&
               ContainsText("PublishSingleFile=true");
    }

    /// <summary>
    /// Validates release trigger tags (v*).
    /// </summary>
    public bool HasTagReleaseTrigger()
    {
        return ContainsText("v*") || ContainsText("refs/tags/v");
    }

    /// <summary>
    /// Validates that single executable artifact or zip naming matches project convention.
    /// </summary>
    public bool HasValidArtifactNaming()
    {
        return ContainsText("GeminiLiveTranslate") &&
               (ContainsText(".exe") || ContainsText(".zip") || ContainsText("GeminiLiveTranslate-Windows-x64"));
    }
}
