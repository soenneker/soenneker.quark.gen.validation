using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

internal static class DiscoveryRunner
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task Run(string directory)
    {
        async Task<string[]> Manifest(string name) =>
            (await File.ReadAllLinesAsync(Path.Combine(directory, name + ".txt")))
            .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

        var options = new CSharpParseOptions(LanguageVersion.Preview,
            preprocessorSymbols: await Manifest("defines"));
        string[] sources = await Manifest("sources");
        var trees = new SyntaxTree[sources.Length];
        await Parallel.ForAsync(0, sources.Length,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) },
            async (i, token) => trees[i] = CSharpSyntaxTree.ParseText(
                await File.ReadAllTextAsync(sources[i], token), options, sources[i], cancellationToken: token));
        var references = (await Manifest("references")).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("QuarkValidationInput", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = await RazorModelDiscovery.Discover(compilation, await Manifest("razor"),
            await Manifest("additional"), await Manifest("configs"), options, CancellationToken.None);
        var index = 0;
        foreach (SyntaxTree tree in result)
        {
            await using var writer = new StreamWriter(Path.Combine(directory, "generated-" + index++ + ".cs"));
            (await tree.GetTextAsync()).Write(writer);
        }
    }
}
