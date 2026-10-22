namespace SmartRail.SharedKernel.Application.Abstractions;

/// <summary>
/// The caller a request runs as, exposed to every module's handlers so a change can be attributed
/// without a reference to Identity (design D3). The implementation lives in the Identity Api
/// project and reads the claims the staff scheme produced; a module only ever sees this port.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The caller's SmartRail staff account, or <see langword="null"/> when there is none.</summary>
    Guid? StaffId { get; }

    /// <summary>The caller's catalogue role, or <see langword="null"/> when it has none.</summary>
    string? Role { get; }
}
