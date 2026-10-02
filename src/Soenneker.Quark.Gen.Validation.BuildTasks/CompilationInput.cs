using Soenneker.Extensions.Task;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

/// <summary>Loads source files into syntax trees with bounded concurrency.</summary>
public static class CompilationInput
{
    /// <summary>Parses source files using the supplied options and preserves their input order.</summary>
    public static async ValueTask<SyntaxTree[]> LoadSources(IReadOnlyList<string> paths, CSharpParseOptions options,
        IFileUtil fileUtil, CancellationToken cancellationToken)
    {
        var trees = new SyntaxTree[paths.Count];
        if (paths.Count < 16 || Environment.ProcessorCount == 1)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                trees[i] = CSharpSyntaxTree.ParseText(await fileUtil.Read(paths[i], log: false, cancellationToken: cancellationToken).NoSync(),
                    options, paths[i], cancellationToken: cancellationToken);
            }
        }
        else
        {
            await Parallel.ForAsync(0, paths.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8),
                CancellationToken = cancellationToken
            }, async (i, token) =>
            {
                trees[i] = CSharpSyntaxTree.ParseText(await fileUtil.Read(paths[i], log: false, cancellationToken: token).NoSync(),
                    options, paths[i], cancellationToken: token);
            }).NoSync();
        }
        return trees;
    }
}
