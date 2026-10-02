using System;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

internal sealed class RazorAnalyzerOptionsProvider(AnalyzerConfigSet configs) : AnalyzerConfigOptionsProvider
{
    private readonly ConcurrentDictionary<string, AnalyzerConfigOptions> _options = new(StringComparer.Ordinal);

    public override AnalyzerConfigOptions GlobalOptions { get; } = new RazorAnalyzerOptions(configs.GlobalConfigOptions.AnalyzerOptions);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GetOptions(tree.FilePath);

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GetOptions(textFile.Path);

    private AnalyzerConfigOptions GetOptions(string path) => _options.GetOrAdd(path,
        static (key, configSet) => new RazorAnalyzerOptions(configSet.GetOptionsForSourcePath(key).AnalyzerOptions), configs);
}
