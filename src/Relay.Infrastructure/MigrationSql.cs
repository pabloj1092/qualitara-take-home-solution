using System.Reflection;

namespace Relay.Infrastructure;

/// <summary>Loads an embedded <c>.sql</c> resource from Migrations/Sql by file name.</summary>
internal static class MigrationSql
{
    public static string Load(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Relay.Infrastructure.Migrations.Sql.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
