// 编辑器菜单：一键创建 MainScene / SettingsScene 并写入 Build Settings（D0 / FR-9）。
// 用户打开工程后点击菜单 MafRag > Setup Scenes 即可；也可由批处理命令 -executeMethod 调用。

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MafRag.Editor
{
    public static class MafRagMenu
    {
        [MenuItem("MafRag/Setup Scenes & Build Settings")]
        public static void SetupScenes()
        {
            CreateScene("Assets/Scenes/MainScene.unity", "MainCamera");
            CreateScene("Assets/Scenes/SettingsScene.unity", "MainCamera");

            var scenes = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/MainScene.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/SettingsScene.unity", true),
            };
            EditorBuildSettings.scenes = scenes;
            AssetDatabase.SaveAssets();
            Debug.Log("[MafRag] 已创建 MainScene / SettingsScene，并设置 Build Settings（主场景为启动场景）。");
        }

        private static void CreateScene(string path, string cameraName)
        {
            if (System.IO.File.Exists(path))
            {
                Debug.Log($"[MafRag] 场景已存在，跳过：{path}");
                return;
            }
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject(cameraName);
            cam.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0, 0, -10);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[MafRag] 已创建场景：{path}");
        }
    }
}
#endif
