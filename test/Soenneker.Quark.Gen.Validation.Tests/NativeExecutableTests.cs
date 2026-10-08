using Soenneker.Extensions.ValueTask;
using Soenneker.Extensions.Task;
using System;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Quark.Gen.Validation.BuildTasks;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.Process.Abstract;
using Soenneker.Utils.Dotnet.Abstract;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Soenneker.Quark.Gen.Validation.Tests;

public sealed class NativeExecutableTests
{
    [Test]
    public async Task Native_executable_generates_expected_output_and_preserves_unchanged_files(CancellationToken cancellationToken)
    {
        // Native matrix jobs supply the published executable; ordinary managed test runs do not.
        string? executable = Environment.GetEnvironmentVariable("QUARK_NATIVE_TOOL");
        if (string.IsNullOrEmpty(executable)) { TUnit.Core.Skip.Test("Set QUARK_NATIVE_TOOL to run native integration tests."); return; }
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            var fileUtil = provider.GetRequiredService<IFileUtil>();
            var directoryUtil = provider.GetRequiredService<IDirectoryUtil>();
            string directory = Path.Combine(Path.GetTempPath(), "quark native " + Guid.NewGuid().ToString("N"));
            await directoryUtil.Create(directory, cancellationToken: cancellationToken).NoSync();
            try
            {
                string source = Path.Combine(directory, "Model.cs");
                await fileUtil.Write(source, "namespace Soenneker.Quark { public interface IGeneratedQuarkValidation { void ValidateField(string memberName, System.Collections.Generic.ICollection<System.ComponentModel.DataAnnotations.ValidationResult> results); } [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class GenerateQuarkValidationAttribute : System.Attribute {} } [Soenneker.Quark.GenerateQuarkValidation] public partial class Model { [System.ComponentModel.DataAnnotations.Required] public string Name { get; set; } }", cancellationToken: cancellationToken).NoSync();
                string sources = Path.Combine(directory, "sources.txt");
                string references = Path.Combine(directory, "references.txt");
                await fileUtil.WriteAllLines(sources, [source], cancellationToken: cancellationToken).NoSync();
                await fileUtil.WriteAllLines(references, ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator), cancellationToken: cancellationToken).NoSync();
                string output = Path.Combine(directory, "Validation.g.cs");
                string[] arguments = ["--sources", sources, "--references", references, "--output", output];
                string[] expectedValues = ["RequiredAttribute", "IGeneratedQuarkValidation"];
                async Task Run()
                {
                    await provider.GetRequiredService<IProcessUtil>().Start(executable,
                        arguments: string.Join(" ", arguments.Select(CommandLineArguments.Quote)), cancellationToken: cancellationToken).NoSync();
                }
                await Run().NoSync();
                string content = await fileUtil.Read(output, cancellationToken: cancellationToken).NoSync();
                foreach (string expected in expectedValues)
                    if (!content.Contains(expected, StringComparison.Ordinal)) throw new Exception("Native output is missing " + expected);
                DateTime timestamp = File.GetLastWriteTimeUtc(output);
                await Run().NoSync();
                if (File.GetLastWriteTimeUtc(output) != timestamp) throw new Exception("Unchanged native output was rewritten.");
            }
            finally { await directoryUtil.Delete(directory).NoSync(); }
        }
        finally
        {
            await provider.DisposeAsync().NoSync();
        }
    }
    [Test]
    public async Task Native_executable_discovers_nested_models_from_the_selected_SDK_Razor_generator(CancellationToken cancellationToken)
    {
        string? executable = Environment.GetEnvironmentVariable("QUARK_NATIVE_TOOL");
        if (string.IsNullOrEmpty(executable)) { TUnit.Core.Skip.Test("Set QUARK_NATIVE_TOOL to run native integration tests."); return; }
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            var fileUtil = provider.GetRequiredService<IFileUtil>();
            var directoryUtil = provider.GetRequiredService<IDirectoryUtil>();
            string? targets = null;
            for (DirectoryInfo? parent = new FileInfo(executable).Directory; parent is not null; parent = parent.Parent)
            {
                string candidate = Path.Combine(parent.FullName, "src", "Soenneker.Quark.Gen.Validation", "Soenneker.Quark.Gen.Validation.targets");
                if (await fileUtil.Exists(candidate, cancellationToken: cancellationToken).NoSync()) { targets = candidate; break; }
            }
            if (targets is null) throw new Exception("Validation targets could not be found.");
            string directory = Path.Combine(Path.GetTempPath(), "quark native razor " + Guid.NewGuid().ToString("N"));
            await directoryUtil.Create(directory, cancellationToken: cancellationToken).NoSync();
            try
            {
                string project = Path.Combine(directory, "Fixture.csproj");
                await fileUtil.Write(project, "<Project Sdk=\"Microsoft.NET.Sdk.Razor\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>NativeRazorFixture</RootNamespace><ValidationBuildTasksExecutablePath>" + System.Security.SecurityElement.Escape(executable) + "</ValidationBuildTasksExecutablePath></PropertyGroup><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup><Import Project=\"" + System.Security.SecurityElement.Escape(targets) + "\" /></Project>", cancellationToken: cancellationToken).NoSync();
                await fileUtil.Write(Path.Combine(directory, "Contract.cs"), "namespace Soenneker.Quark { public interface IGeneratedQuarkValidation { void ValidateField(string memberName, System.Collections.Generic.ICollection<System.ComponentModel.DataAnnotations.ValidationResult> results); } [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class GenerateQuarkValidationAttribute : System.Attribute {} }", cancellationToken: cancellationToken).NoSync();
                await fileUtil.Write(Path.Combine(directory, "Component.razor"), "<p>Native Razor</p>\n@code { [Soenneker.Quark.GenerateQuarkValidation] public partial class EmbeddedModel { [System.ComponentModel.DataAnnotations.Required] public string Name { get; set; } = \"\"; } }", cancellationToken: cancellationToken).NoSync();
                if (!await provider.GetRequiredService<IDotnetUtil>().Build(project, configuration: "Debug", verbosity: "quiet", cancellationToken: cancellationToken).NoSync())
                    throw new Exception("Native Razor fixture failed.");
                string output = (await directoryUtil.GetFilesByExtension(Path.Combine(directory, "obj"), ".cs", recursive: true, cancellationToken: cancellationToken).NoSync())
                    .Single(path => Path.GetFileName(path) == "Validation.g.cs");
                string content = await fileUtil.Read(output, cancellationToken: cancellationToken).NoSync();
                if (!content.Contains("EmbeddedModel", StringComparison.Ordinal) || !content.Contains("RequiredAttribute", StringComparison.Ordinal))
                    throw new Exception("Native validation did not discover the nested Razor model.");
                string stamp = Path.Combine(Path.GetDirectoryName(output)!, "validation.stamp");
                DateTime stampTime = File.GetLastWriteTimeUtc(stamp);
                DateTime outputTime = File.GetLastWriteTimeUtc(output);
                if (!await provider.GetRequiredService<IDotnetUtil>().Build(project, configuration: "Debug", verbosity: "quiet", cancellationToken: cancellationToken).NoSync())
                    throw new Exception("Incremental Razor build failed.");
                if (File.GetLastWriteTimeUtc(stamp) != stampTime || File.GetLastWriteTimeUtc(output) != outputTime)
                    throw new Exception("An unchanged build reran validation generation.");
                await fileUtil.Write(Path.Combine(directory, "Component.razor"), "<p>Native Razor</p>\n@code { [Soenneker.Quark.GenerateQuarkValidation] public partial class EmbeddedModel { [System.ComponentModel.DataAnnotations.Required] public string Changed { get; set; } = \"\"; } }", cancellationToken: cancellationToken).NoSync();
                if (!await provider.GetRequiredService<IDotnetUtil>().Build(project, configuration: "Debug", verbosity: "quiet", cancellationToken: cancellationToken).NoSync())
                    throw new Exception("Changed Razor build failed.");
                content = await fileUtil.Read(output, cancellationToken: cancellationToken).NoSync();
                if (!content.Contains("case \"Changed\"", StringComparison.Ordinal) || content.Contains("case \"Name\"", StringComparison.Ordinal))
                    throw new Exception("Razor changes did not invalidate generated validation.");
            }
            finally { await directoryUtil.Delete(directory).NoSync(); }
        }
        finally
        {
            await provider.DisposeAsync().NoSync();
        }
    }
}
