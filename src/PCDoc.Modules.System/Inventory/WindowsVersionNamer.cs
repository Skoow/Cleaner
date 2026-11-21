namespace PCDoc.Modules.System.Inventory;

/// <summary>
/// Le registre indique « Windows 10 » même sous Windows 11 : le vrai nom se déduit du build (≥ 22000 = Windows 11).
/// </summary>
public static class WindowsVersionNamer
{
    public const int FirstWindows11Build = 22000;

    public static string Name(int build) => build >= FirstWindows11Build ? "Windows 11" : "Windows 10";

    public static string Edition(string? productName, int build)
    {
        var name = Name(build);
        if (string.IsNullOrWhiteSpace(productName))
        {
            return name;
        }

        return productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)
            ? name + productName["Windows 10".Length..]
            : productName;
    }
}
