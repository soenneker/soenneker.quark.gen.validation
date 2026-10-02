using Soenneker.Extensions.ValueTask;
using Soenneker.Extensions.Task;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.File.Registrars;
using Soenneker.Quark.Gen.Validation.BuildTasks;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddFileUtilAsSingleton();
            ServiceProvider provider = services.BuildServiceProvider();
            try
            {
                var fileUtil = provider.GetRequiredService<IFileUtil>();
                string directory = args[0];

                async ValueTask<string[]> Manifest(string name) =>
                    (await fileUtil.ReadAsLines(Path.Combine(directory, name + ".txt")).NoSync()).ToArray();

                var options = new CSharpParseOptions(LanguageVersion.Preview,
                    preprocessorSymbols: await Manifest("defines").NoSync());
                SyntaxTree[] trees = await CompilationInput.LoadSources(await Manifest("sources").NoSync(), options,
                    fileUtil, CancellationToken.None).NoSync();
                IEnumerable<PortableExecutableReference> references =
                    (await Manifest("references").NoSync()).Select(path => MetadataReference.CreateFromFile(path));
                var compilation = CSharpCompilation.Create("QuarkValidationInput", trees, references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                ImmutableArray<SyntaxTree> result = await RazorModelDiscovery.Discover(compilation,
                    await Manifest("razor").NoSync(), await Manifest("additional").NoSync(),
                    await Manifest("configs").NoSync(), options, fileUtil, CancellationToken.None).NoSync();
                var index = 0;

                foreach (SyntaxTree tree in result)
                {
                    var writer =
                        new StreamWriter(fileUtil.OpenWrite(Path.Combine(directory, "generated-" + index++ + ".cs"),
                            log: false));
                    try
                    {
                        (await tree.GetTextAsync()).Write(writer);
                    }
                    finally
                    {
                        await writer.DisposeAsync().NoSync();
                    }
                }

                return 0;
            }
            finally
            {
                await provider.DisposeAsync().NoSync();
            }
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync("QV002: Razor model discovery failed: " + exception).NoSync();
            return 1;
        }
    }
}