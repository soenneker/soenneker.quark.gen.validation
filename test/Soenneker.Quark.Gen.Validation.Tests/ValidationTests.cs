using Soenneker.Extensions.ValueTask;
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
    public async ValueTask Missing_arguments_return_failure()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            AsyncServiceScope scope = provider.CreateAsyncScope();
            try
            {
                var runner = scope.ServiceProvider.GetRequiredService<IValidationWriteRunner>();

                int exitCode = await runner.Run([], CancellationToken.None).NoSync();
                if (exitCode != 1)
                    throw new InvalidOperationException($"Expected missing arguments to fail, got exit code {exitCode}.");
            }
            finally
            {
                await scope.DisposeAsync().NoSync();
            }
        }
        finally
        {
            await provider.DisposeAsync().NoSync();
        }
    }
}
