using DotNetEnv;

namespace Ecommerce.API.Extensions;

internal static class EnvLoader
{
    public static void LoadLocalDotEnv()
    {
        var path = FindEnvFile();
        if (path is null)
            return;

        Env.Load(path);
        ApplyPostgresConnectionFromPassword();
    }

    private static string? FindEnvFile()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static void ApplyPostgresConnectionFromPassword()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")))
            return;

        var pwd = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
        if (string.IsNullOrWhiteSpace(pwd))
            return;

        // Defaults target db-local (compose host port 5434) — Docker API still uses service "db" on internal network.
        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5434";
        var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "InternProjectDb_Local";
        var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            $"Host={host};Port={port};Database={database};Username={user};Password={pwd}");
    }
}
