using System.Reflection;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

/// <summary>Fake for large Jellyfin interfaces: records calls and runs a handler per method name, else returns a default (completed tasks for Task returns).</summary>
public class InterfaceFake : DispatchProxy
{
    private readonly Dictionary<string, Func<object?[], object?>> _handlers = new(StringComparer.Ordinal);
    private readonly List<(string Method, object?[] Args)> _calls = [];

    public static (T Instance, InterfaceFake Fake) Create<T>()
        where T : class
    {
        var instance = Create<T, InterfaceFake>();
        return (instance, (InterfaceFake)(object)instance);
    }

    public InterfaceFake On(string method, Func<object?[], object?> handler)
    {
        _handlers[method] = handler;
        return this;
    }

    public IReadOnlyList<object?[]> Calls(string method)
    {
        lock (_calls)
        {
            return _calls.Where(c => c.Method == method).Select(c => c.Args).ToList();
        }
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        args ??= [];
        lock (_calls)
        {
            _calls.Add((targetMethod.Name, args));
        }

        if (_handlers.TryGetValue(targetMethod.Name, out var handler))
        {
            return handler(args);
        }

        var type = targetMethod.ReturnType;
        if (type == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = type.GetGenericArguments()[0];
            var value = inner.IsValueType ? Activator.CreateInstance(inner) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [value]);
        }

        return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
    }
}
