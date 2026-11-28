namespace SupplyForge.PlatformCore.Contracts.Security;

/// <summary>
/// Central branch-scope guard (BRN-011…014). All module code that needs to verify
/// branch access should call this instead of inspecting <see cref="ICurrentBranchScope"/>
/// flags directly.
/// </summary>
public interface IBranchGuard
{
    /// <summary>
    /// Verifies that the current branch scope can access <paramref name="branchId"/>.
    /// Throws <see cref="BranchNotFoundException"/> if the warehouse does not exist or
    /// the caller's branch scope does not include it (BRN-011, BRN-012).
    /// </summary>
    /// <param name="branchId">The branch to check access for.</param>
    /// <exception cref="BranchNotFoundException">
    /// Thrown when the warehouse does not exist or the caller cannot access it.
    /// Mapped to HTTP 404 per BRN-011/BRN-012.
    /// </exception>
    void RequireAccess(Guid branchId);
}

/// <summary>
/// Thrown when a warehouse does not exist or the caller's branch scope does not
/// include it. Mapped to HTTP 404 per BRN-011/BRN-012.
/// </summary>
public class BranchNotFoundException : InvalidOperationException
{
    public BranchNotFoundException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when a resource is in branch scope but the caller lacks the function
/// permission for the action. Mapped to HTTP 403 per BRN-013.
/// </summary>
public class BranchAccessDeniedException : Exception
{
    public BranchAccessDeniedException(string? message = null)
        : base(message ?? "Branch access denied (BRN-013).")
    {
    }
}
