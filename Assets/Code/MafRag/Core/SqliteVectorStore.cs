// SQLite 向量存储（D1 / D5 / FR-3 / FR-4 / CON-1 / NFR-1）。
// 存储：BLOB float32（小端紧凑，与 sqlite-vec serialize_float32 字节布局一致），附 dim 列做维度校验。
// 计算：相似度以委托注入（SearchAsync 的 similarity 参数），由调用方（Project 程序集）提供
//       TensorPrimitives.CosineSimilarity（SIMD，AOT 安全）。本程序集因此不引用 System.Numerics.Tensors，
//       仅需 Mono.Data.Sqlite / System.Data（Mono.Data.Sqlite 以 Assets/Packages 下的自动引用托管插件提供）。
// 注意：Mono.Data.Sqlite 的 SqliteCommand 无 Async 重载，故 DB 操作走同步 API，外层以 Task.Run 卸载到线程池，
//       避免阻塞 Unity 主线程，同时保持对外 async Task 契约。

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Mono.Data.Sqlite;

public record Chunk(long Id, string Source, int ChunkIndex, string Content, float[] Vector);

// 检索结果（含余弦相似度分数）
public record SearchResult(Chunk Chunk, float Similarity);

public class SqliteVectorStore : IVectorStoreBackend
{
    private readonly string _dbPath;

    public SqliteVectorStore(string dbPath)
    {
        _dbPath = dbPath;
        // 确保库文件所在目录存在（StreamingAssets 根目录已存在，无需建目录）；SQLite 仅会自动建文件、不会建目录。
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath};Version=3;");
        conn.Open();
        return conn;
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Chunks (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    source      TEXT NOT NULL,
                    chunk_index INTEGER NOT NULL,
                    content     TEXT NOT NULL,
                    dim         INTEGER NOT NULL,
                    embedding   BLOB NOT NULL
                );
                CREATE TABLE IF NOT EXISTS FileHashes (
                    source TEXT PRIMARY KEY,
                    hash   TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();
        });
    }

    public async Task UpsertAsync(string source, int chunkIndex, string content, float[] vector)
    {
        await Task.Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Chunks (source, chunk_index, content, dim, embedding) " +
                              "VALUES ($source, $idx, $content, $dim, $emb)";
            cmd.Parameters.AddWithValue("$source", source);
            cmd.Parameters.AddWithValue("$idx", chunkIndex);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$dim", vector.Length);
            cmd.Parameters.AddWithValue("$emb", ToBlob(vector));
            cmd.ExecuteNonQuery();
        });
    }

    // 相似度计算下放到存储层（各后端统一实现），不再由调用方注入。
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK)
    {
        return await Task.Run(() =>
        {
            var scored = new List<(Chunk chunk, float sim)>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, source, chunk_index, content, dim, embedding FROM Chunks";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int dim = reader.GetInt32(4);
                if (dim != queryVector.Length) continue; // 维度不符跳过（R1 / 多模型兼容）
                var blob = reader.GetValue(5) as byte[] ?? Array.Empty<byte>();
                if (blob.Length != dim * 4) continue;
                float[] vec = FromBlob(blob);
                float sim = Cosine(queryVector, vec);
                scored.Add((new Chunk(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), vec), sim));
            }

            scored.Sort((a, b) => b.sim.CompareTo(a.sim)); // 相似度降序
            int k = Math.Min(topK, scored.Count);
            var result = new List<SearchResult>(k);
            for (int i = 0; i < k; i++) result.Add(new SearchResult(scored[i].chunk, scored[i].sim));
            return (IReadOnlyList<SearchResult>)result;
        });
    }

    // 余弦相似度（手动实现，AOT/IL2CPP 安全，无需 System.Numerics.Tensors）
    private static float Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        if (na == 0 || nb == 0) return 0;
        return (float)(dot / (System.Math.Sqrt(na) * System.Math.Sqrt(nb)));
    }

    public string Location => "SQLite: " + _dbPath;

    public async Task<bool> IsEmptyAsync()
    {
        return await Task.Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Chunks";
            object v = cmd.ExecuteScalar();
            return Convert.ToInt64(v) == 0;
        });
    }

    // 读取库内已存向量的维度（取首条 Chunks 记录）。空库返回 null。
    // 用于「切换 embedding 模型后维度不符」的自检（R1 / CON-3）。
    public async Task<int?> GetStoredDimensionAsync()
    {
        return await Task.Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT dim FROM Chunks LIMIT 1";
            object v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? (int?)null : Convert.ToInt32(v);
        });
    }

    // 统计：Chunks 总记录数（FR-6）
    public async Task<int> CountAsync()
    {
        return await Task.Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Chunks";
            object v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v);
        });
    }

    // 统计：去重来源（文档）数（FR-6）
    public async Task<int> CountSourcesAsync()
    {
        return await Task.Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(DISTINCT source) FROM Chunks";
            object v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v);
        });
    }

    public async Task ClearAsync()
    {
        await Task.Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Chunks; DELETE FROM FileHashes;";
            cmd.ExecuteNonQuery();
        });
    }

    // 删除某来源（文档）全部分块：内容变更后重新入库前先清理旧块，避免新旧并存
    public async Task DeleteBySourceAsync(string source)
    {
        await Task.Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Chunks WHERE source = $s";
            cmd.Parameters.AddWithValue("$s", source);
            cmd.ExecuteNonQuery();
        });
    }

    // 增量入库（D4）：获取/设置文件内容哈希
    public async Task<string> GetHashAsync(string source)
    {
        return await Task.Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT hash FROM FileHashes WHERE source = $s";
            cmd.Parameters.AddWithValue("$s", source);
            object v = cmd.ExecuteScalar();
            return v == null ? null : v.ToString();
        });
    }

    public async Task SetHashAsync(string source, string hash)
    {
        await Task.Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO FileHashes (source, hash) VALUES ($s, $h)";
            cmd.Parameters.AddWithValue("$s", source);
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.ExecuteNonQuery();
        });
    }

    // ---- BLOB float32 序列化（小端；x86/ARM 均小端，无需字节序转换） ----
    private static byte[] ToBlob(float[] v)
    {
        var b = new byte[v.Length * 4];
        Buffer.BlockCopy(v, 0, b, 0, b.Length);
        return b;
    }

    private static float[] FromBlob(byte[] b)
    {
        var v = new float[b.Length / 4];
        Buffer.BlockCopy(b, 0, v, 0, b.Length);
        return v;
    }
}
