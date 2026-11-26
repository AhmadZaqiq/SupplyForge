using System.Threading;
using System.Threading.Tasks;
using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.PlatformCore.Infrastructure.Services;

/// <summary>
/// Resolves the current <see cref="ICurrentBranchScope"/> for a caller.
/// Extracted so <see cref="HttpBranchScopeResolver"/> can be unit-tested
/// without a real <see cref="PlatformCore.Infrastructure.Data.PlatformCoreDbContext"/> (A1-4 Part 2).
/// </summary>
public interface IBranchScopeProvider
{
    /// <summary>
    /// Builds the scope for <paramref name="userId"/>.
    /// </summary>
    Task<ICurrentBranchScope> ResolveAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
