using Relay.Application.Abstractions;
using Relay.Domain;

namespace Relay.Application.Testing;

/// <summary>An in-memory <see cref="IAccountMetadataReader"/>, paired with <see cref="StubDashboardReader"/>.</summary>
public sealed class StubAccountMetadataReader : IAccountMetadataReader
{
    private readonly Dictionary<int, AccountMeta> _byAccount = new();

    public StubAccountMetadataReader Seed(int accountId, AccountMeta meta)
    {
        _byAccount[accountId] = meta;
        return this;
    }

    public Task<AccountMeta?> ReadAsync(int accountId, WeekRange? week, CancellationToken ct) =>
        Task.FromResult(_byAccount.GetValueOrDefault(accountId));
}
