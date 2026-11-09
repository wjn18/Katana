using System;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>定向安装命中反馈和镜头表现，保留战斗与刀光的现有调参。</summary>
    public static class SamuraiImpactFeedbackSetup
    {
        [MenuItem("Katana/Samurai/Effects/Install Impact And Rush Feedback")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play。");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != SamuraiCombatSetup.Root + "/Scenes/SamuraiCombatDemo.unity" || scene.isDirty)
                throw new InvalidOperationException("请在已保存的 SamuraiCombatDemo 场景安装。");
            InstallActor(SamuraiCombatSetup.PlayerPath);
            InstallActor(SamuraiCombatSetup.EnemyPath);
            var orbit = UnityEngine.Object.FindAnyObjectByType<SamuraiOrbitCamera>();
            var input = orbit != null ? orbit.input : null;
            if (orbit == null || input == null || input.actor == null)
                throw new InvalidOperationException("缺少已连接玩家的 SamuraiOrbitCamera。");
            var feedback = orbit.GetComponent<SamuraiCombatCameraFeedback>();
            if (feedback == null) feedback = orbit.gameObject.AddComponent<SamuraiCombatCameraFeedback>();
            feedback.Configure(orbit, input.actor);
            EditorUtility.SetDirty(feedback);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void InstallActor(string path)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actor = contents.GetComponent<SamuraiCombatant>();
                if (actor == null) throw new InvalidOperationException("缺少战斗角色：" + path);
                var feedback = contents.GetComponent<SamuraiImpactFeedback>();
                if (feedback == null) feedback = contents.AddComponent<SamuraiImpactFeedback>();
                feedback.Configure(actor);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
    }
}
