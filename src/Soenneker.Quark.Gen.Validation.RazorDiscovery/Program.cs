using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.File.Registrars;
using Soenneker.Quark.Gen.Validation.BuildTasks;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            string directory = args[0];
            string[] Manifest(string name) => File.ReadAllLines(Path.Combine(directory, name + ".txt"));
            var options = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: Manifest("defines"));
            var trees = Manifest("sources").Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path));
            var references = Manifest("references").Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("QuarkValidationInput", trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddFileUtilAsSingleton();
            await using var provider = services.BuildServiceProvider();
            var result = await RazorModelDiscovery.AddRazorModels(compilation, Manifest("razor"), Manifest("additional"),
                Manifest("configs"), options, provider.GetRequiredService<IFileUtil>(), CancellationToken.None);
            int index = 0;
            foreach (var tree in result.SyntaxTrees.Except(compilation.SyntaxTrees))
                await File.WriteAllTextAsync(Path.Combine(directory, "generated-" + index++ + ".cs"), tree.ToString());
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("QV002: Razor model discovery failed: " + exception);
            return 1;
        }
    }
}
