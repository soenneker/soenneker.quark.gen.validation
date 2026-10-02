using Soenneker.Extensions.ValueTask;
using Soenneker.Extensions.Task;
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
    public async ValueTask Manifests_generate_on_first_run_preserve_unchanged_output_and_remove_stale_validators()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            var fileUtil = provider.GetRequiredService<IFileUtil>();
            var directoryUtil = provider.GetRequiredService<IDirectoryUtil>();
            string directory = Path.Combine(Path.GetTempPath(), "quark validation " + Guid.NewGuid().ToString("N"));
            await directoryUtil.Create(directory).NoSync();
            try
            {
                string source = Path.Combine(directory, "Model.cs");
                string sources = Path.Combine(directory, "sources.txt");
                string references = Path.Combine(directory, "references.txt");
                string defines = Path.Combine(directory, "defines.txt");
                string output = Path.Combine(directory, "Validation.g.cs");
                await fileUtil.Write(source, "#if ENABLE_VALIDATION\npublic partial class Model { [System.ComponentModel.DataAnnotations.Required] public string Name { get; set; } }\n#endif").NoSync();
                await fileUtil.WriteAllLines(sources, [source, output]).NoSync();
                await fileUtil.WriteAllLines(references, ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)).NoSync();
                await fileUtil.WriteAllLines(defines, ["ENABLE_VALIDATION"]).NoSync();
                string[] args = new[] { "--sources", sources, "--references", references, "--defines", defines, "--output", output };
                var runner = provider.GetRequiredService<IValidationWriteRunner>();
                if (await runner.Run(args, CancellationToken.None).NoSync() != 0 || !(await fileUtil.Read(output).NoSync()).Contains("RequiredAttribute")) throw new Exception("First generation failed.");
                DateTime timestamp = File.GetLastWriteTimeUtc(output);
                if (await runner.Run(args, CancellationToken.None).NoSync() != 0 || File.GetLastWriteTimeUtc(output) != timestamp) throw new Exception("Unchanged output was rewritten.");
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                try { await runner.Run(args, cancellation.Token).NoSync(); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
                await fileUtil.Write(defines, "").NoSync();
                if (await runner.Run(args, CancellationToken.None).NoSync() != 0 || (await fileUtil.Read(output).NoSync()).Contains("RequiredAttribute")) throw new Exception("Stale validator remained.");
            }
            finally { await directoryUtil.Delete(directory).NoSync(); }
        }
        finally
        {
            await provider.DisposeAsync().NoSync();
        }
    }
}
