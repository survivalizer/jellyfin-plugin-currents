using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Replaces a registered service with a decorator that wraps the original registration.</summary>
public static class ServiceCollectionDecoratorExtensions
{
    public static IServiceCollection Decorate<TService, TDecorator>(this IServiceCollection services)
        where TService : class
        where TDecorator : class, TService
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(TService) && !d.IsKeyedService)
            ?? throw new InvalidOperationException($"{typeof(TService).Name} is not registered.");
        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            sp => ActivatorUtilities.CreateInstance<TDecorator>(sp, (TService)CreateInner(sp, descriptor)),
            descriptor.Lifetime));
        return services;
    }

    private static object CreateInner(IServiceProvider sp, ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(sp)
        ?? ActivatorUtilities.GetServiceOrCreateInstance(sp, descriptor.ImplementationType!);
}
