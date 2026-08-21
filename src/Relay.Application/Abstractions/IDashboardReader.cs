namespace Relay.Application.Abstractions;

/// <summary>
/// The database's job ends here: dense weekly observations, never an <see cref="IQueryable{T}"/>
/// and never an EF entity. Implemented by <c>Relay.Infrastructure</c>.
/// </summary>
public interface IDashboardReader
{
    /// <summary>Null return means the account does not exist.</summary>
    Task<DashboardReadModel?> ReadAsync(DashboardQuery query, CancellationToken ct);
}
