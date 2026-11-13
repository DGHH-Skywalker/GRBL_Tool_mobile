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
        using var schema = connection.CreateCommand(); schema.CommandText = "PRAGMA table_info(executions)";
        var columns = new HashSet<string>();
        using (var reader = schema.ExecuteReader()) { while (reader.Read()) columns.Add(reader.GetString(1)); }
        foreach (var (name, type) in new[] { ("job_name", "TEXT"), ("hash", "TEXT"), ("parameters", "TEXT"), ("started_at", "TEXT"), ("updated_at", "TEXT"), ("acknowledged", "INTEGER NOT NULL DEFAULT 0"), ("total", "INTEGER NOT NULL DEFAULT 0") })
        {
            if (columns.Contains(name)) continue;
            using var migration = connection.CreateCommand(); migration.CommandText = $"ALTER TABLE executions ADD COLUMN {name} {type}"; migration.ExecuteNonQuery();
        }
    }
    private SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    public void Save(ExecutionSnapshot snapshot)
    {
        using var c = Open(); using var command = c.CreateCommand();
        command.CommandText = "INSERT INTO executions(id,state,error,updated_at,acknowledged,total) VALUES($id,$state,$error,$updated,$ack,$total) ON CONFLICT(id) DO UPDATE SET state=$state,error=$error,updated_at=$updated,acknowledged=$ack,total=$total";
        command.Parameters.AddWithValue("$id", snapshot.ExecutionId.ToString());
        command.Parameters.AddWithValue("$state", snapshot.State.ToString());
        command.Parameters.AddWithValue("$error", (object?)snapshot.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$ack", snapshot.AcknowledgedLines); command.Parameters.AddWithValue("$total", snapshot.TotalLines);
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
    public void RecordStart(Guid commandId, string fingerprint, ExecutionSnapshot snapshot, StoredJob? job = null)
    {
        using var c = Open(); using var transaction = c.BeginTransaction();
        using var command = c.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO start_commands VALUES($id,$fingerprint,$execution); INSERT INTO executions(id,state,error,job_name,hash,parameters,started_at,updated_at,acknowledged,total) VALUES($execution,$state,NULL,$name,$hash,$parameters,$time,$time,0,$total)";
        command.Parameters.AddWithValue("$id", commandId.ToString()); command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$execution", snapshot.ExecutionId.ToString()); command.Parameters.AddWithValue("$state", snapshot.State.ToString());
        command.Parameters.AddWithValue("$name", (object?)job?.Name ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", (object?)job?.Hash ?? DBNull.Value);
        command.Parameters.AddWithValue("$parameters", (object?)job?.Parameters ?? DBNull.Value);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$total", snapshot.TotalLines);
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
        command.CommandText = "SELECT id,state,error,job_name,hash,parameters,started_at,updated_at,acknowledged,total FROM executions ORDER BY rowid DESC LIMIT 100";
        using var reader = command.ExecuteReader(); var entries = new List<HistoryEntry>();
        while (reader.Read()) entries.Add(new(Guid.Parse(reader.GetString(0)), Enum.Parse<ExecutionState>(reader.GetString(1)), reader.IsDBNull(2) ? null : reader.GetString(2))
        {
            JobName = reader.IsDBNull(3) ? null : reader.GetString(3), Hash = reader.IsDBNull(4) ? null : reader.GetString(4),
            Parameters = reader.IsDBNull(5) ? null : reader.GetString(5), StartedAt = reader.IsDBNull(6) ? null : reader.GetString(6),
            UpdatedAt = reader.IsDBNull(7) ? null : reader.GetString(7), AcknowledgedLines = reader.GetInt32(8), TotalLines = reader.GetInt32(9)
        });
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
