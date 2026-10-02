using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

internal sealed class RazorAdditionalText(string path, SourceText text) : AdditionalText
{
    public override string Path => path;

    public override SourceText GetText(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }
}
