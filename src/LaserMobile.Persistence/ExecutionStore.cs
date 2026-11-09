using Microsoft.Data.Sqlite;
using LaserMobile.Domain;
namespace LaserMobile.Persistence;

public sealed class ExecutionStore
{
    private readonly string connectionString;
    public ExecutionStore(string databasePath)
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS executions(id TEXT PRIMARY KEY, state TEXT NOT NULL, error TEXT);";
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    public void Save(ExecutionSnapshot snapshot)
    {
        using var c = Open(); using var command = c.CreateCommand();
        command.CommandText = "INSERT INTO executions VALUES($id,$state,$error) ON CONFLICT(id) DO UPDATE SET state=$state,error=$error";
        command.Parameters.AddWithValue("$id", snapshot.ExecutionId.ToString());
        command.Parameters.AddWithValue("$state", snapshot.State.ToString());
        command.Parameters.AddWithValue("$error", (object?)snapshot.Error ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
    public int RecoverInterrupted()
    {
        using var c = Open(); using var command = c.CreateCommand();
        command.CommandText = "UPDATE executions SET state='Interrupted',error='Application restarted; explicit new execution required' WHERE state IN ('Running','Pausing','Paused','Draining','Cancelling')";
        return command.ExecuteNonQuery();
    }
    public IReadOnlyList<HistoryEntry> History()
    {
        using var c = Open(); using var command = c.CreateCommand();
        command.CommandText = "SELECT id,state,error FROM executions ORDER BY rowid DESC LIMIT 100";
        using var reader = command.ExecuteReader(); var entries = new List<HistoryEntry>();
        while (reader.Read()) entries.Add(new(Guid.Parse(reader.GetString(0)), Enum.Parse<ExecutionState>(reader.GetString(1)), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return entries;
    }
    public ExecutionState? ReadState(Guid id)
    {
        using var c = Open(); using var command = c.CreateCommand();
        command.CommandText = "SELECT state FROM executions WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        return command.ExecuteScalar() is string state ? Enum.Parse<ExecutionState>(state) : null;
    }
}
