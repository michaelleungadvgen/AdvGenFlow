using System.Reflection;

namespace AdvGenFlow;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAdvGenFlow(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());

        foreach (var assembly in assemblies)
            RegisterHandlers(services, assembly);

        return services;
    }

    public static IServiceCollection AddAdvGenFlowBehavior<TBehavior>(
        this IServiceCollection services)
        where TBehavior : class
        => services.AddAdvGenFlowBehavior(typeof(TBehavior));

    public static IServiceCollection AddAdvGenFlowBehavior(
        this IServiceCollection services,
        Type behaviorType)
    {
        if (!behaviorType.IsGenericTypeDefinition)
            throw new ArgumentException(
                $"Type '{behaviorType.Name}' must be an open-generic type definition (e.g. MyBehavior<,>).",
                nameof(behaviorType));
        services.AddTransient(typeof(IPipelineBehavior<,>), behaviorType);
        return services;
    }

    // GeneratedMediator is emitted into the consumer's compilation by AdvGenFlow.SourceGen.
    // It cannot be referenced from this library. The consumer passes it as a type argument.
    // Consumer usage: services.AddAdvGenFlowGenerated<GeneratedMediator>(typeof(Program).Assembly)
    public static IServiceCollection AddAdvGenFlowGenerated<TGeneratedMediator>(
        this IServiceCollection services,
        params Assembly[] assemblies)
        where TGeneratedMediator : class, IMediator
    {
        services.AddTransient<IMediator, TGeneratedMediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());

        foreach (var assembly in assemblies)
            RegisterHandlers(services, assembly);

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        var openInterfaces = new[]
        {
            typeof(IRequestHandler<,>),
            typeof(INotificationHandler<>),
            typeof(IStreamRequestHandler<,>),
        };

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;

            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType) continue;

                var generic = iface.GetGenericTypeDefinition();
                if (Array.Exists(openInterfaces, o => o == generic))
                    services.AddTransient(iface, type);
            }
        }
    }
}
