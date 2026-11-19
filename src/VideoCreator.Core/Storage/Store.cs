using Microsoft.Data.Sqlite;

namespace VideoCreator.Storage;

/// <summary>SQLite document store (PRD §20.4): one table, JSON documents keyed by kind/id with an optional parent.</summary>
public sealed class Store : IDisposable
{
    readonly string _cs;
    readonly object _gate = new();

    /// <summary>Serialize a document check and its writes/queue reservation without an intervening edit.</summary>
    public T Atomic<T>(Func<T> action) { lock (_gate) return action(); }

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

    /// <summary>Raised after any document was written or removed (on the writer's thread, outside the lock); used to keep project snapshots current.</summary>
    public event Action? Changed;
    void RaiseChanged() { try { Changed?.Invoke(); } catch (Exception) { /* a listener must never fail a write */ } }

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
        RaiseChanged();
    }

    /// <summary>Every document with its parent and update time (the whole store; snapshots filter it per project).</summary>
    public List<DocRow> Rows()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT kind,id,parent,json,updated FROM docs";
        using var r = cmd.ExecuteReader(); var list = new List<DocRow>();
        while (r.Read()) list.Add(new(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
    }

    /// <summary>Adds documents that do not exist yet, keeping their update times, in one transaction. Existing documents are never overwritten. Returns the number added.</summary>
    public int Import(IEnumerable<DocRow> docs)
    {
        int n;
        lock (_gate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO docs(kind,id,parent,json,updated) VALUES($k,$i,$p,$j,$u)";
            var k = cmd.Parameters.Add("$k", SqliteType.Text); var i = cmd.Parameters.Add("$i", SqliteType.Text); var p = cmd.Parameters.Add("$p", SqliteType.Text);
            var j = cmd.Parameters.Add("$j", SqliteType.Text); var u = cmd.Parameters.Add("$u", SqliteType.Text);
            n = 0;
            foreach (var d in docs) { k.Value = d.Kind; i.Value = d.Id; p.Value = (object?)d.Parent ?? DBNull.Value; j.Value = d.Json; u.Value = d.Updated; n += cmd.ExecuteNonQuery(); }
            tx.Commit();
        }
        if (n > 0) RaiseChanged();
        return n;
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

    /// <summary>Every document as raw JSON (kind, id, json) — for whole-store maintenance such as path rewrites.</summary>
    public List<(string Kind, string Id, string Json)> RawAll()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT kind,id,json FROM docs";
        using var r = cmd.ExecuteReader(); var list = new List<(string, string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    /// <summary>
    /// Replaces the JSON of existing documents in one transaction (all or nothing). The <c>updated</c> time is kept,
    /// so "newest first" orderings (latest export, project list) are not disturbed by maintenance.
    /// </summary>
    public int ReplaceJson(IEnumerable<(string Kind, string Id, string Json)> docs)
    {
        int n;
        lock (_gate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "UPDATE docs SET json=$j WHERE kind=$k AND id=$i";
            var k = cmd.Parameters.Add("$k", SqliteType.Text); var i = cmd.Parameters.Add("$i", SqliteType.Text); var j = cmd.Parameters.Add("$j", SqliteType.Text);
            n = 0;
            foreach (var (kind, id, json) in docs) { k.Value = kind; i.Value = id; j.Value = json; n += cmd.ExecuteNonQuery(); }
            tx.Commit();
        }
        if (n > 0) RaiseChanged();
        return n;
    }

    /// <summary>Deletes all the given documents in one transaction (all or nothing). Returns the number of rows removed.</summary>
    public int Delete(IEnumerable<(string Kind, string Id)> docs)
    {
        int n;
        lock (_gate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM docs WHERE kind=$k AND id=$i";
            var k = cmd.Parameters.Add("$k", SqliteType.Text); var i = cmd.Parameters.Add("$i", SqliteType.Text);
            n = 0;
            foreach (var (kind, id) in docs) { k.Value = kind; i.Value = id; n += cmd.ExecuteNonQuery(); }
            tx.Commit();
        }
        if (n > 0) RaiseChanged();
        return n;
    }

    public void Dispose() => SqliteConnection.ClearAllPools();
}

public sealed record DocRow(string Kind, string Id, string? Parent, string Json, string Updated);

public static class Kinds
{
    public const string Project = "project", Asset = "asset", Rights = "rights", Job = "job", Candidate = "candidate", EditPlan = "editplan", Render = "render", Bgm = "bgm", BrandKit = "brandkit", Setting = "setting", QaRepair = "qarepair";
}
