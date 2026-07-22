// Qdrant 远程向量库后端（D5 / 统一抽象层）。
// 通过 Qdrant REST API 访问（PUT/POST/GET/DELETE），无需额外 DLL，PC 与 Android 均可连 docker 容器服务。
// 相似度由 Qdrant 以 distance=Cosine 返回（score 即余弦相似度）。
// 端点示例：http://localhost:6333 或 http://192.168.x.x:6333（docker 宿主机 IP）。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class QdrantVectorStore : IVectorStoreBackend
{
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _collection;
    private int _dim = -1;
    private RemoteHashStore _hashes;

    public QdrantVectorStore(string endpoint, string apiKey, string collection)
    {
        _endpoint = (endpoint == null ? "" : endpoint.TrimEnd('/'));
        if (string.IsNullOrEmpty(_endpoint)) _endpoint = "http://localhost:6333";
        _apiKey = apiKey;
        _collection = string.IsNullOrEmpty(collection) ? "maf_rag" : collection;
    }

    public string Location => $"Qdrant: {_endpoint}/collections/{_collection}";

    private string CollUrl => $"{_endpoint}/collections/{Uri.EscapeDataString(_collection)}";

    private UnityWebRequest Auth(UnityWebRequest u)
    {
        if (!string.IsNullOrEmpty(_apiKey)) u.SetRequestHeader("api-key", _apiKey);
        // 明文 http（如局域网 Qdrant http://192.168.x.x:6333）需通过 Player Settings 的
        // “Allow HTTP (Cleartext)” = Always Allowed 放行；Unity 2022 无 UnityWebRequest.insecureHttpOption 代码 API，
        // 故该开关在项目设置里开启（见 ProjectSettings.asset 的 insecureHttpOption: 2）。
        // https 端点（自签名/内网证书）在证书校验失败时会抛同一异常，挂忽略证书的 CertificateHandler 规避（仅 https 生效）。
        if (u.url != null && u.url.StartsWith("https", StringComparison.OrdinalIgnoreCase))
            u.certificateHandler = new BypassCertHandler();
        return u;
    }

    // 忽略 TLS 证书校验，用于本地/内网自签名的 Qdrant https 端点（避免 “Insecure connection not allowed”）
    private class BypassCertHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData) => true;
    }

    public async Task InitializeAsync()
    {
        if (_dim < 0) _dim = await EmbeddingFactory.GetDimensionAsync();
        var (exists, dim) = await GetCollectionInfoAsync();
        if (!exists)
        {
            await CreateCollectionAsync(_dim);
            _hashes ??= new RemoteHashStore("qdrant_" + _collection);
            _hashes.Clear();   // 集合不存在→全新集合，清空本地哈希缓存避免误判“已入库”
        }
        else if (dim > 0 && dim != _dim)
        {
            await DeleteCollectionAsync();
            await CreateCollectionAsync(_dim);
            _hashes ??= new RemoteHashStore("qdrant_" + _collection);
            _hashes.Clear();   // 维度变化重建集合，远端点已清空，同步清空本地哈希缓存
        }
    }

    private async Task<(bool, int)> GetCollectionInfoAsync()
    {
        try
        {
            var json = await GetAsync(CollUrl);
            var o = RagMiniJson.Deserialize(json) as Dictionary<string, object>;
            if (o != null && o.TryGetValue("result", out var r) && r is Dictionary<string, object> res)
            {
                if (res.TryGetValue("vectors", out var v) && v is Dictionary<string, object> vec && vec.TryGetValue("size", out var s))
                    return (true, VectorStoreUtil.ToInt(s));
                if (res.ContainsKey("status")) return (true, -1);
            }
        }
        catch { }
        return (false, -1);
    }

    private async Task CreateCollectionAsync(int dim)
    {
        var body = "{\"vectors\":{\"size\":" + dim + ",\"distance\":\"Cosine\"}}";
        await PutAsync(CollUrl, body);
    }

    private async Task DeleteCollectionAsync()
    {
        try { await DeleteAsync(CollUrl); } catch { }
    }

    public async Task UpsertAsync(string source, int chunkIndex, string content, float[] vector)
    {
        uint id = (uint)HashId(source + "#" + chunkIndex);
        var emb = VectorStoreUtil.FloatsToJson(vector);
        var payload = "{\"source\":" + RagMiniJson.Str(source) + ",\"chunkIndex\":" + chunkIndex + ",\"content\":" + RagMiniJson.Str(content) + "}";
        var body = "{\"points\":[{\"id\":" + id + ",\"vector\":" + emb + ",\"payload\":" + payload + "}]}";
        await PutAsync($"{CollUrl}/points", body);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK)
    {
        var emb = VectorStoreUtil.FloatsToJson(queryVector);
        var body = "{\"vector\":" + emb + ",\"limit\":" + topK + ",\"with_payload\":true}";
        var json = await PostAsync($"{CollUrl}/points/search", body);
        var results = new List<SearchResult>();
        var o = RagMiniJson.Deserialize(json) as Dictionary<string, object>;
        if (o != null && o.TryGetValue("result", out var r) && r is List<object> arr)
        {
            foreach (var it in arr)
            {
                if (it is Dictionary<string, object> p)
                {
                    double score = p.TryGetValue("score", out var sc) ? VectorStoreUtil.ToDouble(sc) : 0;
                    var payload = p.TryGetValue("payload", out var pl) && pl is Dictionary<string, object> pd ? pd : null;
                    string src = payload != null && payload.TryGetValue("source", out var s) ? s as string : "";
                    int cidx = payload != null && payload.TryGetValue("chunkIndex", out var ci) ? VectorStoreUtil.ToInt(ci) : 0;
                    string cnt = payload != null && payload.TryGetValue("content", out var c) ? c as string : "";
                    results.Add(new SearchResult(new Chunk(0, src, cidx, cnt, queryVector), (float)score));
                }
            }
        }
        return results;
    }

    public async Task<bool> IsEmptyAsync()
    {
        var (exists, _) = await GetCollectionInfoAsync();
        if (!exists) return true;
        try
        {
            var json = await GetAsync(CollUrl);
            var o = RagMiniJson.Deserialize(json) as Dictionary<string, object>;
            if (o != null && o.TryGetValue("result", out var r) && r is Dictionary<string, object> res)
            {
                if (res.TryGetValue("points_count", out var pc)) return VectorStoreUtil.ToInt(pc) == 0;
                if (res.TryGetValue("vectors_count", out var vc)) return VectorStoreUtil.ToInt(vc) == 0;
            }
        }
        catch { }
        return false;
    }

    public async Task<int?> GetStoredDimensionAsync()
    {
        var (_, dim) = await GetCollectionInfoAsync();
        return dim > 0 ? (int?)dim : null;
    }

    public async Task<int> CountAsync()
    {
        try
        {
            var json = await GetAsync(CollUrl);
            var o = RagMiniJson.Deserialize(json) as Dictionary<string, object>;
            if (o != null && o.TryGetValue("result", out var r) && r is Dictionary<string, object> res)
            {
                if (res.TryGetValue("points_count", out var pc)) return VectorStoreUtil.ToInt(pc);
                if (res.TryGetValue("vectors_count", out var vc)) return VectorStoreUtil.ToInt(vc);
            }
        }
        catch { }
        return 0;
    }

    // 远程后端无法低成本去重来源，返回 -1（UI 显示为 —）
    public Task<int> CountSourcesAsync() => Task.FromResult(-1);

    // 重置向量库：删除整个集合，并清空本地增量入库哈希缓存（否则 Reset 后增量入库会误判“已入库”而跳过）。
    public async Task ClearAsync()
    {
        await DeleteCollectionAsync();
        _hashes ??= new RemoteHashStore("qdrant_" + _collection);
        _hashes.Clear();
    }

    // 按来源（source 字段）删除点：内容变更后重新入库前清理旧块（filter 不匹配时不报错）
    public async Task DeleteBySourceAsync(string source)
    {
        var body = "{\"filter\":{\"must\":[{\"key\":\"source\",\"match\":{\"value\":" + RagMiniJson.Str(source) + "}}]}}";
        await PostAsync($"{CollUrl}/points/delete", body);
    }

    public Task<string> GetHashAsync(string source)
    {
        _hashes ??= new RemoteHashStore("qdrant_" + _collection);
        return Task.FromResult(_hashes.Get(source));
    }

    public Task SetHashAsync(string source, string hash)
    {
        _hashes ??= new RemoteHashStore("qdrant_" + _collection);
        _hashes.Set(source, hash);
        return Task.CompletedTask;
    }

    // ---- HTTP 辅助（在主线程发起，await 轮询，IL2CPP/AOT 安全） ----
    private async Task<string> GetAsync(string url)
    {
        using var u = Auth(UnityWebRequest.Get(url));
        var req = u.SendWebRequest();
        while (!req.isDone) await Task.Yield();
        if (u.result != UnityWebRequest.Result.Success) throw new Exception($"Qdrant GET {url} 失败: {u.error} {u.downloadHandler.text}");
        return u.downloadHandler.text;
    }

    private async Task<string> PostAsync(string url, string body)
    {
        using var u = Auth(new UnityWebRequest(url, "POST"));
        u.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        u.downloadHandler = new DownloadHandlerBuffer();
        u.SetRequestHeader("Content-Type", "application/json");
        var req = u.SendWebRequest();
        while (!req.isDone) await Task.Yield();
        if (u.result != UnityWebRequest.Result.Success) throw new Exception($"Qdrant POST {url} 失败: {u.error} {u.downloadHandler.text}");
        return u.downloadHandler.text;
    }

    private async Task<string> PutAsync(string url, string body)
    {
        using var u = Auth(UnityWebRequest.Put(url, body));
        u.SetRequestHeader("Content-Type", "application/json");
        var req = u.SendWebRequest();
        while (!req.isDone) await Task.Yield();
        if (u.result != UnityWebRequest.Result.Success) throw new Exception($"Qdrant PUT {url} 失败: {u.error} {u.downloadHandler.text}");
        return u.downloadHandler.text;
    }

    private async Task<string> DeleteAsync(string url)
    {
        using var u = Auth(new UnityWebRequest(url, "DELETE"));
        var req = u.SendWebRequest();
        while (!req.isDone) await Task.Yield();
        if (u.result != UnityWebRequest.Result.Success) throw new Exception($"Qdrant DELETE {url} 失败: {u.error} {u.downloadHandler.text}");
        return u.downloadHandler.text;
    }

    private static int HashId(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h = (h ^ c) * 16777619; }
        return unchecked((int)h);
    }
}
