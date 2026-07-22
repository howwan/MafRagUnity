// RAG 状态管理器（D12 / FR-5.3 / FR-9.3）。
// DontDestroyOnLoad 单例，持有向量库/检索器/入库器/智能体，保证主场景<->设置场景往返时对话上下文与连接不丢失。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace MafRag
{
    public class RagManager : MonoBehaviour
    {
        public static RagManager Instance { get; private set; }

        private IVectorStoreBackend _store;
        private RagRetriever _retriever;
        private RagIngestor _ingestor;
        private RagAgentCore _agentCore;
        private bool _ingestedOnce;

        // 跨场景保持的对话历史（DontDestroyOnLoad，主<->设置往返不丢上下文）
        public List<(string role, string text)> History { get; } = new List<(string role, string text)>();

        private async void Awake()
        {
            Instance = this;
            await EnsureDataFilesAsync();   // 首次运行把出厂 rag.db / rag-config.json 拷到 persistentDataPath（Android 才可读写）
            RagSettings.Load();
            BuildBackend();
            _agentCore = new RagAgentCore();
        }

        // 出厂数据文件随包放在 StreamingAssets；为在 Android（StreamingAssets 只读）上可读写，
        // 首次运行时拷贝到 persistentDataPath，之后所有读写都走 persistentDataPath。
        private static async Task EnsureDataFilesAsync()
        {
            string[] files = { "rag.db", "rag-config.json" };
            foreach (var name in files)
            {
                string dest = Path.Combine(Application.persistentDataPath, name);
                if (File.Exists(dest)) continue;
                string src = Path.Combine(Application.streamingAssetsPath, name);
                try
                {
                    if (Application.platform == RuntimePlatform.Android)
                    {
                        using (var req = UnityWebRequest.Get(src))
                        {
                            // 本项目 Unity 版本的 UnityWebRequestAsyncOperation 无 GetAwaiter，改用轮询（与 ReadAllTextAsync 一致）
                            var op = req.SendWebRequest();
                            while (!op.isDone) await Task.Yield();
                            if (req.result == UnityWebRequest.Result.Success)
                                File.WriteAllBytes(dest, req.downloadHandler.data);
                            else
                                Debug.LogWarning($"[RagData] 拷贝出厂文件 {name} 失败：{req.error}");
                        }
                    }
                    else if (File.Exists(src))
                    {
                        File.Copy(src, dest);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[RagData] 拷贝出厂文件 {name} 失败：{ex.Message}");
                }
            }
        }

        // 按当前配置构建向量库后端（支持 SQLite / Qdrant / pgvector（PC）无缝切换）
        private void BuildBackend()
        {
            _store = RagConfig.CreateBackend();
            _retriever = new RagRetriever(_store);
            _ingestor = new RagIngestor(_store);
        }

        public async Task EnsureInitializedAsync() => await _store.InitializeAsync();
        public async Task<bool> IsEmptyAsync() => await _store.IsEmptyAsync();

        // 供 UI 展示的向量库定位信息（如 SQLite 路径 / 远程 endpoint）
        public string BackendLocation => _store?.Location ?? "";
        // 去重文档数（远程后端返回 -1）
        public async Task<int> StoreDocCountAsync() => await _store.CountSourcesAsync();

        // 首次对话且库空 -> 自动入库内置知识库（FR-1.5）
        public async Task EnsureKnowledgeAsync(Action<string> log)
        {
            await _store.InitializeAsync();
            if (_ingestedOnce) return;
            _ingestedOnce = true;
            if (await _store.IsEmptyAsync())
            {
                log?.Invoke("[系统] 知识库为空，正在自动入库内置知识库，请稍候…");
                try { await IngestStreamingAssetsAsync(null); log?.Invoke("[系统] 内置知识库入库完成。"); }
                catch (Exception ex) { log?.Invoke("[系统] 内置知识库入库失败：" + ex.Message); }
            }
        }

        // 内置知识库入库：
        // - PC/Editor：直接扫描 rag-doc 目录（含子目录）的 *.md，增删文件后自动一致，无需维护 manifest.txt。
        // - Android：StreamingAssets 位于 APK 内（jar:file:// URL），无法用 Directory 枚举，回退读取 manifest.txt 白名单。
        public async Task<int> IngestStreamingAssetsAsync(IngestProgress prog)
        {
            await _store.InitializeAsync();
            string dir = Path.Combine(Application.streamingAssetsPath, "rag-doc");

            string[] names;
            if (Application.platform == RuntimePlatform.Android)
            {
                string manifestText = await ReadAllTextAsync(Path.Combine(dir, "manifest.txt"));
                var list = new List<string>();
                foreach (var line in manifestText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string n = line.Trim();
                    if (!string.IsNullOrEmpty(n)) list.Add(n);
                }
                names = list.ToArray();
            }
            else
            {
                var mdFiles = Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories);
                // 以 dir 为基的“相对路径（子目录/文件名）”，剔除前导分隔符；
                // 同时与「选择目录并入库」在 PC 下键空间一致，避免两种方式重复入库。
                names = mdFiles.Select(f => f.Substring(dir.Length).TrimStart('\\', '/')).ToArray();
            }

            if (prog != null) { prog.Total = names.Length; prog.Done = 0; }
            int total = 0;
            for (int i = 0; i < names.Length; i++)
            {
                string rel = names[i];
                if (prog != null) { prog.Current = rel; prog.Done = i; }
                try
                {
                    // Android 的 StreamingAssets 为 jar:file:// URL：逐段 URL 编码（中文/特殊字符），子目录也正确。
                    string fileUrl = Application.platform == RuntimePlatform.Android
                        ? dir + "/" + string.Join("/", rel.Split('/', '\\').Select(Uri.EscapeDataString))
                        : dir + "/" + rel.Replace('\\', '/');
                    string text = await ReadAllTextAsync(fileUrl);
                    // 用稳定路径作为去重键。
                    string key = Path.Combine(dir, rel);
                    total += await _ingestor.IngestSourceAsync(key, rel, text, prog);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RagManager] 读取内置文件失败：{rel}\n{ex.Message}");
                    if (prog != null) prog.Log += $"跳过 {rel}: {ex.Message}\n";
                }
            }
            if (prog != null) prog.Done = names.Length;
            return total;
        }

        // 从用户选择的目录入库（PC 原生对话框或路径输入；Android 用路径输入）
        public async Task<int> IngestFolderAsync(string folder, IngestProgress prog)
        {
            return await _ingestor.IngestFolderAsync(folder, prog);
        }

        // 检索增强问答：检索 Top-K -> 拼上下文 -> MAF 流式生成；回调返回 token 与来源列表。
        public async Task AskStreamingAsync(string question,
            Action<string> onToken,
            Action<List<(string source, float sim, string content)>> onSources,
            Action<string> onError)
        {
            await _store.InitializeAsync();
            try
            {
                var results = await _retriever.RetrieveAsync(question);
                var sources = results.Select(r => (source: r.Chunk.Source, sim: r.Similarity, content: r.Chunk.Content)).ToList();
                onSources?.Invoke(sources);

                string context = sources.Count == 0
                    ? "（无相关上下文）"
                    : string.Join("\n\n", sources.Select(s => $"[来源 {s.source} | 相似度 {s.sim:F3}]\n{s.content}"));

                string prompt = $"<context>\n{context}\n</context>\n\n用户问题：{question}";
                string full = "";
                await _agentCore.RunStreamingAsync(prompt,
                    tok => { full += tok; onToken?.Invoke(tok); },
                    onError);
                History.Add(("user", question));
                History.Add(("assistant", full));
            }
            catch (Exception ex)
            {
                onError?.Invoke("检索失败（Embedding 服务不可用？）：" + ex.Message);
            }
        }

        public async Task ResetAsync() => await _store.ClearAsync();

        // 重置为出厂：删除 persistentDataPath 下的 rag.db / rag-config.json，重新从 StreamingAssets 拷贝出厂文件，
        // 再重载配置、重建后端（回到首次运行的干净状态；对话历史保留）。
        // SQLite 连接为即用即开即用即关（每次操作均 using），删除文件安全，无需显式关闭旧连接。
        public async Task ResetToFactoryAsync()
        {
            foreach (var name in new[] { "rag.db", "rag-config.json" })
            {
                string dest = Path.Combine(Application.persistentDataPath, name);
                try { if (File.Exists(dest)) File.Delete(dest); }
                catch (System.Exception ex) { Debug.LogWarning($"[RagData] 删除出厂副本 {name} 失败：{ex.Message}"); }
            }
            // 一并清理远程后端（Qdrant / pgvector）的本地增量入库哈希缓存，避免残留导致无法重新入库
            try
            {
                foreach (var f in System.IO.Directory.GetFiles(Application.persistentDataPath, "mafhash_*.json"))
                    System.IO.File.Delete(f);
            }
            catch (System.Exception ex) { Debug.LogWarning($"[RagData] 清理哈希缓存失败：{ex.Message}"); }
            await EnsureDataFilesAsync();   // 重新拷贝出厂文件
            RagSettings.Load();
            _ingestedOnce = false;          // 允许返回主场景后再次自动入库内置知识库
            EmbeddingFactory.Reset();
            _agentCore = new RagAgentCore();
            BuildBackend();
        }

        public async Task<(int docs, int chunks, int? dim, string path)> GetStatsAsync()
        {
            await _store.InitializeAsync();
            int chunks = await _store.CountAsync();
            int docs = await _store.CountSourcesAsync();
            int? dim = await _store.GetStoredDimensionAsync();
            return (docs, chunks, dim, _store.Location);
        }

        // 设置变更后调用：重建 embedding 缓存、agent 与向量库后端（应用新配置，FR-8）
        public void ApplySettings()
        {
            EmbeddingFactory.Reset();
            _agentCore = new RagAgentCore();
            BuildBackend();
        }

        // 平台无关文本读取：Android 的 StreamingAssets 在 APK 内，必须用 UnityWebRequest。
        private async Task<string> ReadAllTextAsync(string url)
        {
            if (url.Contains("://") || Application.platform == RuntimePlatform.Android)
            {
                using var uwr = UnityWebRequest.Get(url);
                var req = uwr.SendWebRequest();
                while (!req.isDone) await Task.Yield();
                if (uwr.result != UnityWebRequest.Result.Success)
                    throw new Exception(uwr.error);
                return uwr.downloadHandler.text;
            }
            return File.ReadAllText(url);
        }
    }
}
