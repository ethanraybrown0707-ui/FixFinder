namespace FixFinder.Core.Parsing;

/// <summary>
/// The messages a language prints and carries on past, rather than stopping the program: PHP's warnings, notices and
/// deprecation notices, and Perl's warnings.
/// </summary>
public static class CarriedOnPast
{
    /// <summary>What the message was, in words - "a warning", "a notice", "a deprecation notice" - or null for one that stopped the program.</summary>
    public static string? WhatItWas(ParsedError error) => Parsers.PhpParser.CarriedOnPast(error) ?? Parsers.PerlParser.CarriedOnPast(error);
}
