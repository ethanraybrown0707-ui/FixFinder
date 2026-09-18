namespace FixFinder.Core.Sources;

/// <summary>Maps a package or module name to the GitHub repository that hosts it.</summary>
public static class KnownRepoMap
{
    private static readonly Dictionary<string, string> Repositories = new(StringComparer.OrdinalIgnoreCase)
    {
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

        ["com.fasterxml.jackson"] = "FasterXML/jackson-databind",
        ["jackson"] = "FasterXML/jackson-databind",
        ["org.springframework"] = "spring-projects/spring-framework",
        ["spring"] = "spring-projects/spring-framework",
        ["org.hibernate"] = "hibernate/hibernate-orm",
        ["hibernate"] = "hibernate/hibernate-orm",

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

    public static string? Resolve(string? module)
    {
        if (string.IsNullOrWhiteSpace(module)) return null;

        var name = module.Trim().TrimEnd('/');

        if (Repositories.TryGetValue(name, out var direct)) return direct;

        if (name.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = name["github.com/".Length..].Split('/');
            if (parts.Length >= 2) return $"{parts[0]}/{parts[1]}";
        }

        var segments = name.Split('.');

        for (var take = segments.Length; take >= 1; take--)
        {
            var prefix = string.Join('.', segments[..take]);
            if (Repositories.TryGetValue(prefix, out var byPrefix)) return byPrefix;
        }

        return Repositories.TryGetValue(segments[0], out var first) ? first : null;
    }
}
