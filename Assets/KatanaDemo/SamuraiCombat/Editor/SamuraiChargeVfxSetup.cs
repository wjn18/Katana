using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>仅安装蓄力表现与保存新增蓄力参数，不重建战斗或覆盖原刀光调参。</summary>
    public static class SamuraiChargeVfxSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        public const string GlowMaterialPath = Root + "/VFX/SamuraiChargeBlade.mat";
        public const string ScreenMaterialPath = Root + "/VFX/SamuraiCriticalScreen.mat";
        public const string WindMaterialPath = Root + "/VFX/SamuraiChargeWind.mat";

        [MenuItem("OC Design/Samurai/Install Charge Wind")]
        public static void InstallWind()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play。");
            var wind = Material(WindMaterialPath, "KatanaDemo/VFX/SamuraiChargeWind");
            InstallWindActor(SamuraiCombatSetup.PlayerPath, wind);
            InstallWindActor(SamuraiCombatSetup.EnemyPath, wind);
        }

        static void InstallWindActor(string path, Material material)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actor = contents.GetComponent<SamuraiCombatant>();
                if (!actor) throw new InvalidOperationException("缺少战斗角色：" + path);
                var serialized = new SerializedObject(actor);
                var bladeBase = serialized.FindProperty("weaponBase").objectReferenceValue as Transform;
                var bladeTip = serialized.FindProperty("weaponTip").objectReferenceValue as Transform;
                if (!bladeBase || !bladeTip) throw new InvalidOperationException("缺少实际刀刃端点。");
                var wind = contents.GetComponent<SamuraiChargeWind>();
                if (!wind) wind = contents.AddComponent<SamuraiChargeWind>();
                wind.Configure(actor, bladeBase, bladeTip, material);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        [MenuItem("OC Design/Samurai/Install Charge Effects")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play。");
            var glow = Material(GlowMaterialPath, "KatanaDemo/VFX/SamuraiChargeBlade");
            var screen = Material(ScreenMaterialPath, "OC Design/Samurai/Critical Screen Cracks");
            InstallActor(SamuraiCombatSetup.PlayerPath, glow, screen, true);
            InstallActor(SamuraiCombatSetup.EnemyPath, glow, screen, false);
            InstallWind();
        }

        static Material Material(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Shader 尚未正确编译：" + shaderName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        static void InstallActor(string path, Material glowMaterial, Material screenMaterial, bool player)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actor = contents.GetComponent<SamuraiCombatant>();
                if (!actor || !actor.Profile) throw new InvalidOperationException("缺少战斗角色：" + path);
                var serialized = new SerializedObject(actor);
                var bladeBase = serialized.FindProperty("weaponBase").objectReferenceValue as Transform;
                var bladeTip = serialized.FindProperty("weaponTip").objectReferenceValue as Transform;
                if (!bladeBase || !bladeTip || !bladeBase.parent)
                    throw new InvalidOperationException("缺少实际刀刃端点。");
                var meshes = bladeBase.parent.GetComponentsInChildren<MeshFilter>(true)
                    .Where(m => m.sharedMesh && m.sharedMesh.name == "Uchigatana_Smoothed_Blade").ToArray();
                if (meshes.Length != 1)
                    throw new InvalidOperationException("无法唯一定位当前打刀刀身网格。");
                var glow = contents.GetComponent<SamuraiChargeBladeGlow>();
                if (!glow) glow = contents.AddComponent<SamuraiChargeBladeGlow>();
                glow.Configure(actor, bladeBase, bladeTip, meshes, glowMaterial);
                if (player)
                {
                    var flash = contents.GetComponent<SamuraiCriticalScreenFlash>();
                    if (!flash) flash = contents.AddComponent<SamuraiCriticalScreenFlash>();
                    flash.Configure(actor, screenMaterial);
                }
                // 保存新增字段的默认值；不覆盖用户已调整的蓄力/战斗参数。
                EditorUtility.SetDirty(actor.Profile);
                AssetDatabase.SaveAssetIfDirty(actor.Profile);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
    }
}
