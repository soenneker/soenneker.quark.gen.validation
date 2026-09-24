using System;
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

public sealed class ValidationWriteRunner(IFileUtil fileUtil, IDirectoryUtil directoryUtil) : Abstract.IValidationWriteRunner
{
    public async ValueTask<int> Run(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var map = ParseArgs(args);
            string Required(string key) => map.TryGetValue(key, out var value) && !value.IsNullOrWhiteSpace() ? value : throw new ArgumentException("Missing required " + key);
            var outputPath = Path.GetFullPath(Required("--output"));
            var sourcePaths = await fileUtil.ReadAsLines(Required("--sources"), cancellationToken: cancellationToken);
            var referencePaths = await fileUtil.ReadAsLines(Required("--references"), cancellationToken: cancellationToken);
            var defines = map.TryGetValue("--defines", out var definesPath) ? await fileUtil.ReadAsLines(definesPath, cancellationToken: cancellationToken) : [];
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: defines.ExceptNullOrWhiteSpace());
            var trees = new List<SyntaxTree>();
            foreach (var path in sourcePaths.ExceptNullOrWhiteSpace().DistinctIgnoreCase())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFullPath(path).EqualsIgnoreCase(outputPath)) continue;
                trees.Add(CSharpSyntaxTree.ParseText(await fileUtil.Read(path, cancellationToken: cancellationToken), parseOptions, path, cancellationToken: cancellationToken));
            }
            var references = referencePaths.ExceptNullOrWhiteSpace().DistinctIgnoreCase().Select(p => MetadataReference.CreateFromFile(p));
            var compilation = CSharpCompilation.Create("QuarkValidationInput", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            async ValueTask<string[]> Manifest(string key) => map.TryGetValue(key, out var path)
                ? (await fileUtil.ReadAsLines(path, cancellationToken: cancellationToken)).ExceptNullOrWhiteSpace().ToArray() : [];
            compilation = await RazorModelDiscovery.AddRazorModels(compilation, await Manifest("--razor"), await Manifest("--additional"), await Manifest("--configs"), parseOptions, fileUtil, cancellationToken);
            var output = ValidationEmitter.Emit(compilation, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await directoryUtil.Create(Path.GetDirectoryName(outputPath)!, cancellationToken: cancellationToken);
            if (!await fileUtil.Exists(outputPath, cancellationToken) || await fileUtil.Read(outputPath, cancellationToken: cancellationToken) != output)
                await fileUtil.WriteAtomically(outputPath, output, cancellationToken: cancellationToken);
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine("Quark validation generation failed: " + exception.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--sources" or "--references" or "--output" or "--defines" or "--razor" or "--additional" or "--configs") || !map.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Invalid or duplicate argument: " + args[i]);
        }
        return map;
    }
}
