using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Quark.Gen.Validation.BuildTasks.Abstract;

public interface IValidationWriteRunner
{
    /// <summary>Runs file generation using the supplied command-line arguments.</summary>
    /// <param name="args">--sources and --references specify newline-delimited file manifests; --output is the generated C# path. Optional --defines, --features, --razor, --additional and --configs provide symbols, compiler features, SDK Razor generators, additional files and analyzer configurations. --razor-output saves the rewritten Razor sources and their sources.txt manifest for compilation.</param>
    /// <param name="cancellationToken">Token used to cancel generation.</param>
    /// <returns>Zero on success; a nonzero exit code on failure.</returns>
    ValueTask<int> Run(string[] args, CancellationToken cancellationToken);
}
