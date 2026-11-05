using NetArchTest.Rules;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// The one shared transaction across modules stays where the constitution allows it
/// (Principle I, as amended in 2.6.0; 020 research ST-04, ST-05).
/// </summary>
/// <remarks>
/// The parts of one site export or import share a connection and transaction; nothing else
/// may. A handler that joined a transfer session "because it was there" would quietly turn
/// the exception into a general way of writing across modules in one transaction.
/// </remarks>
public sealed class TransferSessionUsageTests
{
    private const string Enlistment = "QuickBuilder.Shared.Infrastructure.Data.TransferEnlistment";
    private const string SessionImplementation = "QuickBuilder.Sites.Features.Transfer.Engine.SiteTransferSession";
    private const string EngineNamespace = "QuickBuilder.Sites.Features.Transfer.Engine";

    [Fact]
    public void Only_archive_parts_and_the_transfer_engine_join_a_transfer_transaction()
    {
        var offenders = ModuleBoundaryTests.ModuleImplementations
            .SelectMany(assembly => Types.InAssembly(assembly)
                .That().DoNotResideInNamespace("Coverlet")
                .ShouldNot()
                .HaveDependencyOnAny(Enlistment, SessionImplementation)
                .GetResult()
                .FailingTypes ?? [])
            .Where(type => !IsAllowed(type))
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Only archive parts and the transfer engine may join a site transfer's transaction:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>A part (or the compiler's nested types of one), or the Sites transfer engine.</summary>
    private static bool IsAllowed(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current.Name.EndsWith("ArchivePart", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return (type.Namespace ?? string.Empty).StartsWith(EngineNamespace, StringComparison.Ordinal);
    }
}
