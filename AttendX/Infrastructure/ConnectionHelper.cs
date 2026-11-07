namespace AttendX.Infrastructure;

public static class ConnectionHelper
{
    public static string ConvertToNpgsqlConnectionString(string connStr)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return connStr;
        if (!connStr.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connStr.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connStr;
        }

        try
        {
            var uri = new Uri(connStr);
            var userInfo = uri.UserInfo.Split(':', 2);
            var user = Uri.UnescapeDataString(userInfo[0]);
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 5432;
            var db = uri.AbsolutePath.TrimStart('/');

            var npgsqlSb = new Npgsql.NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = db,
                Username = user,
                Password = pass,
                SslMode = Npgsql.SslMode.Require
            };

            if (!string.IsNullOrEmpty(uri.Query))
            {
                var query = uri.Query.TrimStart('?');
                foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        var k = Uri.UnescapeDataString(kv[0]);
                        var v = Uri.UnescapeDataString(kv[1]);
                        if (k.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                        {
                            if (Enum.TryParse<Npgsql.SslMode>(v, true, out var ssl))
                                npgsqlSb.SslMode = ssl;
                        }
                        else if (k.Equals("pooling", StringComparison.OrdinalIgnoreCase) && bool.TryParse(v, out var pool))
                        {
                            npgsqlSb.Pooling = pool;
                        }
                    }
                }
            }

            return npgsqlSb.ConnectionString;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse database connection URI '{connStr}': {ex.Message}", ex);
        }
    }
}
