// 入库管线（D4 / FR-1 / FR-3）。递归扫描目录 *.md，按文本哈希增量入库；
// 单文件异常跳过并记日志（NFR-4）。新增 IngestTextAsync 供 PC 文件夹与 Android 内置知识库共用。

using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

// 入库进度（由后台线程更新、主线程读取以驱动进度条；volatile 保证跨线程可见）。
public class IngestProgress
{
    public volatile int Done;     // 已完成文件数
    public volatile int Total;    // 总文件数
    public volatile int Skipped;  // 因内容未变更而跳过的文件数
    public volatile string Current = ""; // 当前正在处理的文件名
    public volatile string Log = "";     // 追加日志
}

public class RagIngestor
{
    private readonly IVectorStoreBackend _store;

    public RagIngestor(IVectorStoreBackend store)
    {
        _store = store;
    }

    public async Task IngestAsync(IngestProgress status = null)
    {
        string folder = Path.Combine(Application.streamingAssetsPath, RagConfig.RagDocFolder);
        await IngestFolderAsync(folder, status);
    }

    // 从任意目录递归导入其下所有 *.md（主场景“选择目录导入”使用）。返回本次新增块数。
    public async Task<int> IngestFolderAsync(string folder, IngestProgress status = null)
    {
        await _store.InitializeAsync();

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Debug.LogWarning($"[RagIngestor] 未找到目录：{folder}");
            return 0;
        }

        // 维度自检（切换 embedding 模型须重建库）
        int? storedDim = await _store.GetStoredDimensionAsync();
        if (storedDim.HasValue)
        {
            int actualDim = await EmbeddingFactory.GetDimensionAsync();
            if (storedDim.Value != actualDim)
            {
                Debug.LogWarning(
                    $"[RagIngestor] 检测到向量库维度（{storedDim.Value}）与当前 embedding 模型真实输出维度（{actualDim}）不一致，可能来自切换 embedding 模型。" +
                    $"建议先清空向量库再入库，否则旧记录检索时会因维度不符被跳过。");
            }
        }

        string[] files = Directory.GetFiles(folder, "*.md", SearchOption.AllDirectories);
        if (status != null)
        {
            status.Total = files.Length;
            status.Done = 0;
        }
        int totalChunks = 0;
        for (int fi = 0; fi < files.Length; fi++)
        {
            string file = files[fi];
            if (status != null)
            {
                status.Current = Path.GetFileName(file);
                status.Done = fi; // 已完成数（含当前）
            }
            try
            {
                string text = File.ReadAllText(file);
                // 用稳定路径作为去重键，与「仅入库内置知识库」保持一致的键空间
                totalChunks += await IngestSourceAsync(file, Path.GetFileName(file), text, status);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RagIngestor] 处理文件失败，已跳过：{file}\n{ex.Message}");
                if (status != null) status.Log += $"跳过 {Path.GetFileName(file)}: {ex.Message}\n";
            }
        }
        if (status != null) status.Done = files.Length;
        Debug.Log($"[RagIngestor] 完成，本次新增 {totalChunks} 块，跳过 {status?.Skipped ?? 0} 个未变更");
        return totalChunks;
    }

    // 单文件入库（含增量去重）：计算内容哈希，与库内已存哈希比较；一致则跳过不入库。
    // sourceKey 作为去重键（建议用稳定路径，如 Path.Combine(dir, name)），sourceLabel 用于日志与分块来源名。
    // 返回本次新增块数；内容未变更返回 0 并累加 status.Skipped。
    public async Task<int> IngestSourceAsync(string sourceKey, string sourceLabel, string text, IngestProgress status = null)
    {
        string hash = ComputeHash(text);
        string existing = await _store.GetHashAsync(sourceKey);
        if (existing == hash)
        {
            Debug.Log($"[RagIngestor] 跳过（未变更）：{sourceLabel}");
            if (status != null) status.Skipped++;
            return 0; // 增量：内容一致则跳过
        }

        // 内容变更（或该来源此前已有块）：先清理旧块，避免新旧块并存导致重复检索
        await _store.DeleteBySourceAsync(sourceLabel);
        int chunks = await IngestTextAsync(sourceLabel, text, status);
        await _store.SetHashAsync(sourceKey, hash);
        return chunks;
    }

    // 单文本入库（chunk + embed + upsert），供 PC 文件夹与 Android 内置知识库共用。
    // 返回本次新增块数；异常时记日志并返回 0。
    public async Task<int> IngestTextAsync(string source, string text, IngestProgress status = null)
    {
        try
        {
            var chunks = MarkdownChunker.Chunk(text, RagConfig.ChunkSize, RagConfig.Overlap);
            for (int i = 0; i < chunks.Count; i++)
            {
                float[] vec = await EmbeddingFactory.GenerateAsync(chunks[i]);
                await _store.UpsertAsync(source, i, chunks[i], vec);
            }
            Debug.Log($"[RagIngestor] 入库 {source}：{chunks.Count} 块");
            return chunks.Count;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[RagIngestor] 入库文本失败，已跳过：{source}\n{ex.Message}");
            if (status != null) status.Log += $"跳过 {source}: {ex.Message}\n";
            return 0;
        }
    }

    private static string ComputeHash(string text)
    {
        using var sha = new SHA256Managed();
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        byte[] hash = sha.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
