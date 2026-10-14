using System;
using System.IO;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Soenneker.Quark.Gen.Validation.BuildTasks;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
                throw new ArgumentException("Expected the input directory and selected SDK directory.");

            string compilerDirectory = Path.Combine(Path.GetFullPath(args[1]), "Roslyn", "bincore");
            // Bind SDK assemblies before compiling DiscoveryRunner, which references the baseline Roslyn API.
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(compilerDirectory, "Microsoft.CodeAnalysis.dll"));
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(compilerDirectory, "Microsoft.CodeAnalysis.CSharp.dll"));
            await DiscoveryRunner.Run(args[0]);
            return 0;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync("QV002: Razor model discovery failed (SDK: " +
                (args.Length > 1 ? args[1] : "unspecified") + "): " + exception);
            return 1;
        }
    }
}
