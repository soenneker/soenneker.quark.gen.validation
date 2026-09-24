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
    internal static async ValueTask<CSharpCompilation> AddRazorModels(CSharpCompilation compilation, string[] analyzerPaths, string[] additionalPaths, string[] configPaths, CSharpParseOptions parseOptions, IFileUtil fileUtil, CancellationToken token)
    {
        if (analyzerPaths.Length == 0 || !additionalPaths.Any(p => p.EndsWithIgnoreCase(".razor"))) return compilation;
        var directories = analyzerPaths.Select(Path.GetDirectoryName).Distinct().ToArray();
        Assembly? Resolve(AssemblyLoadContext _, AssemblyName name)
        {
            foreach (var directory in directories)
            {
                var path = Path.Combine(directory!, name.Name + ".dll");
                if (fileUtil.Exists(path, token).AwaitSyncSafe(token)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var generators = analyzerPaths.Select(p => new AnalyzerFileReference(p, new Loader())).SelectMany(r => r.GetGenerators(LanguageNames.CSharp)).ToArray();
            if (generators.Length == 0) throw new InvalidOperationException("QV002: The selected SDK's Razor generator could not be loaded.");
            var configurationFiles = ImmutableArray.CreateBuilder<AnalyzerConfig>();
            foreach (var path in configPaths)
            {
                if (await fileUtil.Exists(path, token))
                    configurationFiles.Add(AnalyzerConfig.Parse(SourceText.From(await fileUtil.Read(path, cancellationToken: token)), path));
            }
            var configs = AnalyzerConfigSet.Create(configurationFiles.ToImmutable());
            var additionalTexts = new List<AdditionalText>();
            foreach (var path in additionalPaths)
                additionalTexts.Add(new Additional(path, SourceText.From(await fileUtil.Read(path, cancellationToken: token))));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, additionalTexts, parseOptions, new OptionsProvider(configs));
            driver = driver.RunGenerators(compilation, token);
            var result = driver.GetRunResult();
            if (result.Results.Any(r => r.Exception is not null)) throw new InvalidOperationException("QV002: Razor model discovery failed: " + result.Results.Where(r => r.Exception is not null).Select(r => r.Exception!.Message).ToSeparatedString(';', includeSpace: true));
            return compilation.AddSyntaxTrees(result.GeneratedTrees);
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }

    private sealed class Loader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath) { }
        public Assembly LoadFromPath(string fullPath) => AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
    private sealed class Additional(string path, SourceText text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return text;
        }
    }
    private sealed class Options(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
    private sealed class OptionsProvider(AnalyzerConfigSet configs) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions => new Options(configs.GlobalConfigOptions.AnalyzerOptions);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options(configs.GetOptionsForSourcePath(tree.FilePath).AnalyzerOptions);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(configs.GetOptionsForSourcePath(textFile.Path).AnalyzerOptions);
    }
}
