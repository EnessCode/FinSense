using FinSense.Domain.Entities;
using FluentAssertions;

namespace FinSense.Application.UnitTests;

public class ArchitectureTests
{
    [Fact]
    public void DomainAssembly_ShouldHaveZeroExternalNuGetDependencies()
    {
        var domainAssembly = typeof(User).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies();

        referencedAssemblies.Should().OnlyContain(
            a => a.Name != null && (a.Name.StartsWith("System") || a.Name == "netstandard" || a.Name == "mscorlib"),
            "FinSense.Domain must remain pure C# with zero external NuGet dependencies.");
    }
}
