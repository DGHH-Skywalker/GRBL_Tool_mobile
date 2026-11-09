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
        command.CommandText = "CREATE TABLE IF NOT EXISTS executions(id TEXT PRIMARY KEY, state TEXT NOT NULL, error TEXT); CREATE TABLE IF NOT EXISTS start_commands(id TEXT PRIMARY KEY, fingerprint TEXT NOT NULL, execution_id TEXT NOT NULL);";
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
    public bool IsDuplicateStart(Guid commandId, string fingerprint)
    {
        using var c = Open(); using var command = c.CreateCommand();
        command.CommandText = "SELECT fingerprint FROM start_commands WHERE id=$id";
        command.Parameters.AddWithValue("$id", commandId.ToString());
        var existing = command.ExecuteScalar() as string;
        if (existing is not null && existing != fingerprint) throw new InvalidOperationException("重复命令 ID 的任务或参数不一致");
        return existing is not null;
    }
    public void RecordStart(Guid commandId, string fingerprint, ExecutionSnapshot snapshot)
    {
        using var c = Open(); using var transaction = c.BeginTransaction();
        using var command = c.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO start_commands VALUES($id,$fingerprint,$execution); INSERT INTO executions VALUES($execution,$state,NULL)";
        command.Parameters.AddWithValue("$id", commandId.ToString()); command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$execution", snapshot.ExecutionId.ToString()); command.Parameters.AddWithValue("$state", snapshot.State.ToString());
        command.ExecuteNonQuery(); transaction.Commit();
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
