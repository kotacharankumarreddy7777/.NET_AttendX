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

string holidaysSql = File.ReadAllText("seed_holidays.sql");
using (var cmd = new NpgsqlCommand(holidaysSql, conn))
{
    cmd.ExecuteNonQuery();
}

using var countCmd = new NpgsqlCommand("SELECT count(*) FROM \"Holidays\"", conn);
int count = (int)countCmd.ExecuteScalar();
Console.WriteLine($"Holidays in Neon: {count}");
