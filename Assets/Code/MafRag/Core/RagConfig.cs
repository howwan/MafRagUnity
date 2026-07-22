// 运行时配置（D1 / FR-3 / FR-8 / CON-3）。所有字段均来自 RagSettings.Current（持久化 JSON）。
// 向量库后端（统一抽象层，无缝切换）：sqlite / qdrant / pgvector。
//   - sqlite：本地·离线（默认）
//   - qdrant：远程·高性能（REST，PC/Android 均可连 docker）
//   - pgvector：生产，仅 PC 构建（Npgsql，需定义 NPGSQL 符号）

using System.IO;
using UnityEngine;

public static class RagConfig
{
    // ---- 向量库后端 ----
    public static string Backend => (RagSettings.Current.backend ?? "sqlite").ToLowerInvariant();
    public static string VectorStoreEndpoint => RagSettings.Current.vectorStoreEndpoint ?? "http://localhost:6333";
    public static string VectorStoreApiKey => RagSettings.Current.vectorStoreApiKey ?? "";
    public static string CollectionName => RagSettings.Current.collectionName ?? "maf_rag";

    // pgvector（仅 PC / NPGSQL）
    public static string PgHost => RagSettings.Current.pgHost ?? "localhost";
    public static int PgPort => System.Math.Max(1, RagSettings.Current.pgPort);
    public static string PgDatabase => RagSettings.Current.pgDatabase ?? "postgres";
    public static string PgUser => RagSettings.Current.pgUser ?? "postgres";
    public static string PgPassword => RagSettings.Current.pgPassword ?? "";
    public static string PgTable => RagSettings.Current.pgTable ?? "rag_chunks";

    // SQLite 本地库路径：统一放 persistentDataPath（PC/Android 均可读写；首次运行从 StreamingAssets 拷贝出厂库）。
    public static string DbPath => Path.Combine(Application.persistentDataPath, "rag.db");

    // 内置知识库（StreamingAssets，随包发布；PC/Android 统一经 UnityWebRequest 读取）
    public static string RagDocFolder => "rag-doc";

    // Embedding（复用 Chat 同源 Ollama endpoint，OpenAI 兼容）
    public static string EmbeddingEndpoint => RagSettings.Current.embeddingEndpoint ?? "http://localhost:11434/v1";
    public static string EmbeddingApiKey => RagSettings.Current.embeddingApiKey ?? "ollama";
    public static string EmbeddingModel => RagSettings.Current.embeddingModel ?? "qwen3-embedding:0.6b";

    // 对话 LLM
    public static string ChatEndpoint => RagSettings.Current.chatEndpoint ?? "http://localhost:11434/v1";
    public static string ChatApiKey => RagSettings.Current.chatApiKey ?? "ollama";
    public static string ChatModel => RagSettings.Current.chatModel ?? "qwen3:4b";

    // 分块 / 检索
    public static int ChunkSize => System.Math.Max(100, RagSettings.Current.chunkSize);
    public static int Overlap => System.Math.Min(ChunkSize - 1, System.Math.Max(0, RagSettings.Current.overlap));
    public static int TopK => System.Math.Max(1, RagSettings.Current.topK);

    // 按当前配置构建向量库后端实例（统一抽象层入口）
    public static IVectorStoreBackend CreateBackend()
    {
        switch (Backend)
        {
            case "qdrant": return new QdrantVectorStore(VectorStoreEndpoint, VectorStoreApiKey, CollectionName);
#if NPGSQL
            case "pgvector": return new PgVectorStore(PgHost, PgPort, PgDatabase, PgUser, PgPassword, PgTable);
#endif
            default: return new SqliteVectorStore(DbPath);
        }
    }
}
