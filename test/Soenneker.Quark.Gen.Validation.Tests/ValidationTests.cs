using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Quark.Gen.Validation.BuildTasks;
using Soenneker.Quark.Gen.Validation.BuildTasks.Abstract;

namespace Soenneker.Quark.Gen.Validation.Tests;

public sealed class ValidationTests
{
    [Test]
    public async Task Missing_arguments_return_failure()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IValidationWriteRunner>();

        int exitCode = await runner.Run([], CancellationToken.None);
        if (exitCode != 1)
            throw new InvalidOperationException($"Expected missing arguments to fail, got exit code {exitCode}.");
    }
}
