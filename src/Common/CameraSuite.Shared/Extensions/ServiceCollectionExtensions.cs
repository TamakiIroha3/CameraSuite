using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Models;
using CameraSuite.Shared.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CameraSuite.Shared.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCameraSuiteCore(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<CameraSuiteOptions>()
            .Bind(configuration.GetSection(CameraSuiteOptions.SectionName));

        services.AddSingleton(JsonOptionsFactory.CreateDefault());
        services.AddSingleton(MessagingJsonContext.Default);

        return services;
    }
}

