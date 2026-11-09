using System;
using OCDesign.Samurai;
using UnityEditor;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>定向安装冬月身体特效，保留角色、根曲线、普通刀光和其他反馈调参。</summary>
    public static class SamuraiFrostMoonEffectsSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        public const string MaterialPath = Root + "/VFX/SamuraiFrostMoon.mat";
        [MenuItem("Katana/Samurai/Effects/Install Frost Moon Effects")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出Play。");
            var shader = Shader.Find("KatanaDemo/VFX/SamuraiFrostMoon");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("冬月Shader尚未编译成功。");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "SamuraiFrostMoon" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            var high = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(Root + "/Config/Nioh3/" + SamuraiFrostMoonSetup.HighName + ".asset");
            var mid = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(Root + "/Config/Nioh3/" + SamuraiFrostMoonSetup.MidName + ".asset");
            if (high == null || mid == null) throw new InvalidOperationException("缺少已确认的冬月动作。");
            var contents = PrefabUtility.LoadPrefabContents(SamuraiCombatSetup.PlayerPath);
            try
            {
                var actor = contents.GetComponent<SamuraiCombatant>();
                var fx = contents.GetComponent<SamuraiFrostMoonVfx>();
                bool changed = fx == null;
                if (fx == null) fx = contents.AddComponent<SamuraiFrostMoonVfx>();
                var data = new SerializedObject(fx);
                changed |= data.FindProperty("actor").objectReferenceValue != actor
                    || data.FindProperty("effectMaterial").objectReferenceValue != material
                    || data.FindProperty("highAction").objectReferenceValue != high
                    || data.FindProperty("midAction").objectReferenceValue != mid;
                if (changed)
                {
                    fx.Configure(actor, material, high, mid);
                    PrefabUtility.SaveAsPrefabAsset(contents, SamuraiCombatSetup.PlayerPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
    }
}
