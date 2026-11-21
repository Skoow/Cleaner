using Serilog;
using Serilog.Core;

namespace PCDoc.Infrastructure;

/// <summary>Journal texte lisible : un fichier par jour, 14 jours conservés.</summary>
public static class Logging
{
    public static Logger CreateFileLogger(string logsDir)
    {
        Directory.CreateDirectory(logsDir);
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logsDir, "pcdoc-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();
    }
}
