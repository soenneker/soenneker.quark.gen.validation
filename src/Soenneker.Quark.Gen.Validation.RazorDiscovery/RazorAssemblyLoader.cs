using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

internal sealed class RazorAssemblyLoader : IAnalyzerAssemblyLoader
{
    public void AddDependencyLocation(string fullPath) { }
    public Assembly LoadFromPath(string fullPath) => AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
}
