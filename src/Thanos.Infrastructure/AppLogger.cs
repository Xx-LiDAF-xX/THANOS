namespace Thanos.Infrastructure;

public sealed class AppLogger(LocalPlatformService platform)
{
    public void Write(string subsystem, string message)
    {
        try
        {
            var directory = Path.Combine(platform.ApplicationDataDirectory, "logs");
            Directory.CreateDirectory(directory);
            var line = System.Text.Json.JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, subsystem, message });
            File.AppendAllText(Path.Combine(directory, "app.jsonl"), line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging failures must not discard collected evidence.
            System.Diagnostics.Trace.WriteLine("THANOS application log could not be written.");
        }
    }
}
