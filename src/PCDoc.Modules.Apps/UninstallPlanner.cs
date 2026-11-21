using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.Apps;

public enum UninstallMethod
{
    Msi,
    Silent,
    Interactive,
    Store,
}

/// <summary>Comment désinstaller : MSI silencieux, désinstalleur silencieux, fenêtre de l'éditeur, ou paquet du Store.</summary>
public sealed record UninstallPlan(UninstallMethod Method, string? Executable = null, string Arguments = "");

/// <summary>Choisit la méthode de désinstallation la plus silencieuse possible.</summary>
public static class UninstallPlanner
{
    private const string InnoSilent = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";

    /// <summary>
    /// Null si la désinstallation n'est pas proposée : programme installé pour l'utilisateur seul (il faudrait le lancer
    /// sans élévation), désinstalleur hors des dossiers réservés aux administrateurs, ou aucune commande.
    /// </summary>
    public static UninstallPlan? Plan(InstalledApp app, IUninstallLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(launcher);
        if (app.Kind == AppKind.Store)
        {
            return app.NonRemovable || app.IsFramework
                   || !(StoreAppReader.IsSafeName(app.PackageFullName) || StoreAppReader.IsSafeName(app.ProvisionedName))
                ? null
                : new UninstallPlan(UninstallMethod.Store);
        }

        if (app.Scope == AppScope.User)
        {
            return null;
        }

        if (app.ProductCode is not null)
        {
            return new UninstallPlan(UninstallMethod.Msi, "msiexec.exe", $"/x {app.ProductCode} /qn /norestart");
        }

        var command = Split(app.UninstallString);
        bool Trusted((string Exe, string Args)? c) => c is { } x && launcher.IsTrusted(x.Exe) && launcher.AreArgumentsTrusted(x.Args);

        // Inno Setup écrit QuietUninstallString avec /SILENT (fenêtre de progression) : /VERYSILENT passe avant.
        if (app.RegistryPath?.EndsWith("_is1", StringComparison.OrdinalIgnoreCase) == true && Trusted(command))
        {
            return new UninstallPlan(UninstallMethod.Silent, command!.Value.Exe, Join(command.Value.Args, InnoSilent));
        }

        if (Split(app.QuietUninstallString) is { } quiet && Trusted(quiet))
        {
            return new UninstallPlan(UninstallMethod.Silent, quiet.Exe, quiet.Args);
        }

        return Trusted(command) ? new UninstallPlan(UninstallMethod.Interactive, command!.Value.Exe, command.Value.Args) : null;
    }

    /// <summary>Sépare l'exécutable (entre guillemets, ou jusqu'au premier « .exe ») de ses arguments.</summary>
    internal static (string Exe, string Args)? Split(string? commandLine)
    {
        var text = commandLine?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return end <= 1 ? null : (text[1..end], text[(end + 1)..].Trim());
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe < 0 ? null : (text[..(exe + 4)], text[(exe + 4)..].Trim());
    }

    private static string Join(string args, string extra)
    {
        var kept = string.Join(' ', args.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(a => !a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase) && !a.Equals("/VERYSILENT", StringComparison.OrdinalIgnoreCase)));
        return (kept + " " + extra).Trim();
    }
}
