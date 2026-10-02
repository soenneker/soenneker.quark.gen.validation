using Soenneker.Extensions.Task;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.ValueTask;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Enumerable.String;
using Soenneker.Utils.File.Abstract;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

internal static class RazorModelDiscovery
{
    internal static async ValueTask<ImmutableArray<SyntaxTree>> Discover(CSharpCompilation compilation,
        string[] analyzerPaths, string[] additionalPaths, string[] configPaths, CSharpParseOptions parseOptions,
        IFileUtil fileUtil, CancellationToken token)
    {
        if (analyzerPaths.Length == 0 || !additionalPaths.Any(p => p.EndsWithIgnoreCase(".razor")))
            return [];
        string?[] directories = analyzerPaths.Select(Path.GetDirectoryName).Distinct().ToArray();

        Assembly? Resolve(AssemblyLoadContext _, AssemblyName name)
        {
            foreach (string? directory in directories)
            {
                string path = Path.Combine(directory!, name.Name + ".dll");
                if (fileUtil.Exists(path, token).AwaitSyncSafe(token))
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }

            return null;
        }

        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var loader = new RazorAssemblyLoader();
            ISourceGenerator[] generators = analyzerPaths.Select(p => new AnalyzerFileReference(p, loader))
                                                         .SelectMany(r => r.GetGenerators(LanguageNames.CSharp))
                                                         .ToArray();
            if (generators.Length == 0)
                throw new InvalidOperationException("QV002: The selected SDK's Razor generator could not be loaded.");
            ImmutableArray<AnalyzerConfig>.Builder configurationFiles = ImmutableArray.CreateBuilder<AnalyzerConfig>();
            foreach (string path in configPaths)
            {
                if (await fileUtil.Exists(path, token).NoSync())
                    configurationFiles.Add(AnalyzerConfig.Parse(
                        SourceText.From(await fileUtil.Read(path, cancellationToken: token).NoSync()), path));
            }

            var configs = AnalyzerConfigSet.Create(configurationFiles.ToImmutable());
            var additionalTexts = new List<AdditionalText>(additionalPaths.Length);
            foreach (string path in additionalPaths)
                additionalTexts.Add(new RazorAdditionalText(path,
                    SourceText.From(await fileUtil.Read(path, cancellationToken: token).NoSync())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, additionalTexts, parseOptions,
                new RazorAnalyzerOptionsProvider(configs));
            driver = driver.RunGenerators(compilation, token);
            GeneratorDriverRunResult result = driver.GetRunResult();
            if (result.Results.Any(r => r.Exception is not null))
                throw new InvalidOperationException("QV002: Razor model discovery failed: " + result.Results
                    .Where(r => r.Exception is not null).Select(r => r.Exception!.Message)
                    .ToSeparatedString(';', includeSpace: true));
            return result.GeneratedTrees;
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= Resolve;
        }
    }
}