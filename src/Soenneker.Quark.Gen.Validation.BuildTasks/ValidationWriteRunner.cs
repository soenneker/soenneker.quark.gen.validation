using Soenneker.Extensions.ValueTask;
using Soenneker.Extensions.Task;
using System;
using Soenneker.Utils.Process.Abstract;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Enumerable.String;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

public sealed class ValidationWriteRunner(IFileUtil fileUtil, IDirectoryUtil directoryUtil, IProcessUtil processUtil)
    : Abstract.IValidationWriteRunner
{
    public async ValueTask<int> Run(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            Dictionary<string, string> map = ParseArgs(args);

            string Required(string key) => map.TryGetValue(key, out string? value) && !value.IsNullOrWhiteSpace()
                ? value
                : throw new ArgumentException("Missing required " + key);

            string outputPath = Path.GetFullPath(Required("--output"));
            List<string> sourcePaths =
                await fileUtil.ReadAsLines(Required("--sources"), cancellationToken: cancellationToken).NoSync();
            List<string> referencePaths =
                await fileUtil.ReadAsLines(Required("--references"), cancellationToken: cancellationToken).NoSync();
            List<string> defines = map.TryGetValue("--defines", out string? definesPath)
                ? await fileUtil.ReadAsLines(definesPath, cancellationToken: cancellationToken).NoSync()
                : [];
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview,
                preprocessorSymbols: defines.ExceptNullOrWhiteSpace());
            if (map.TryGetValue("--features", out string? featuresPath))
            {
                var features = await fileUtil.ReadAsLines(featuresPath, cancellationToken: cancellationToken).NoSync();
                parseOptions = parseOptions.WithFeatures(features.Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(f => f.Split('=', 2)).Select(f => new KeyValuePair<string, string>(f[0], f.Length == 2 ? f[1] : "true")));
            }
            StringComparer pathComparer =
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var uniquePaths = new HashSet<string>(sourcePaths.Count, pathComparer);
            var paths = new List<string>(sourcePaths.Count);
            foreach (string path in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                string fullPath = Path.GetFullPath(path);
                if (!pathComparer.Equals(fullPath, outputPath) && uniquePaths.Add(fullPath))
                    paths.Add(fullPath);
            }

            SyntaxTree[] trees = await CompilationInput.LoadSources(paths, parseOptions, fileUtil, cancellationToken)
                                                       .NoSync();
            uniquePaths.Clear();
            var references = new List<MetadataReference>(referencePaths.Count);
            foreach (string path in referencePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                string fullPath = Path.GetFullPath(path);
                if (uniquePaths.Add(fullPath))
                    references.Add(MetadataReference.CreateFromFile(fullPath));
            }

            var compilation = CSharpCompilation.Create("QuarkValidationInput", trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            async ValueTask<string[]> Manifest(string key) => map.TryGetValue(key, out string? path)
                ? (await fileUtil.ReadAsLines(path, cancellationToken: cancellationToken).NoSync())
                  .ExceptNullOrWhiteSpace().ToArray()
                : [];

            string[] analyzers = await Manifest("--razor").NoSync();
            if (analyzers.Length != 0)
            {
                string[] additional = await Manifest("--additional").NoSync();
                if (additional.Any(path => path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
                    compilation = await RazorModelDiscovery.AddRazorModels(compilation, analyzers, additional,
                        await Manifest("--configs").NoSync(), Required("--sdk"), parseOptions, fileUtil, directoryUtil, processUtil,
                        cancellationToken).NoSync();
            }

            if (map.TryGetValue("--razor-output", out string? razorOutput))
            {
                razorOutput = Path.GetFullPath(razorOutput);
                await directoryUtil.Create(razorOutput, cancellationToken: cancellationToken).NoSync();
                var generatedPaths = new List<string>();
                foreach (SyntaxTree tree in compilation.SyntaxTrees.Except(trees))
                {
                    string generatedPath = Path.Combine(razorOutput, "Razor-" + generatedPaths.Count + ".g.cs");
                    string generated = RazorBindingRewriter.Rewrite(compilation, tree).GetText(cancellationToken).ToString();
                    if (!await GeneratedFile.Matches(generatedPath, generated, fileUtil, cancellationToken).NoSync())
                        await fileUtil.WriteAtomically(generatedPath, generated, cancellationToken: cancellationToken).NoSync();
                    generatedPaths.Add(generatedPath);
                }
                string manifest = string.Join(Environment.NewLine, generatedPaths);
                string manifestPath = Path.Combine(razorOutput, "sources.txt");
                if (!await GeneratedFile.Matches(manifestPath, manifest, fileUtil, cancellationToken).NoSync())
                    await fileUtil.WriteAtomically(manifestPath, manifest, cancellationToken: cancellationToken).NoSync();
            }

            string output = ValidationEmitter.Emit(compilation, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await directoryUtil.Create(Path.GetDirectoryName(outputPath)!, cancellationToken: cancellationToken)
                               .NoSync();
            if (!await GeneratedFile.Matches(outputPath, output, fileUtil, cancellationToken).NoSync())
                await fileUtil.WriteAtomically(outputPath, output, cancellationToken: cancellationToken).NoSync();
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Quark validation generation failed: " + exception.Message).NoSync();
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(7, StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length ||
                args[i] is not ("--sources" or "--references" or "--output" or "--defines" or "--razor"
                    or "--additional" or "--configs" or "--sdk" or "--razor-output" or "--features") || !map.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Invalid or duplicate argument: " + args[i]);
        }

        return map;
    }
}
