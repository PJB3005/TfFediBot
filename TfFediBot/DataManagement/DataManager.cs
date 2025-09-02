using Microsoft.Data.Sqlite;

namespace TfFediBot.DataManagement;

/// <summary>
/// Manages the watchdog's SQLite database, used to store all persisted data of the watchdog itself.
/// </summary>
public sealed class DataManager
{
    public SqliteConnection OpenConnection()
    {
        var con = new SqliteConnection(GetConnectionString());
        con.Open();
        return con;
    }

    private string GetConnectionString()
    {
        return "Data Source=data.db;Foreign Keys=True;";
    }

    public void Start()
    {
        using var con = OpenConnection();

        Migrator.Migrate(con, "TfFediBot.DataManagement.Migrations");
    }
}
