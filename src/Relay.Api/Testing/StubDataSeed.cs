using Relay.Application.Abstractions;
using Relay.Application.Testing;
using Relay.Domain;

namespace Relay.Api.Testing;

/// <summary>
/// A synthetic but structurally accurate payload for account 6, registered behind
/// <c>UseStubDashboardReader</c> so the whole assembly path (validation → orchestrator → baseline
/// → status → DTO → JSON) can be exercised with a real <c>curl</c> before Stage 2's migration and
/// database exist.
/// </summary>
public static class StubDataSeed
{
    private static readonly WeekRange ViewedWeek = WeekRange.FromIsoWeek("2026-W30");

    public static (StubDashboardReader Dashboard, StubAccountMetadataReader Metadata) Build()
    {
        var locations = new List<LocationInfo>
        {
            new(1, "Site A", null, null),
            new(2, "Site B", null, null),
        };

        var metadata = new StubAccountMetadataReader().Seed(
            6,
            new AccountMeta(
                6, "Metro Collision Centers", "America/New_York", locations,
                ViewedWeek.Preceding(20).First(), ViewedWeek, ViewedWeek,
                MaxWindowForWeek: 20, DefaultWindow: 8, ThresholdSet.Defaults));

        var weeks = ViewedWeek.Preceding(8).Append(ViewedWeek).ToList();

        var tiles = new List<TileSeries>
        {
            BuildTile("call_received", "Calls received", 1, weeks, baseValue: 40, null, 0, OutcomePolarity.Good),
            BuildTile("call_received", "Calls received", 1, weeks, baseValue: 27, "connected", 1, OutcomePolarity.Good, 40),
            BuildTile("call_received", "Calls received", 1, weeks, baseValue: 8, "missed", 2, OutcomePolarity.Bad, 40),
            BuildTile("call_received", "Calls received", 1, weeks, baseValue: 5, "voicemail", 3, OutcomePolarity.Neutral, 40),
            BuildTile("lead_created", "Leads created", 2, weeks, baseValue: 12, null, 0, OutcomePolarity.Good),
            BuildTile("lead_created", "Leads created", 2, weeks, baseValue: 5, "converted", 1, OutcomePolarity.Good, 12),
            BuildTile("lead_created", "Leads created", 2, weeks, baseValue: 7, "open", 2, OutcomePolarity.Neutral, 12),
            BuildTile("appointment_set", "Appointments set", 3, weeks, baseValue: 6, null, 0, OutcomePolarity.Good),
            BuildTile("appointment_set", "Appointments set", 3, weeks, baseValue: 5, "completed", 1, OutcomePolarity.Good, 6),
            BuildTile("appointment_set", "Appointments set", 3, weeks, baseValue: 1, "no_show", 2, OutcomePolarity.Bad, 6),
        };

        var dashboard = new StubDashboardReader().Seed(
            6,
            new DashboardReadModel(
                new AccountInfo(6, "Metro Collision Centers", "America/New_York"),
                locations,
                tiles,
                new DisclosureData(
                    NullOutcomeCount: 3,
                    Exclusions:
                    [
                        new ExclusionInfo(
                            new DateOnly(2026, 6, 3), new DateOnly(2026, 6, 3),
                            "D1 · replayed bulk backfill (audit 2026-08-20)", [new DateOnly(2026, 6, 1)]),
                    ])));

        return (dashboard, metadata);
    }

    private static TileSeries BuildTile(
        string eventType, string eventTypeDisplayName, int eventTypeSortOrder,
        IReadOnlyList<WeekRange> weeks, int baseValue, string? outcome, int outcomeSortOrder,
        OutcomePolarity polarity, int? denominatorBase = null)
    {
        var kind = outcome is null ? TileKind.Count : TileKind.Rate;
        var observations = weeks
            .Select((w, i) =>
            {
                var jitter = (i * 7) % 5 - 2;
                if (kind == TileKind.Count)
                {
                    return new WeekObservation(w.Start, Math.Max(0, baseValue + jitter), null, 7, 7);
                }

                var denominator = Math.Max(1, denominatorBase!.Value + jitter);
                var numerator = Math.Min(denominator, Math.Max(0, baseValue + jitter));
                var value = Math.Round(numerator * 100m / denominator, 2);
                return new WeekObservation(w.Start, value, denominator, 7, 7);
            })
            .ToList();

        var outcomeDisplayName = outcome switch
        {
            "connected" => "Connected",
            "missed" => "Missed",
            "voicemail" => "Voicemail",
            "converted" => "Converted",
            "open" => "Open",
            "completed" => "Completed",
            "no_show" => "No-show",
            _ => null,
        };

        return new TileSeries(
            new TileKey(eventType, outcome, kind),
            eventTypeDisplayName, eventTypeSortOrder, outcomeDisplayName, outcomeSortOrder, polarity, observations);
    }
}
