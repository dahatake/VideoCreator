using Microsoft.Data.Sqlite;

namespace VideoCreator.Storage;

/// <summary>SQLite document store (PRD §20.4): one table, JSON documents keyed by kind/id with an optional parent.</summary>
public sealed class Store : IDisposable
{
    readonly string _cs;
    readonly object _gate = new();

    public Store(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL;");
        Exec(c, "CREATE TABLE IF NOT EXISTS docs (kind TEXT NOT NULL, id TEXT NOT NULL, parent TEXT, json TEXT NOT NULL, updated TEXT NOT NULL, PRIMARY KEY(kind,id));");
        Exec(c, "CREATE INDEX IF NOT EXISTS ix_docs_parent ON docs(kind,parent);");
    }

    SqliteConnection Open() { var c = new SqliteConnection(_cs); c.Open(); return c; }
    static void Exec(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    public void Put<T>(string kind, string id, string? parent, T doc)
    {
        lock (_gate)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO docs(kind,id,parent,json,updated) VALUES($k,$i,$p,$j,$u) ON CONFLICT(kind,id) DO UPDATE SET parent=$p,json=$j,updated=$u";
            cmd.Parameters.AddWithValue("$k", kind); cmd.Parameters.AddWithValue("$i", id);
            cmd.Parameters.AddWithValue("$p", (object?)parent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$j", Json.Serialize(doc)); cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public T? Get<T>(string kind, string id) where T : class
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT json FROM docs WHERE kind=$k AND id=$i";
        cmd.Parameters.AddWithValue("$k", kind); cmd.Parameters.AddWithValue("$i", id);
        return cmd.ExecuteScalar() is string s ? Json.Deserialize<T>(s) : null;
    }

    public List<T> List<T>(string kind, string? parent = null)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = parent == null ? "SELECT json FROM docs WHERE kind=$k ORDER BY updated DESC" : "SELECT json FROM docs WHERE kind=$k AND parent=$p ORDER BY updated DESC";
        cmd.Parameters.AddWithValue("$k", kind);
        if (parent != null) cmd.Parameters.AddWithValue("$p", parent);
        using var r = cmd.ExecuteReader(); var list = new List<T>();
        while (r.Read()) list.Add(Json.Deserialize<T>(r.GetString(0)));
        return list;
    }

    public List<string> Ids(string kind, string parent)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id FROM docs WHERE kind=$k AND parent=$p";
        cmd.Parameters.AddWithValue("$k", kind); cmd.Parameters.AddWithValue("$p", parent);
        using var r = cmd.ExecuteReader(); var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Deletes all the given documents in one transaction (all or nothing). Returns the number of rows removed.</summary>
    public int Delete(IEnumerable<(string Kind, string Id)> docs)
    {
        lock (_gate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM docs WHERE kind=$k AND id=$i";
            var k = cmd.Parameters.Add("$k", SqliteType.Text); var i = cmd.Parameters.Add("$i", SqliteType.Text);
            var n = 0;
            foreach (var (kind, id) in docs) { k.Value = kind; i.Value = id; n += cmd.ExecuteNonQuery(); }
            tx.Commit();
            return n;
        }
    }

    public void Dispose() => SqliteConnection.ClearAllPools();
}

public static class Kinds
{
    public const string Project = "project", Asset = "asset", Rights = "rights", Job = "job", Candidate = "candidate", EditPlan = "editplan", Render = "render", Bgm = "bgm";
}
