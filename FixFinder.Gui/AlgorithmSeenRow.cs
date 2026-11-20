using FixFinder.Core.Analysis.Recognition;

namespace FixFinder.Gui;

/// <summary>An algorithm recognised in the code, as the window shows it: which function and line, what it looks like, and why.</summary>
public sealed class AlgorithmSeenRow(AlgorithmSeen seen)
{
    public string Title =>
        $"{(seen.Function == AlgorithmRecognition.TopLevelCode ? "The top-level code" : $"`{seen.Function}`")}, line {seen.Line}: {seen.Algorithm}";

    public string HowItWorks => char.ToUpperInvariant(seen.HowItWorks[0]) + seen.HowItWorks[1..] + ".";

    public string WhatToKnow => seen.WhatToKnow;

    /// <summary>What calling it on lists of FixFinder's own showed, when it was called.</summary>
    public string Tried => seen.Tried ?? "";

    public bool HasTried => seen.Tried is not null;
}
