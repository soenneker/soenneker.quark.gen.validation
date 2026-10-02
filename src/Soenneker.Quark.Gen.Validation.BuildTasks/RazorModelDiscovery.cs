using Soenneker.Extensions.ValueTask;
using Soenneker.Extensions.Task;
using System;
using System.Collections.Generic;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.Dotnet.Abstract;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

internal static class RazorModelDiscovery
{
    internal static async ValueTask<CSharpCompilation> AddRazorModels(CSharpCompilation compilation, string[] analyzerPaths,
        string[] additionalPaths, string[] configPaths, CSharpParseOptions parseOptions, IFileUtil fileUtil, IDirectoryUtil directoryUtil, IDotnetUtil dotnetUtil, CancellationToken token)
    {
        if (analyzerPaths.Length == 0 || !additionalPaths.Any(path => path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
            return compilation;

        // The selected SDK's Razor generator must run in a managed host; the validation emitter remains native.
        string helper = Path.Combine(AppContext.BaseDirectory, "razor", "Soenneker.Quark.Gen.Validation.RazorDiscovery.dll");
        for (var parent = new DirectoryInfo(AppContext.BaseDirectory); !await fileUtil.Exists(helper, token).NoSync() && parent is not null; parent = parent.Parent)
            helper = Path.Combine(parent.FullName, "artifacts", "native", RuntimeInformation.RuntimeIdentifier, "razor", "Soenneker.Quark.Gen.Validation.RazorDiscovery.dll");
        if (!await fileUtil.Exists(helper, token).NoSync())
            throw new InvalidOperationException("QV002: Razor discovery helper was not found: " + helper);

        string directory = await directoryUtil.CreateTempDirectory(token).NoSync();
        try
        {
            IEnumerable<string> sourcePaths = compilation.SyntaxTrees.Select(tree => tree.FilePath);
            await fileUtil.WriteAllLines(Path.Combine(directory, "sources.txt"), sourcePaths, cancellationToken: token).NoSync();
            await fileUtil.WriteAllLines(Path.Combine(directory, "references.txt"), compilation.References.OfType<PortableExecutableReference>().Select(reference => reference.FilePath!), cancellationToken: token).NoSync();
            await fileUtil.WriteAllLines(Path.Combine(directory, "defines.txt"), parseOptions.PreprocessorSymbolNames, cancellationToken: token).NoSync();
            await fileUtil.WriteAllLines(Path.Combine(directory, "razor.txt"), analyzerPaths, cancellationToken: token).NoSync();
            await fileUtil.WriteAllLines(Path.Combine(directory, "additional.txt"), additionalPaths, cancellationToken: token).NoSync();
            await fileUtil.WriteAllLines(Path.Combine(directory, "configs.txt"), configPaths, cancellationToken: token).NoSync();
            try
            {
                await dotnetUtil.Execute("exec " + CommandLineArguments.Quote(helper) + " " + CommandLineArguments.Quote(directory), token).NoSync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new InvalidOperationException("QV002: Razor discovery failed: " + exception.Message, exception);
            }
            List<string> paths = await directoryUtil.GetFilesByExtension(directory, ".cs", cancellationToken: token).NoSync();
            paths.RemoveAll(path => !Path.GetFileName(path).StartsWith("generated-", StringComparison.Ordinal));
            paths.Sort(StringComparer.Ordinal);
            SyntaxTree[] generatedTrees = await CompilationInput.LoadSources(paths, parseOptions, fileUtil, token).NoSync();
            return compilation.AddSyntaxTrees(generatedTrees);
        }
        finally { await directoryUtil.Delete(directory, CancellationToken.None).NoSync(); }
    }
}
