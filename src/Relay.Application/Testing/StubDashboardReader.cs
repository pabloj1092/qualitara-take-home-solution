using Relay.Application.Abstractions;

namespace Relay.Application.Testing;

/// <summary>
/// An in-memory <see cref="IDashboardReader"/>. Exercises the whole assembly path — validation,
/// orchestration, baseline, status, DTO, JSON — before the database exists, and is reused
/// (not thrown away) as the fake behind <c>DashboardQueryServiceTests</c>.
/// </summary>
public sealed class StubDashboardReader : IDashboardReader
{
    private readonly Dictionary<int, DashboardReadModel> _byAccount = new();

    public StubDashboardReader Seed(int accountId, DashboardReadModel model)
    {
        _byAccount[accountId] = model;
        return this;
    }

    public Task<DashboardReadModel?> ReadAsync(DashboardQuery query, CancellationToken ct) =>
        Task.FromResult(_byAccount.GetValueOrDefault(query.AccountId));
}
