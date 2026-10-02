using Microsoft.Extensions.DependencyInjection;
using Soenneker.Utils.Dotnet.Registrars;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.Directory.Registrars;
using Soenneker.Quark.Gen.Validation.BuildTasks.Abstract;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

public static class Startup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddFileUtilAsSingleton();
        services.AddDirectoryUtilAsSingleton();
        services.AddDotnetUtilAsSingleton();
        services.AddSingleton<IValidationWriteRunner, ValidationWriteRunner>();
    }
}
