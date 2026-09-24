using System;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Quark.Gen.Validation.BuildTasks.Abstract;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Quark.Gen.Validation.BuildTasks;

namespace Soenneker.Quark.Gen.Validation.Tests;

public sealed class ValidationRunnerTests
{
    [Test]
    public async Task Manifests_generate_on_first_run_preserve_unchanged_output_and_remove_stale_validators()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var fileUtil = provider.GetRequiredService<IFileUtil>();
        var directoryUtil = provider.GetRequiredService<IDirectoryUtil>();
        var directory = Path.Combine(Path.GetTempPath(), "quark validation " + Guid.NewGuid().ToString("N"));
        await directoryUtil.Create(directory);
        try
        {
            var source = Path.Combine(directory, "Model.cs");
            var sources = Path.Combine(directory, "sources.txt");
            var references = Path.Combine(directory, "references.txt");
            var defines = Path.Combine(directory, "defines.txt");
            var output = Path.Combine(directory, "Validation.g.cs");
            await fileUtil.Write(source, "#if ENABLE_VALIDATION\npublic partial class Model { [System.ComponentModel.DataAnnotations.Required] public string Name { get; set; } }\n#endif");
            await fileUtil.WriteAllLines(sources, [source, output]);
            await fileUtil.WriteAllLines(references, ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator));
            await fileUtil.WriteAllLines(defines, ["ENABLE_VALIDATION"]);
            var args = new[] { "--sources", sources, "--references", references, "--defines", defines, "--output", output };
            var runner = provider.GetRequiredService<IValidationWriteRunner>();
            if (await runner.Run(args, CancellationToken.None) != 0 || !(await fileUtil.Read(output)).Contains("RequiredAttribute")) throw new Exception("First generation failed.");
            var timestamp = File.GetLastWriteTimeUtc(output);
            if (await runner.Run(args, CancellationToken.None) != 0 || File.GetLastWriteTimeUtc(output) != timestamp) throw new Exception("Unchanged output was rewritten.");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await runner.Run(args, cancellation.Token); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
            await fileUtil.Write(defines, "");
            if (await runner.Run(args, CancellationToken.None) != 0 || (await fileUtil.Read(output)).Contains("RequiredAttribute")) throw new Exception("Stale validator remained.");
        }
        finally { await directoryUtil.Delete(directory); }
    }
}
