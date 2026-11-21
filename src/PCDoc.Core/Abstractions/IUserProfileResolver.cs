namespace PCDoc.Core.Abstractions;

/// <summary>Le profil de la personne devant le PC (spec v2 §3.2).</summary>
/// <param name="IsProcessUser">Vrai si le profil cible est aussi le compte qui exécute PCDoc.</param>
public sealed record UserProfile(
    string Sid,
    string AccountName,
    string ProfilePath,
    string RoamingAppData,
    string LocalAppData,
    bool IsProcessUser);

/// <summary>Trouve l'utilisateur de la session ouverte, même si PCDoc tourne sous un autre compte administrateur.</summary>
public interface IUserProfileResolver
{
    UserProfile Resolve();
}
