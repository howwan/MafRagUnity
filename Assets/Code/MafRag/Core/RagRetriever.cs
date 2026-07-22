// 检索（D5 / FR-4 / NFR-3）。嵌入查询 -> 向量库余弦 Top-K（后端无关）。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class RagRetriever
{
    private readonly IVectorStoreBackend _store;

    // 一次会话内维度不匹配告警只提示一次，避免每条查询都刷屏。
    private static bool _dimensionWarned;

    public RagRetriever(IVectorStoreBackend store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<SearchResult>> RetrieveAsync(string question, int? topK = null)
    {
        int k = topK ?? RagConfig.TopK;
        float[] vec = await EmbeddingFactory.GenerateAsync(question);

        // 维度自检（R1 / 切换 embedding 模型须重建库）：
        // 库内已有向量但维度与当前查询向量不一致时，检索会全部跳过导致空结果。
        int? storedDim = await _store.GetStoredDimensionAsync();
        if (storedDim.HasValue && storedDim.Value != vec.Length)
        {
            if (!_dimensionWarned)
            {
                _dimensionWarned = true;
                Debug.LogWarning(
                    $"[RagRetriever] embedding 维度不匹配：向量库存储为 {storedDim.Value} 维，当前 embedding 模型输出 {vec.Length} 维。" +
                    $"检索将无结果，请清空向量库后重新入库（切换 embedding 模型须重建库）。");
            }
            return Array.Empty<SearchResult>();
        }

        // 相似度由具体后端（SQLite / pgvector 本地算 / Qdrant 由服务端返回）统一计算。
        return await _store.SearchAsync(vec, k);
    }
}
