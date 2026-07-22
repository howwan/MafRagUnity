// 远程向量库（Qdrant / pgvector）用的本地文件哈希存储，实现「增量入库」（只处理内容变化的文件）。
// 哈希以 JSON 存于 persistentDataPath，按 collection 区分，避免不同集合互相干扰。

using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class RemoteHashStore
{
    private readonly string _file;
    private Dictionary<string, object> _map = new Dictionary<string, object>();

    public RemoteHashStore(string collection)
    {
        string safe = string.Empty;
        foreach (char c in collection)
            safe += char.IsLetterOrDigit(c) ? c : '_';
        _file = Path.Combine(Application.persistentDataPath, "mafhash_" + safe + ".json");
        Load();
    }

    void Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var o = RagMiniJson.Deserialize(File.ReadAllText(_file));
                if (o is Dictionary<string, object> d)
                {
                    _map = d;
                    return;
                }
            }
        }
        catch (System.Exception ex) { Debug.LogWarning("[RemoteHashStore] 读取失败：" + ex.Message); }
        _map = new Dictionary<string, object>();
    }

    public string Get(string source)
    {
        return _map.TryGetValue(source, out var v) ? v as string : null;
    }

    public void Set(string source, string hash)
    {
        _map[source] = hash;
        Save();
    }

    // 清空全部哈希（重置向量库时使用），并落盘为空 map
    public void Clear()
    {
        _map = new Dictionary<string, object>();
        Save();
    }

    void Save()
    {
        try { File.WriteAllText(_file, RagMiniJson.Serialize(_map)); }
        catch (System.Exception ex) { Debug.LogWarning("[RemoteHashStore] 写入失败：" + ex.Message); }
    }
}
