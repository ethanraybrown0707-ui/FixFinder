namespace FixFinder.Core.Execution.Libraries;

/// <summary>A library as Maven names it: the group that publishes it, the artifact, and one version of it.</summary>
/// <param name="Classifier">Which of the artifact's files - "tests", "sources" - when it is not the main one.</param>
public sealed record LibraryName(string Group, string Artifact, string Version, string Classifier = "")
{
    /// <summary>What every version of this library has in common: a build chooses one version for each.</summary>
    public string Key => Classifier.Length > 0 ? $"{Group}:{Artifact}:{Classifier}" : $"{Group}:{Artifact}";

    public override string ToString() => Classifier.Length > 0 ? $"{Group}:{Artifact}:{Version}:{Classifier}" : $"{Group}:{Artifact}:{Version}";
}

/// <summary>A library the program declares that is not on this computer, and where it was looked for.</summary>
public sealed record MissingLibrary(string Name, string Reason)
{
    public override string ToString() => $"{Name} ({Reason})";
}
