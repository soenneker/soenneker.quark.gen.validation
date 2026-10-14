using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

internal static class RazorModelDiscovery
{
    internal static async ValueTask<ImmutableArray<SyntaxTree>> Discover(CSharpCompilation compilation,
        string[] analyzerPaths, string[] additionalPaths, string[] configPaths, CSharpParseOptions parseOptions,
        CancellationToken token)
    {
        if (analyzerPaths.Length == 0 || !additionalPaths.Any(p => p.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
            return [];
        string?[] directories = analyzerPaths.Select(Path.GetDirectoryName).Distinct().ToArray();

        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            foreach (string? directory in directories)
            {
                string path = Path.Combine(directory!, name.Name + ".dll");
                if (File.Exists(path))
                    return context.LoadFromAssemblyPath(path);
            }
            return null;
        }

        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var loader = new RazorAssemblyLoader();
            var failures = new List<string>();
            var generators = new List<ISourceGenerator>();
            foreach (string path in analyzerPaths)
            {
                var reference = new AnalyzerFileReference(path, loader);
                reference.AnalyzerLoadFailed += (_, failure) => failures.Add(
                    path + ": " + failure.ErrorCode + " " + failure.Message + " " + failure.Exception);
                generators.AddRange(reference.GetGenerators(LanguageNames.CSharp));
            }
            if (generators.Count == 0 || failures.Count != 0)
                throw new InvalidOperationException("QV002: The selected SDK's Razor generator could not be loaded. " +
                    "Roslyn: " + typeof(Compilation).Assembly.FullName + " (" + typeof(Compilation).Assembly.Location +
                    "). Analyzers: " + string.Join("; ", analyzerPaths) + ". " + string.Join("; ", failures));

            ImmutableArray<AnalyzerConfig>.Builder configurationFiles = ImmutableArray.CreateBuilder<AnalyzerConfig>();
            foreach (string path in configPaths)
            {
                if (File.Exists(path))
                    configurationFiles.Add(AnalyzerConfig.Parse(SourceText.From(await File.ReadAllTextAsync(path, token)), path));
            }
            var configs = AnalyzerConfigSet.Create(configurationFiles.ToImmutable());
            var additionalTexts = new List<AdditionalText>(additionalPaths.Length);
            foreach (string path in additionalPaths)
                additionalTexts.Add(new RazorAdditionalText(path, SourceText.From(await File.ReadAllTextAsync(path, token))));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, additionalTexts, parseOptions,
                new RazorAnalyzerOptionsProvider(configs));
            driver = driver.RunGenerators(compilation, token);
            GeneratorDriverRunResult result = driver.GetRunResult();
            if (result.Results.Any(r => r.Exception is not null))
                throw new InvalidOperationException("QV002: Razor model discovery failed: " + string.Join("; ",
                    result.Results.Where(r => r.Exception is not null).Select(r => r.Exception!.ToString())));
            return result.GeneratedTrees;
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= Resolve;
        }
    }
}
