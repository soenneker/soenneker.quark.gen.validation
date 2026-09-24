using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Quark.Gen.Validation.BuildTasks.Abstract;

public interface IValidationWriteRunner
{
    /// <summary>Runs file generation using the supplied command-line arguments.</summary>
    /// <param name="args">--sources and --references specify newline-delimited file manifests; --output is the generated C# path. Optional --defines, --razor, --additional and --configs provide symbols, SDK Razor generators, additional files and analyzer configurations.</param>
    /// <param name="cancellationToken">Token used to cancel generation.</param>
    /// <returns>Zero on success; a nonzero exit code on failure.</returns>
    ValueTask<int> Run(string[] args, CancellationToken cancellationToken);
}
