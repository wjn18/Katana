using System;
using OCDesign.Samurai;
using UnityEditor;

namespace OCDesign.EditorTools
{
    /// <summary>开启玩家琢技并登记现有居合武技；不重建场景/Prefab，不重设用户倍率。</summary>
    public static class SamuraiArtsSetup
    {
        [MenuItem("OC Design/Samurai/Install Arts Proficiency")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出Play后接入琢技。");
            var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(
                "Assets/KatanaDemo/SamuraiCombat/Config/SamuraiPlayer.asset");
            if (profile == null) throw new InvalidOperationException("缺少现有玩家配置。");
            if (!profile.artsEnabled)
            {
                profile.artsEnabled = true;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
            if (profile.charged == null) throw new InvalidOperationException("缺少现有居合动作。");
            if (!profile.charged.isMartialArt || profile.charged.displayName != "Iai")
            {
                profile.charged.isMartialArt = true;
                profile.charged.displayName = "Iai";
                EditorUtility.SetDirty(profile.charged);
                AssetDatabase.SaveAssetIfDirty(profile.charged);
            }
        }
    }
}
