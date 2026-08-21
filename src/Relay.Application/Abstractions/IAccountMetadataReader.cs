using Relay.Domain;

namespace Relay.Application.Abstractions;

public interface IAccountMetadataReader
{
    /// <summary>
    /// Null return means the account does not exist. <paramref name="week"/> drives
    /// <see cref="AccountMeta.MaxWindowForWeek"/>, which shrinks as the week moves backwards; when
    /// omitted it defaults to <see cref="AccountMeta.LatestCompleteWeek"/>.
    /// </summary>
    Task<AccountMeta?> ReadAsync(int accountId, WeekRange? week, CancellationToken ct);
}
