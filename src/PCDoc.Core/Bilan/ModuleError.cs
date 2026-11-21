namespace PCDoc.Core.Bilan;

/// <summary>Un module dont l'analyse a échoué ou n'a pas répondu à temps.</summary>
public sealed record ModuleError(string Module, string Message);
