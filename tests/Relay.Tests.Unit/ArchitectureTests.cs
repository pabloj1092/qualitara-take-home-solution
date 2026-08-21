using Relay.Application.Baseline;

namespace Relay.Tests.Unit;

/// <summary>
/// The reason the read port exists: without this guard, <c>DashboardQueryService</c> could drift
/// back to taking a <c>DbContext</c> directly and the unit suite would quietly acquire a database
/// dependency.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void ApplicationAssembly_DoesNotReferenceEfCoreOrInfrastructure()
    {
        var applicationAssembly = typeof(BaselineService).Assembly;
        var referencedNames = applicationAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)
            .ToList();

        Assert.DoesNotContain(
            referencedNames, name => name!.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            referencedNames, name => name!.Contains("Relay.Infrastructure", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            referencedNames, name => name!.Contains("Npgsql", StringComparison.OrdinalIgnoreCase));
    }
}
