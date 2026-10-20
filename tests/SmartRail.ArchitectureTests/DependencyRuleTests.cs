using System.Reflection;
using NetArchTest.Rules;
using SmartRail.SharedKernel.Primitives;

namespace SmartRail.ArchitectureTests;

/// <summary>
/// The dependency rules every module follows (design D5, <c>architecture.md</c> §2): dependencies
/// point inward, no module reaches into another module's layers, and entities live in the Domain.
/// </summary>
public sealed class DependencyRuleTests
{
    /// <summary>
    /// The known current violations, excluded from the rules until the module refactor removes
    /// them. Each entry is removed by tasks 2.2 (Rail Network) / 3.2 (Live Operations).
    /// </summary>
    private static readonly string[] PendingMoves =
    [
    ];

    private static readonly ModuleLayers[] Modules =
    [
        new(
            "IdentityAccess",
            Domain("IdentityAccess"),
            typeof(IdentityAccess.Application.DependencyInjection).Assembly,
            typeof(IdentityAccess.Infrastructure.DependencyInjection).Assembly,
            typeof(IdentityAccess.Api.DependencyInjection).Assembly),
        new(
            "RailNetwork",
            typeof(RailNetwork.Domain.Entities.Line).Assembly,
            typeof(RailNetwork.Application.DependencyInjection).Assembly,
            typeof(RailNetwork.Infrastructure.DependencyInjection).Assembly,
            typeof(RailNetwork.Api.DependencyInjection).Assembly),
        new(
            "LiveOperations",
            Domain("LiveOperations"),
            typeof(LiveOperations.Application.DependencyInjection).Assembly,
            typeof(LiveOperations.Infrastructure.DependencyInjection).Assembly,
            typeof(LiveOperations.Api.DependencyInjection).Assembly),
        new(
            "Distribution",
            Domain: null,
            typeof(Distribution.Application.DependencyInjection).Assembly,
            typeof(Distribution.Infrastructure.DependencyInjection).Assembly,
            typeof(Distribution.Api.DependencyInjection).Assembly),
        new(
            "Catering",
            Domain("Catering"),
            typeof(Catering.Application.DependencyInjection).Assembly,
            typeof(Catering.Infrastructure.DependencyInjection).Assembly,
            typeof(Catering.Api.DependencyInjection).Assembly),
        new(
            "Audit",
            Domain("Audit"),
            typeof(Audit.Application.DependencyInjection).Assembly,
            typeof(Audit.Infrastructure.DependencyInjection).Assembly,
            typeof(Audit.Api.DependencyInjection).Assembly),
    ];

    [Fact]
    public void Domain_depends_only_on_the_shared_kernel()
    {
        foreach (var module in Modules.Where(module => module.Domain is not null))
        {
            var forbidden = new List<string>
            {
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "MediatR",
                $"SmartRail.{module.Name}.Application",
                $"SmartRail.{module.Name}.Infrastructure",
                $"SmartRail.{module.Name}.Api",
                $"SmartRail.{module.Name}.Contracts",
            };

            forbidden.AddRange(OtherModuleLayers(module.Name));
            forbidden.AddRange(Modules.Select(other => $"SmartRail.{other.Name}.Contracts"));

            var result = TypesIn(module.Domain!)
                .Should()
                .NotHaveDependencyOnAny([.. forbidden])
                .GetResult();

            AssertRule(result, $"{module.Name}.Domain");
        }
    }

    [Fact]
    public void Application_does_not_depend_on_entity_framework_asp_net_core_or_infrastructure()
    {
        foreach (var module in Modules)
        {
            var forbidden = new List<string>
            {
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
            };

            forbidden.AddRange(Modules.Select(other => $"SmartRail.{other.Name}.Infrastructure"));
            forbidden.AddRange(Modules.Select(other => $"SmartRail.{other.Name}.Api"));
            forbidden.AddRange(OtherModuleLayers(module.Name));

            var result = TypesIn(module.Application)
                .Should()
                .NotHaveDependencyOnAny([.. forbidden])
                .GetResult();

            AssertRule(result, $"{module.Name}.Application");
        }
    }

    [Fact]
    public void Api_does_not_depend_on_infrastructure_or_entity_framework()
    {
        foreach (var module in Modules)
        {
            var forbidden = new List<string>
            {
                "Microsoft.EntityFrameworkCore",
            };

            forbidden.AddRange(Modules.Select(other => $"SmartRail.{other.Name}.Infrastructure"));

            var result = TypesIn(module.Api)
                .Should()
                .NotHaveDependencyOnAny([.. forbidden])
                .GetResult();

            AssertRule(result, $"{module.Name}.Api");
        }
    }

    [Fact]
    public void A_module_does_not_depend_on_another_modules_layers()
    {
        foreach (var module in Modules)
        {
            var otherLayers = OtherModuleLayers(module.Name);

            foreach (var (layer, assembly) in module.Layers())
            {
                var result = TypesIn(assembly)
                    .Should()
                    .NotHaveDependencyOnAny(otherLayers)
                    .GetResult();

                AssertRule(result, $"{module.Name}.{layer}");
            }
        }
    }

    [Fact]
    public void Every_entity_lives_in_a_domain_assembly()
    {
        var entities = Types.InAssemblies(Modules.SelectMany(module => module.Layers().Select(layer => layer.Assembly)))
            .That()
            .Inherit(typeof(Entity<>))
            .And()
            .DoNotHaveName(PendingMoves)
            .GetTypes();

        Assert.NotEmpty(entities);

        foreach (var entity in entities)
        {
            var assemblyName = entity.Assembly.GetName().Name;

            Assert.True(
                assemblyName?.EndsWith(".Domain", StringComparison.Ordinal) == true,
                $"{entity.FullName} derives from Entity<> but lives in '{assemblyName}', not a Domain assembly.");
        }
    }

    private static PredicateList TypesIn(Assembly assembly) =>
        Types.InAssembly(assembly).That().DoNotHaveName(PendingMoves);

    private static string[] OtherModuleLayers(string moduleName) =>
        [.. Modules
            .Where(other => !string.Equals(other.Name, moduleName, StringComparison.Ordinal))
            .SelectMany(other => new[]
            {
                $"SmartRail.{other.Name}.Domain",
                $"SmartRail.{other.Name}.Application",
                $"SmartRail.{other.Name}.Infrastructure",
                $"SmartRail.{other.Name}.Api",
            })];

    private static Assembly Domain(string moduleName) => Assembly.Load($"SmartRail.{moduleName}.Domain");

    private static void AssertRule(TestResult result, string subject)
    {
        var failing = result.FailingTypeNames is { } names ? string.Join(", ", names) : "unknown";

        Assert.True(
            result.IsSuccessful,
            $"{subject} breaks a dependency rule (design D5). Offending types: {failing}");
    }

    private sealed record ModuleLayers(
        string Name,
        Assembly? Domain,
        Assembly Application,
        Assembly Infrastructure,
        Assembly Api)
    {
        public IEnumerable<(string Layer, Assembly Assembly)> Layers()
        {
            if (Domain is not null)
            {
                yield return ("Domain", Domain);
            }

            yield return ("Application", Application);
            yield return ("Infrastructure", Infrastructure);
            yield return ("Api", Api);
        }
    }
}
