using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

internal sealed class RazorAnalyzerOptions(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
}
