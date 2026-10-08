using Soenneker.Extensions.ValueTask;
using Soenneker.Extensions.Task;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Quark.Gen.Validation.BuildTasks;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Quark.Gen.Validation.Tests;

public sealed class ValidationPipelineTests
{
    [Test]
    public async Task Parallel_source_loading_preserves_order_options_and_cancellation(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection(); services.AddLogging(); Startup.ConfigureServices(services);
        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            var fileUtil = provider.GetRequiredService<IFileUtil>();
            var directoryUtil = provider.GetRequiredService<IDirectoryUtil>();
            string directory = await directoryUtil.CreateTempDirectory(cancellationToken: cancellationToken).NoSync();
            try
            {
                string[] paths = Enumerable.Range(0, 24).Select(i => Path.Combine(directory, i + ".cs")).ToArray();
                for (var i = 0; i < paths.Length; i++)
                    await fileUtil.Write(paths[i], "#if INCLUDED\npublic class Model" + i + " {}\n#endif", cancellationToken: cancellationToken).NoSync();
                var options = new CSharpParseOptions(preprocessorSymbols: ["INCLUDED"]);
                SyntaxTree[] trees = await CompilationInput.LoadSources(paths, options, fileUtil, cancellationToken).NoSync();
                for (var i = 0; i < paths.Length; i++)
                    if (trees[i].FilePath != paths[i] || trees[i].Options != options || !trees[i].GetRoot(cancellationToken: cancellationToken).DescendantTokens().Any(t => t.ValueText == "Model" + i))
                        throw new Exception("Parallel loading changed source ordering or parse options");
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                try { await CompilationInput.LoadSources(paths, options, fileUtil, cancellation.Token).NoSync(); }
                catch (OperationCanceledException) { return; }
                throw new Exception("Source loading ignored cancellation");
            }
            finally { await directoryUtil.Delete(directory).NoSync(); }
        }
        finally
        {
            await provider.DisposeAsync().NoSync();
        }
    }

    [Test]
    public async Task Streaming_output_comparison_handles_boundaries_encoding_and_length(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection(); services.AddLogging(); Startup.ConfigureServices(services);
        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            var fileUtil = provider.GetRequiredService<IFileUtil>();
            var directoryUtil = provider.GetRequiredService<IDirectoryUtil>();
            string directory = await directoryUtil.CreateTempDirectory(cancellationToken: cancellationToken).NoSync();
            try
            {
                string path = Path.Combine(directory, "output.cs");
                if (await GeneratedFile.Matches(path, "", fileUtil, cancellationToken).NoSync()) throw new Exception("Missing file matched");
                foreach (string text in new[] { "", "hello", new string('x', 4095) + "😀é" + new string('y', 10000) })
                {
                    await fileUtil.Write(path, text, cancellationToken: cancellationToken).NoSync();
                    if (!await GeneratedFile.Matches(path, text, fileUtil, cancellationToken).NoSync() ||
                        await GeneratedFile.Matches(path, text + "x", fileUtil, cancellationToken).NoSync() ||
                        await GeneratedFile.Matches(path, "x" + text, fileUtil, cancellationToken).NoSync()) throw new Exception("Content comparison failed");
                    if (text.Length > 0 && await GeneratedFile.Matches(path, text[..^1], fileUtil, cancellationToken).NoSync()) throw new Exception("Extra content matched");
                }
                byte[] encoded = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("encoded 😀")).ToArray();
                using var stream = new MemoryStream(encoded);
                await fileUtil.Write(path, stream, cancellationToken: cancellationToken).NoSync();
                if (!await GeneratedFile.Matches(path, "encoded 😀", fileUtil, cancellationToken).NoSync()) throw new Exception("BOM detection changed");
            }
            finally { await directoryUtil.Delete(directory).NoSync(); }
        }
        finally
        {
            await provider.DisposeAsync().NoSync();
        }
    }
}
