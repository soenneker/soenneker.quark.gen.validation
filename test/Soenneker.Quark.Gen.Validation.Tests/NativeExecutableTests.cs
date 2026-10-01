using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Soenneker.Quark.Gen.Validation.Tests;

public sealed class NativeExecutableTests
{
    [Test]
    public async Task Native_executable_generates_expected_output_and_preserves_unchanged_files()
    {
        // Native matrix jobs supply the published executable; ordinary managed test runs do not.
        string? executable = Environment.GetEnvironmentVariable("QUARK_NATIVE_TOOL");
        if (string.IsNullOrEmpty(executable)) { TUnit.Core.Skip.Test("Set QUARK_NATIVE_TOOL to run native integration tests."); return; }
        string directory = Path.Combine(Path.GetTempPath(), "quark native " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "Model.cs");
            await File.WriteAllTextAsync(source, "namespace Soenneker.Quark { public interface IGeneratedQuarkValidation { void ValidateField(string memberName, System.Collections.Generic.ICollection<System.ComponentModel.DataAnnotations.ValidationResult> results); } [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class GenerateQuarkValidationAttribute : System.Attribute {} } [Soenneker.Quark.GenerateQuarkValidation] public partial class Model { [System.ComponentModel.DataAnnotations.Required] public string Name { get; set; } }");
            string sources = Path.Combine(directory, "sources.txt");
            string references = Path.Combine(directory, "references.txt");
            await File.WriteAllLinesAsync(sources, [source]);
            await File.WriteAllLinesAsync(references, ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator));
            string output = Path.Combine(directory, "Validation.g.cs");
            string[] arguments = ["--sources", sources, "--references", references, "--output", output];
            string[] expectedValues = ["RequiredAttribute", "IGeneratedQuarkValidation"];
            async Task Run()
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false };
                foreach (string argument in arguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new Exception("Native tool could not start.");
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new Exception("Native tool failed: " + process.ExitCode);
            }
            await Run();
            string content = await File.ReadAllTextAsync(output);
            foreach (string expected in expectedValues)
                if (!content.Contains(expected, StringComparison.Ordinal)) throw new Exception("Native output is missing " + expected);
            DateTime timestamp = File.GetLastWriteTimeUtc(output);
            await Run();
            if (File.GetLastWriteTimeUtc(output) != timestamp) throw new Exception("Unchanged native output was rewritten.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    [Test]
    public async Task Native_executable_discovers_nested_models_from_the_selected_SDK_Razor_generator()
    {
        string? executable = Environment.GetEnvironmentVariable("QUARK_NATIVE_TOOL");
        if (string.IsNullOrEmpty(executable)) { TUnit.Core.Skip.Test("Set QUARK_NATIVE_TOOL to run native integration tests."); return; }
        string? targets = null;
        for (DirectoryInfo? parent = new FileInfo(executable).Directory; parent is not null; parent = parent.Parent)
        {
            string candidate = Path.Combine(parent.FullName, "src", "Soenneker.Quark.Gen.Validation", "Soenneker.Quark.Gen.Validation.targets");
            if (File.Exists(candidate)) { targets = candidate; break; }
        }
        if (targets is null) throw new Exception("Validation targets could not be found.");
        string directory = Path.Combine(Path.GetTempPath(), "quark native razor " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string project = Path.Combine(directory, "Fixture.csproj");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk.Razor\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>NativeRazorFixture</RootNamespace><ValidationBuildTasksExecutablePath>" + System.Security.SecurityElement.Escape(executable) + "</ValidationBuildTasksExecutablePath></PropertyGroup><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup><Import Project=\"" + System.Security.SecurityElement.Escape(targets) + "\" /></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory, "Contract.cs"), "namespace Soenneker.Quark { public interface IGeneratedQuarkValidation { void ValidateField(string memberName, System.Collections.Generic.ICollection<System.ComponentModel.DataAnnotations.ValidationResult> results); } [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class GenerateQuarkValidationAttribute : System.Attribute {} }");
            await File.WriteAllTextAsync(Path.Combine(directory, "Component.razor"), "<p>Native Razor</p>\n@code { [Soenneker.Quark.GenerateQuarkValidation] public partial class EmbeddedModel { [System.ComponentModel.DataAnnotations.Required] public string Name { get; set; } = \"\"; } }");
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            start.ArgumentList.Add("build");
            start.ArgumentList.Add(project);
            start.ArgumentList.Add("--verbosity");
            start.ArgumentList.Add("quiet");
            using var process = Process.Start(start) ?? throw new Exception("Razor fixture build could not start.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new Exception("Native Razor fixture failed: " + process.ExitCode);
            string output = Directory.GetFiles(Path.Combine(directory, "obj"), "Validation.g.cs", SearchOption.AllDirectories).Single();
            string content = await File.ReadAllTextAsync(output);
            if (!content.Contains("EmbeddedModel", StringComparison.Ordinal) || !content.Contains("RequiredAttribute", StringComparison.Ordinal))
                throw new Exception("Native validation did not discover the nested Razor model.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
