// RAG 配置（JSON 持久化，D1 / FR-8 / CON-3）。读优先 persistentDataPath，缺失时回退内置默认。
// 向量库后端：sqlite | qdrant | pgvector（pgvector 仅 PC / NPGSQL）。

using System.IO;
using UnityEngine;

[System.Serializable]
public class RagSettingsData
{
    // 向量库后端：sqlite | qdrant | pgvector
    public string backend = "sqlite";
    public string vectorStoreEndpoint = "http://localhost:6333";
    public string vectorStoreApiKey = "";
    public string collectionName = "maf_rag";

    // pgvector（仅 PC / NPGSQL 生效）
    public string pgHost = "localhost";
    public int pgPort = 5432;
    public string pgDatabase = "postgres";
    public string pgUser = "postgres";
    public string pgPassword = "";
    public string pgTable = "rag_chunks";

    // Embedding
    public string embeddingEndpoint = "http://localhost:11434/v1";
    public string embeddingApiKey = "ollama";
    public string embeddingModel = "qwen3-embedding:0.6b";

    // 对话 LLM
    public string chatEndpoint = "http://localhost:11434/v1";
    public string chatApiKey = "ollama";
    public string chatModel = "qwen3:4b";

    // 分块 / 检索
    public int chunkSize = 1000;
    public int overlap = 100;
    public int topK = 3;

    // 默认 Markdown 目录（PC 绝对路径；留空则用内置知识库）
    public string defaultMarkdownDir = "";

    // 日志级别：Debug | Info | Warn | Error（越低越详细），设置界面切换并持久化。
    public string logLevel = "Info";
}

public static class RagSettings
{
    public static RagSettingsData Current = new RagSettingsData();

    // 配置文件统一放 persistentDataPath（PC/Android 均可读写；首次运行从 StreamingAssets 拷贝出厂配置）。
    // 设置界面的修改直接写回此文件，保证“界面配置”与“rag-config.json”始终一致。
    private static string ConfigPath => Path.Combine(Application.persistentDataPath, "rag-config.json");

    public static void Load()
    {
        try
        {
            string json = null;
            if (File.Exists(ConfigPath)) json = File.ReadAllText(ConfigPath);                 // 已保存配置（首次运行已从 StreamingAssets 拷贝）
            if (!string.IsNullOrEmpty(json))
            {
                var d = JsonUtility.FromJson<RagSettingsData>(json);
                if (d != null) Current = d;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[RagSettings] 读取配置失败，使用内置默认：" + ex.Message);
        }
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonUtility.ToJson(Current, true));
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[RagSettings] 保存配置失败：" + ex.Message);
        }
    }
}
