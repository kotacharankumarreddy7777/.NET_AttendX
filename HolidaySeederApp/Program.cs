using System;
using System.IO;
using Npgsql;

var builder = new NpgsqlConnectionStringBuilder
{
    Host = "ep-shy-rain-bek90206-pooler.c-9.us-east-2.aws.neon.tech",
    Port = 5432,
    Database = "neondb",
    Username = "neondb_owner",
    Password = "npg_eTVyOn6UR9Bb",
    SslMode = SslMode.Require,
    CommandTimeout = 600
};

using var conn = new NpgsqlConnection(builder.ConnectionString);
conn.Open();
Console.WriteLine("Connected to Neon");

string sqlPath = Path.Combine(Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.Parent?.FullName ?? ".", "seed_holidays.sql");
// Fallback search
if (!File.Exists(sqlPath))
{
    sqlPath = "../seed_holidays.sql";
}
if (!File.Exists(sqlPath))
{
    sqlPath = "seed_holidays.sql";
}

string sql = File.ReadAllText(sqlPath);
using var cmd = new NpgsqlCommand(sql, conn);
cmd.ExecuteNonQuery();

using var countCmd = new NpgsqlCommand("SELECT count(*) FROM \"Holidays\"", conn);
long count = (long)countCmd.ExecuteScalar()!;
Console.WriteLine($"Holidays in Neon: {count}");
