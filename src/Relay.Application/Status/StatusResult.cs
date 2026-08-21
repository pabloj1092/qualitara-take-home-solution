using Relay.Domain;

namespace Relay.Application.Status;

public sealed record StatusResult(TileStatus Status, ReasonCode Reason, decimal? DeltaPct, decimal? DeltaPp);
