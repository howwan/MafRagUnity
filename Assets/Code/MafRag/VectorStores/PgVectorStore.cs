// pgvector（PostgreSQL + pgvector 扩展）向量库后端（仅 PC 构建，生产环境）。
// 通过 Npgsql 访问。InitializeAsync 会**自动**执行 CREATE EXTENSION IF NOT EXISTS vector
// （与 unified-rag 的 pgvector.py 一致），无需手动前置；仅当数据库账号没有 CREATE EXTENSION
// 权限时，才需要先用超级用户执行一次 CREATE EXTENSION vector。
//
// 本文件整体以 NPGSQL 条件编译符号包裹：仅在“已定义 NPGSQL”时编译（该符号应仅加在
// PC 构建的 Scripting Define Symbols 中），因此：
//   - Android / 未定义 NPGSQL 的构建：不会引用 Npgsql，也不会打包该依赖；
//   - PC 构建且已加 NPGSQL：pgvector 后端可用，与其他后端共享同一套抽象层。
//
// 启用步骤（README 亦有说明）：
//   1) 将 Npgsql.dll 及其依赖放入 Assets/Plugins/Npgsql（仅 PC 平台可见）；
//   2) 在 Player Settings → PC Standalone / Editor 的 Scripting Define Symbols 中加入 NPGSQL。

#if NPGSQL
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using UnityEngine;

public class PgVectorStore : IVectorStoreBackend
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _database;
    private readonly string _user;
    private readonly string _password;
    private readonly string _table;
    private int _dim = -1;
    private RemoteHashStore _hashes;

    public PgVectorStore(string host, int port, string database, string user, string password, string table)
    {
        _host = string.IsNullOrEmpty(host) ? "localhost" : host;
        _port = port <= 0 ? 5432 : port;
        _database = string.IsNullOrEmpty(database) ? "postgres" : database;
        _user = string.IsNullOrEmpty(user) ? "postgres" : user;
        _password = password ?? "";
        _table = string.IsNullOrEmpty(table) ? "rag_chunks" : table;
    }

    public string Location => $"pgvector: {_host}:{_port}/{_database}.{_table}";

    private string ConnStr()
        => $"Host={_host};Port={_port};Database={_database};Username={_user};Password={_password};Pooling=false;Timeout=15;CommandTimeout=60";

    // 维护库（postgres）连接串：用于建库前探测/创建目标库。
    private string MaintenanceConnStr()
        => $"Host={_host};Port={_port};Database=postgres;Username={_user};Password={_password};Pooling=false;Timeout=15;CommandTimeout=60";

    // 目标库是否存在：能连上即存在；仅当明确为 “3D000: database does not exist” 时返回 false，
    // 其余连接异常（服务未起、密码错等）原样抛出，避免静默吞掉故障。
    private async Task<bool> DatabaseExistsAsync()
    {
        try
        {
            using var conn = new NpgsqlConnection(ConnStr());
            await conn.OpenAsync();
            return true;
        }
        catch (PostgresException pex) when (pex.SqlState == "3D000")
        {
            return false;
        }
    }

    // 目标库不存在时，连维护库自动 CREATE DATABASE。用户账号需有 CREATEDB 权限；
    // 缺乏权限或库已被并发创建时给出友好提示/忽略。
    private async Task EnsureDatabaseAsync(string dbName)
    {
        using var conn = new NpgsqlConnection(MaintenanceConnStr());
        await conn.OpenAsync();
        try
        {
            using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\";", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException pex) when (pex.SqlState == "42P04")
        {
            // 并发/竞态：库已被他人创建，忽略
        }
        catch (PostgresException pex) when (pex.SqlState == "42501")
        {
            throw new Exception($"数据库 “{dbName}” 不存在，且当前账号无创建数据库权限（SQLSTATE 42501）。请先用管理员执行：CREATE DATABASE \"{dbName}\";");
        }
    }

    public async Task InitializeAsync()
    {
        if (_dim < 0) _dim = await EmbeddingFactory.GetDimensionAsync();
        // 目标库不存在时自动建库，避免一进主界面就报 “database does not exist”。
        if (!await DatabaseExistsAsync())
            await EnsureDatabaseAsync(_database);
        using var conn = new NpgsqlConnection(ConnStr());
        await conn.OpenAsync();
        // 启用 pgvector 扩展（需超级用户；无则抛出友好提示）
        try
        {
            using var ext = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", conn);
            await ext.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            throw new Exception("无法启用 pgvector 扩展（若数据库账号非超级用户，请先用超级用户执行一次 CREATE EXTENSION vector）：" + ex.Message);
        }
        // 维度变化则重建表
        int? existing = await GetDimAsync(conn);
        if (existing.HasValue && existing.Value != _dim)
        {
            using var d = new NpgsqlCommand($"DROP TABLE IF EXISTS {_table};", conn);
            await d.ExecuteNonQueryAsync();
            existing = null;
        }
        if (!existing.HasValue)
        {
            using var ct = new NpgsqlCommand(
                $"CREATE TABLE IF NOT EXISTS {_table} (id serial PRIMARY KEY, source text, chunk_index int, content text, embedding vector({_dim}));", conn);
            await ct.ExecuteNonQueryAsync();
            using var idx = new NpgsqlCommand(
                $"CREATE UNIQUE INDEX IF NOT EXISTS {_table}_src_idx ON {_table}(source, chunk_index);", conn);
            await idx.ExecuteNonQueryAsync();
        }
        // 余弦检索加速索引（HNSW，与 unified-rag 示例一致；需 pgvector>=0.5）。
        // 失败不阻断初始化：老版本 pgvector 或不支持 HNSW 时，退回顺序扫描。
        try
        {
            using var hnsw = new NpgsqlCommand(
                $"CREATE INDEX IF NOT EXISTS {_table}_hnsw_idx ON {_table} USING hnsw (embedding vector_cosine_ops);", conn);
            await hnsw.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PgVector: 跳过 HNSW 索引（可能 pgvector 版本过低或配置不支持），退回顺序扫描：{ex.Message}");
        }
    }

    public async Task UpsertAsync(string source, int chunkIndex, string content, float[] vector)
    {
        using var conn = new NpgsqlConnection(ConnStr());
        await conn.OpenAsync();
        // 不依赖 NpgsqlVector（低版本 Npgsql 无此类型）：直接以 pgvector 字面量写入 SQL。
        // 字面量仅由浮点数组成，不含用户输入，安全。
        using var cmd = new NpgsqlCommand(
            $"INSERT INTO {_table} (source, chunk_index, content, embedding) VALUES (@s,@ci,@c,'{VecLiteral(vector)}') " +
            $"ON CONFLICT (source, chunk_index) DO UPDATE SET content=EXCLUDED.content, embedding=EXCLUDED.embedding;", conn);
        cmd.Parameters.AddWithValue("s", source);
        cmd.Parameters.AddWithValue("ci", chunkIndex);
        cmd.Parameters.AddWithValue("c", content);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK)
    {
        using var conn = new NpgsqlConnection(ConnStr());
        await conn.OpenAsync();
        // pgvector 的 <=> 为余弦距离 = 1 - 余弦相似度，故相似度 = 1 - distance
        using var cmd = new NpgsqlCommand(
            $"SELECT source, chunk_index, content, 1 - (embedding <=> '{VecLiteral(queryVector)}') AS sim FROM {_table} ORDER BY embedding <=> '{VecLiteral(queryVector)}' LIMIT @k;", conn);
        cmd.Parameters.AddWithValue("k", topK);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<SearchResult>();
        while (await r.ReadAsync())
        {
            string src = r.GetString(0);
            int cidx = r.GetInt32(1);
            string cnt = r.GetString(2);
            float sim = (float)r.GetDouble(3);
            list.Add(new SearchResult(new Chunk(0, src, cidx, cnt, queryVector), sim));
        }
        return list;
    }

    public async Task<bool> IsEmptyAsync()
    {
        using var conn = new NpgsqlConnection(ConnStr());
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {_table};", conn);
        var n = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(n) == 0;
    }

    public async Task<int?> GetStoredDimensionAsync()
    {
        using var conn = new NpgsqlConnection(ConnStr());
        await conn.OpenAsync();
        return await GetDimAsync(conn);
    }

    private async Task<int?> GetDimAsync(NpgsqlConnection conn)
    {
        try
        {
            using var cmd = new NpgsqlCommand(
                "SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a WHERE a.attrelid = @t::regclass AND a.attname='embedding';", conn);
            cmd.Parameters.AddWithValue("t", _table);
            var o = await cmd.ExecuteScalarAsync();
            if (o == null || o == DBNull.Value) return null;
            var m = Regex.Match(o.ToString(), @"vector\((\d+)\)");
            return m.Success ? int.Parse(m.Groups[1].Value) : (int?)null;
        }
        catch { return null; }
    }

    public async Task<int> CountAsync()
    {
        using var conn = new NpgsqlConnection(ConnStr());
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {_table};", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<int> CountSourcesAsync()
    {
        using var conn = new NpgsqlConnection(ConnStr());
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand($"SELECT COUNT(DISTINCT source) FROM {_table};", conn);
        var n = await cmd.ExecuteScalarAsync();
        return n == null || n == DBNull.Value ? 0 : Convert.ToInt32(n);
    }

    public async Task ClearAsync()
    {
        using var conn = new NpgsqlConnection(ConnStr());
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand($"DELETE FROM {_table};", conn);
        await cmd.ExecuteNonQueryAsync();
        _hashes ??= new RemoteHashStore("pg_" + _table);
        _hashes.Clear();   // 清空本地增量入库哈希缓存，避免 Reset 后误判“已入库”而跳过
    }

    // 删除某来源（文档）全部分块：内容变更后重新入库前先清理旧块，避免新旧并存
    public async Task DeleteBySourceAsync(string source)
    {
        using var conn = new NpgsqlConnection(ConnStr());
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand($"DELETE FROM {_table} WHERE source = @s;", conn);
        cmd.Parameters.AddWithValue("s", source);
        await cmd.ExecuteNonQueryAsync();
    }

    public Task<string> GetHashAsync(string source)
    {
        _hashes ??= new RemoteHashStore("pg_" + _table);
        return Task.FromResult(_hashes.Get(source));
    }

    public Task SetHashAsync(string source, string hash)
    {
        _hashes ??= new RemoteHashStore("pg_" + _table);
        _hashes.Set(source, hash);
        return Task.CompletedTask;
    }

    // 将浮点向量格式化为 pgvector 字面量 '[a,b,c]'，使用不变文化，避免小数点区域化问题。
    private static string VecLiteral(float[] v)
    {
        if (v == null || v.Length == 0) return "[]";
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < v.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(v[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }
}
#endif
