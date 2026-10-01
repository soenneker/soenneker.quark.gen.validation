using System;
using System.Diagnostics;
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
        string[] additionalPaths, string[] configPaths, CSharpParseOptions parseOptions, IFileUtil fileUtil, CancellationToken token)
    {
        if (analyzerPaths.Length == 0 || !additionalPaths.Any(path => path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
            return compilation;

        // The selected SDK's Razor generator must run in a managed host; the validation emitter remains native.
        string helper = Path.Combine(AppContext.BaseDirectory, "razor", "Soenneker.Quark.Gen.Validation.RazorDiscovery.dll");
        for (DirectoryInfo? parent = new DirectoryInfo(AppContext.BaseDirectory); !File.Exists(helper) && parent is not null; parent = parent.Parent)
            helper = Path.Combine(parent.FullName, "artifacts", "native", RuntimeInformation.RuntimeIdentifier, "razor", "Soenneker.Quark.Gen.Validation.RazorDiscovery.dll");
        if (!File.Exists(helper))
            throw new InvalidOperationException("QV002: Razor discovery helper was not found: " + helper);

        string directory = Path.Combine(Path.GetTempPath(), "quark-razor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePaths = compilation.SyntaxTrees.Select(tree => tree.FilePath);
            await File.WriteAllLinesAsync(Path.Combine(directory, "sources.txt"), sourcePaths, token);
            await File.WriteAllLinesAsync(Path.Combine(directory, "references.txt"), compilation.References.OfType<PortableExecutableReference>().Select(reference => reference.FilePath!), token);
            await File.WriteAllLinesAsync(Path.Combine(directory, "defines.txt"), parseOptions.PreprocessorSymbolNames, token);
            await File.WriteAllLinesAsync(Path.Combine(directory, "razor.txt"), analyzerPaths, token);
            await File.WriteAllLinesAsync(Path.Combine(directory, "additional.txt"), additionalPaths, token);
            await File.WriteAllLinesAsync(Path.Combine(directory, "configs.txt"), configPaths, token);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add(helper);
            start.ArgumentList.Add(directory);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("QV002: Could not start Razor discovery.");
            try { await process.WaitForExitAsync(token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            if (process.ExitCode != 0)
                throw new InvalidOperationException("QV002: Razor discovery failed with exit code " + process.ExitCode);
            foreach (string path in Directory.GetFiles(directory, "generated-*.cs").OrderBy(path => path, StringComparer.Ordinal))
                compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(path, token), parseOptions, path, cancellationToken: token));
            return compilation;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
