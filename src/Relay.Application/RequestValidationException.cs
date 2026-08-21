namespace Relay.Application;

/// <summary>
/// A request-level validation failure that depends on account state (unknown location name, a
/// week or window outside the account's actual data range) — the checks that can only happen
/// once the account's metadata has been read. Mapped to a 400 with an actionable message by the
/// API's exception handling middleware.
/// </summary>
public sealed class RequestValidationException(string parameterName, string message) : Exception(message)
{
    public string ParameterName { get; } = parameterName;
}
