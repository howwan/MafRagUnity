// 向量存储后端统一抽象（D1 / D5）。
// 用于实现 unified-rag 描述的「统一抽象层」：SQLite（本地·离线）、Qdrant（远程·高性能）、
// pgvector（生产·仅 PC / NPGSQL）可无缝切换，且共享同一套 Embedding / 文本分块 / LLM 生成 / UI 交互逻辑。
// 注：Chunk / SearchResult 记录定义于同程序集的 SqliteVectorStore.cs。

using System.Collections.Generic;
using System.Threading.Tasks;

public interface IVectorStoreBackend
{
    // 初始化（建表 / 建集合 / 校验维度）
    Task InitializeAsync();

    // 写入一条分块向量
    Task UpsertAsync(string source, int chunkIndex, string content, float[] vector);

    // Top-K 检索（相似度由后端自行计算并返回）
    Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK);

    // 库是否为空
    Task<bool> IsEmptyAsync();

    // 库内已存向量维度（空库返回 null），用于切换 embedding 模型后的维度自检
    Task<int?> GetStoredDimensionAsync();

    // 统计：分块总数
    Task<int> CountAsync();

    // 统计：去重来源（文档）数；远程后端无法低成本去重时返回 -1
    Task<int> CountSourcesAsync();

    // 清空（重置）
    Task ClearAsync();

    // 删除某来源（文档）的全部分块，用于内容变更后清理旧块（增量更新，避免新旧块并存）
    Task DeleteBySourceAsync(string source);

    // 增量入库：读取/写入文件内容哈希（远程后端用本地文件实现）
    Task<string> GetHashAsync(string source);
    Task SetHashAsync(string source, string hash);

    // 展示用定位信息（如 SQLite 路径 / 远程 endpoint）
    string Location { get; }
}
