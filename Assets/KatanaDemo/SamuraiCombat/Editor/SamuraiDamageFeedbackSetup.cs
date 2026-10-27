using System;
using OCDesign.Samurai;
using UnityEditor;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>只装配角色受击表现，不重建战斗配置、不保存当前场景。</summary>
    public static class SamuraiDamageFeedbackSetup
    {
        const string Root = "Assets/KatanaDemo/SamuraiCombat";

        [MenuItem("OC Design/Samurai/Install Damage Feedback")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 Play 后安装受击表现。");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/VFX/Shaders/SamuraiDamageBurst.shader");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("缺少可用的 SamuraiDamageBurst Shader。");
            InstallActor(Root + "/Prefabs/SamuraiCombatPlayer.prefab", shader);
            InstallActor(Root + "/Prefabs/SamuraiTrainingOpponent.prefab", shader);
        }

        static void InstallActor(string path, Shader shader)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actor = contents.GetComponent<SamuraiCombatant>();
                if (actor == null) throw new InvalidOperationException("缺少战斗角色：" + path);
                var feedback = contents.GetComponent<SamuraiDamageVfx>();
                if (feedback == null) feedback = contents.AddComponent<SamuraiDamageVfx>();
                feedback.Configure(actor, shader);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
    }
}
