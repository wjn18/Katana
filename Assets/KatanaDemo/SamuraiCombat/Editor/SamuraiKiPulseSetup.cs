using System;
using OCDesign.Samurai;
using UnityEditor;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>只为现有玩家开启残心并装配表现；不重建场景、动作或防御配置。</summary>
    public static class SamuraiKiPulseSetup
    {
        const string Root = "Assets/KatanaDemo/SamuraiCombat";

        [MenuItem("OC Design/Samurai/Enable Frost Moon Input")]
        public static void EnableFrostMoon()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出Play后修改冬月配置。");
            var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            if (profile == null) throw new InvalidOperationException("缺少现有玩家配置。");
            if (profile.kiPulseEnabled && profile.frostMoonEnabled) return;
            profile.kiPulseEnabled = profile.frostMoonEnabled = true;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        [MenuItem("OC Design/Samurai/Enable Ki Pulse Flux")]
        public static void EnableFlux()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出Play后修改残心配置。");
            var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            if (profile == null) throw new InvalidOperationException("缺少现有玩家配置。");
            if (profile.kiPulseEnabled && profile.kiPulseFluxEnabled) return;
            profile.kiPulseEnabled = profile.kiPulseFluxEnabled = true;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        [MenuItem("OC Design/Samurai/Install Ki Pulse")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出Play后接入残心。");
            var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/VFX/Shaders/SamuraiChargeWind.shader");
            if (profile == null || shader == null) throw new InvalidOperationException("缺少现有玩家配置或光点Shader。");
            if (!profile.kiPulseEnabled)
            {
                profile.kiPulseEnabled = true;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
            string path = Root + "/Prefabs/SamuraiCombatPlayer.prefab";
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var feedback = contents.GetComponent<SamuraiKiPulseFeedback>();
                if (feedback == null)
                {
                    feedback = contents.AddComponent<SamuraiKiPulseFeedback>();
                    feedback.Configure(contents.GetComponent<SamuraiCombatant>(), shader);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
    }
}
