// Embedding 生成器（D2 / FR-3 / CON-3）。
// 复用 ChatClientFactory 同源 Ollama endpoint，经 Microsoft.Extensions.AI 的 OpenAI embedding 客户端访问。
// 维度不再手工配置：每次生成依赖模型默认输出，入库与查询天然一致（R1）。
// 真实维度由 GetDimensionAsync() 运行时探针测得，用于切换模型后的库维度自检。

using System;
using System.ClientModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OpenAI;

public static class EmbeddingFactory
{
    private static IEmbeddingGenerator<string, Embedding<float>> _generator;

    // 模型真实输出维度的缓存（由探针 embedding 测得）。
    private static int? _cachedDimension;

    /// <summary>构建一个指向本地 Ollama embedding 服务的生成器（配置来自 RagSettings）。</summary>
    public static IEmbeddingGenerator<string, Embedding<float>> CreateLocal()
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri(RagConfig.EmbeddingEndpoint) };
        var openAIClient = new OpenAIClient(new ApiKeyCredential(RagConfig.EmbeddingApiKey), options);
        // OpenAI.Embeddings.EmbeddingClient -> IEmbeddingGenerator（Microsoft.Extensions.AI 扩展）
        var embeddingClient = openAIClient.GetEmbeddingClient(RagConfig.EmbeddingModel);
        return embeddingClient.AsIEmbeddingGenerator();
    }

    /// <summary>清空缓存的生成器，使下一次调用使用最新配置。</summary>
    public static void Reset()
    {
        _generator = null;
        _cachedDimension = null;
    }

    /// <summary>
    /// 直接从 embedding 模型查询其真实输出维度（以探针文本生成一个向量，取其长度）。
    /// 结果缓存，避免重复请求。这是与向量库维度比对时的唯一可靠基准。
    /// </summary>
    public static async Task<int> GetDimensionAsync()
    {
        if (_cachedDimension.HasValue) return _cachedDimension.Value;
        float[] probe = await GenerateAsync("__dimension_probe__");
        _cachedDimension = probe.Length;
        return probe.Length;
    }

    /// <summary>将文本转为 float[] 向量。异常向上抛出供上层捕获（NFR-4）。</summary>
    public static async Task<float[]> GenerateAsync(string text)
    {
        _generator ??= CreateLocal();
        // 不锁定 Dimensions：模型返回其默认维度，入库与查询调用同一方法天然一致。
        GeneratedEmbeddings<Embedding<float>> embeddings = await _generator.GenerateAsync(new[] { text });
        Embedding<float> embedding = embeddings.First();
        // Embedding<float>.Vector 为 ReadOnlyMemory<float>；转 float[] 便于存储与计算
        return embedding.Vector.ToArray();
    }
}
