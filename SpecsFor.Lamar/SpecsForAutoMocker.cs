using JasperFx.Core.Reflection;
using Lamar;
using Lamar.IoC.Instances;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace SpecsFor.Lamar;

public class SpecsForAutoMocker<TSut> where TSut : class
{
    private readonly MoqServiceLocator _locator;

    public Container Container { get; protected set; }

    private readonly HashSet<Type> _registeredTypes = new();

    public T Get<T>() where T : class
    {
        return Container.GetInstance<T>();
    }

    public TSut ClassUnderTest => Container.GetInstance<TSut>();

    public SpecsForAutoMocker()
    {
        _locator = new MoqServiceLocator();

        var registry = new ServiceRegistry();

        RegisterType(typeof(TSut), registry);

        registry.Policies.OnMissingFamily(new AutoMockingFamilyPolicy(_locator));
        Container = new Container(registry);
    }

    private void RegisterType(Type type, ServiceRegistry registry)
    {
        if (_registeredTypes.Contains(type))
        {
            return;
        }

        _registeredTypes.Add(type);

        if (type.IsInterface
            || type.IsAbstract
            || type.IsPrimitive
            || type == typeof(string)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Func<>)))
        {
            registry.AddScoped(type, _ => _locator.Service(type));
            return;
        }

        if (type.IsArray)
        {
            registry.AddScoped(type, _ => Array.CreateInstance(type, 0));
            return;
        }

        var parameterLessConstructor = type.GetConstructor(Type.EmptyTypes);

        if (parameterLessConstructor != null)
        {
            // If the type has a parameterless constructor, register it directly
            registry.For(type).Use(type).Scoped();
            return;
        }

        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        if (constructor == null)
        {
            // No public constructor found, cannot register type
            return;
        }

        var parameters = constructor.GetParameters();

        foreach (var param in parameters)
        {
            RegisterType(param.ParameterType, registry);
        }

        registry.For(type).Use(type).Scoped();
    }

    private class AutoMockingFamilyPolicy : IFamilyPolicy
    {
        private readonly MoqServiceLocator _locator;

        public AutoMockingFamilyPolicy(MoqServiceLocator locator)
        {
            _locator = locator;
        }

        public ServiceFamily Build(Type type, ServiceGraph serviceGraph)
        {
            if (type.IsConcrete())
            {
                return null;
            }

            var service = _locator.Service(type);

            if (service == null)
            {
                return null;
            }

            var family = new ServiceFamily(type, Array.Empty<IDecoratorPolicy>());
            var instance = new ObjectInstance(type, service);

            family.Append(new Instance[] { instance }, Array.Empty<IDecoratorPolicy>());

            return family;
        }
    }
}