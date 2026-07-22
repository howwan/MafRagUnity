// 编辑器工具：构建期自动生成内置知识库清单 rag-doc/manifest.txt（D16 / FR-15）。
// 供 Android 读取随包 .md 列表；PC 端入库直接递归扫描目录，无需清单。
// 注意：本文件属于 MafRag.Editor.asmdef（includePlatforms=Editor），
// 切勿移到运行时 asmdef 下，否则 using UnityEditor 会导致全部编辑器菜单失效。
// 类不能是 static：IPreprocessBuildWithReport 的回调需实例成员。

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MafRag.Editor
{
    public class RagDocManifestGenerator : IPreprocessBuildWithReport
    {
        // 构建前回调（Android / PC 打包前都会触发），确保随包清单与目录 .md 一致
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            GenerateManifest();
        }

        [MenuItem("MafRag/重新生成知识库清单")]
        public static void MenuGenerate()
        {
            GenerateManifest();
            EditorUtility.DisplayDialog("内置知识库清单", "已重新生成 rag-doc/manifest.txt", "确定");
        }

        public static void GenerateManifest()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "rag-doc");
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning("[RagDocManifest] 目录不存在，跳过生成：" + dir);
                return;
            }

            var mdFiles = Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories);
            var names = new List<string>();
            foreach (var f in mdFiles)
            {
                string rel = f.Substring(dir.Length).TrimStart('\\', '/');
                names.Add(rel.Replace('\\', '/')); // 统一用斜杠，保证 Android 读取一致
            }
            names.Sort();

            string manifest = string.Join("\n", names);
            string outPath = Path.Combine(dir, "manifest.txt");
            File.WriteAllText(outPath, manifest);
            Debug.Log($"[RagDocManifest] 已生成清单：{outPath}（{names.Count} 个 .md）");
        }
    }
}
#endif
