namespace FixFinder.Core.Sources;

/// <summary>
/// Maps a package or module name to the GitHub repository that hosts it.
/// </summary>
/// <remarks>
/// A fixed table of about forty entries, not a resolver. Working out the repository for an
/// arbitrary package means querying npm, PyPI, NuGet, crates.io, pkg.go.dev, Maven Central and
/// RubyGems, following each one's metadata to a source URL that is frequently absent, wrong, or
/// a redirect to somewhere that is not GitHub at all. That is a project in itself, and it would
/// spend network requests on every search to sometimes improve one.
/// <para>
/// What this table buys is the case that actually matters: when the crash is inside a library
/// this well known, confining the search to that library's own issue tracker turns a page of
/// vaguely related results into the handful of people who hit the same bug in the same code.
/// When there is no entry, the search simply runs unconfined, which is the normal path.
/// </para>
/// </remarks>
public static class KnownRepoMap
{
    private static readonly Dictionary<string, string> Repositories = new(StringComparer.OrdinalIgnoreCase)
    {
        // .NET
        ["Newtonsoft.Json"] = "JamesNK/Newtonsoft.Json",
        ["AutoMapper"] = "AutoMapper/AutoMapper",
        ["Serilog"] = "serilog/serilog",
        ["Dapper"] = "DapperLib/Dapper",
        ["Polly"] = "App-vNext/Polly",
        ["FluentValidation"] = "FluentValidation/FluentValidation",
        ["MediatR"] = "jbogard/MediatR",
        ["xunit"] = "xunit/xunit",
        ["Moq"] = "devlooped/moq",
        ["EntityFrameworkCore"] = "dotnet/efcore",
        ["Microsoft.EntityFrameworkCore"] = "dotnet/efcore",

        // Python
        ["requests"] = "psf/requests",
        ["numpy"] = "numpy/numpy",
        ["pandas"] = "pandas-dev/pandas",
        ["django"] = "django/django",
        ["flask"] = "pallets/flask",
        ["sqlalchemy"] = "sqlalchemy/sqlalchemy",
        ["pydantic"] = "pydantic/pydantic",
        ["urllib3"] = "urllib3/urllib3",
        ["pytest"] = "pytest-dev/pytest",
        ["boto3"] = "boto/boto3",
        ["fastapi"] = "fastapi/fastapi",

        // JavaScript and Node
        ["express"] = "expressjs/express",
        ["react"] = "facebook/react",
        ["axios"] = "axios/axios",
        ["lodash"] = "lodash/lodash",
        ["webpack"] = "webpack/webpack",
        ["vite"] = "vitejs/vite",
        ["next"] = "vercel/next.js",
        ["typescript"] = "microsoft/TypeScript",
        ["jest"] = "jestjs/jest",
        ["mongoose"] = "Automattic/mongoose",

        // Java
        ["com.fasterxml.jackson"] = "FasterXML/jackson-databind",
        ["jackson"] = "FasterXML/jackson-databind",
        ["org.springframework"] = "spring-projects/spring-framework",
        ["spring"] = "spring-projects/spring-framework",
        ["org.hibernate"] = "hibernate/hibernate-orm",
        ["hibernate"] = "hibernate/hibernate-orm",

        // Go, Rust, Ruby
        ["gin-gonic/gin"] = "gin-gonic/gin",
        ["gorm.io/gorm"] = "go-gorm/gorm",
        ["serde"] = "serde-rs/serde",
        ["tokio"] = "tokio-rs/tokio",
        ["reqwest"] = "seanmonstar/reqwest",
        ["clap"] = "clap-rs/clap",
        ["rails"] = "rails/rails",
        ["nokogiri"] = "sparklemotion/nokogiri",
    };

    public static int Count => Repositories.Count;

    /// <summary>
    /// Looks up a module name, tolerating the forms different runtimes print.
    /// </summary>
    /// <remarks>
    /// Tries the name as given, then progressively less of it, because what arrives here is
    /// whatever the stack trace called the module: Java prints a full package path, Go prints an
    /// import path with a host on the front, and .NET prints a dotted assembly name.
    /// </remarks>
    public static string? Resolve(string? module)
    {
        if (string.IsNullOrWhiteSpace(module)) return null;

        var name = module.Trim().TrimEnd('/');

        if (Repositories.TryGetValue(name, out var direct)) return direct;

        // github.com/gin-gonic/gin -> gin-gonic/gin
        if (name.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = name["github.com/".Length..].Split('/');
            if (parts.Length >= 2) return $"{parts[0]}/{parts[1]}";
        }

        // org.springframework.beans.factory -> org.springframework -> springframework
        var segments = name.Split('.');

        for (var take = segments.Length; take >= 1; take--)
        {
            var prefix = string.Join('.', segments[..take]);
            if (Repositories.TryGetValue(prefix, out var byPrefix)) return byPrefix;
        }

        return Repositories.TryGetValue(segments[0], out var first) ? first : null;
    }
}
