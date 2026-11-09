using System;
using System.Linq;
using OCDesign.Combat;
using UnityEditor;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>定向装配六招刀光；沿用第一招材质，保留用户挂点、动作与连段参数。</summary>
    public static class Attack01VfxSetup
    {
        public const string MaterialPath = "Assets/KatanaDemo/NaginataCombat/VFX/Attack01BladeWind.mat";
        [MenuItem("Katana/Naginata/Install Naginata Blade Wind (All Six)")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请在 Edit 模式装配刀光。");
            Shader shader = Shader.Find("KatanaDemo/VFX/BladeWindRibbon");
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("刀光 Shader 尚未成功编译。");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "Attack01 Blade Wind" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            GameObject contents = PrefabUtility.LoadPrefabContents(NaginataCombatSetup.PrefabPath);
            try
            {
                var grip = contents.GetComponentInChildren<OCDesign.TwoHandPolearmGrip>(true);
                var driver = contents.GetComponent<CombatAnimationDriver>();
                var attack = AssetDatabase.LoadAssetAtPath<CombatAttackDefinition>(
                    NaginataCombatSetup.Root + "/Config/NaginataAttack01.asset");
                if (!grip || !grip.weaponSettings || !driver || !attack)
                    throw new InvalidOperationException("缺少第一招、驱动器或武器参考。");
                Transform space = grip.weaponSettings.shaftSpace;
                var filter = space.GetComponentInChildren<MeshFilter>();
                if (!filter || !filter.sharedMesh)
                    throw new InvalidOperationException("无法读取正式薙刀网格。");
                // 使用导入后的真实刀尖，避免 glTF X 翻转或弯刃造成端点偏离。
                Vector3 tip = filter.sharedMesh.vertices.OrderByDescending(v => v.y).First();
                tip = space.InverseTransformPoint(filter.transform.TransformPoint(tip));
                var effect = contents.GetComponent<CombatBladeRibbon>();
                if (!effect) effect = contents.AddComponent<CombatBladeRibbon>();
                effect.Configure(driver, attack, space, new Vector3(0f, 1.60f, 0f), tip, material);
                effect.ConfigureAdditionalAttacks(new[]
                {
                    Profile("NaginataAttack02", new Vector2(0.30f, 0.60f)),
                    Profile("NaginataHeavyAttack03Fix", new Vector2(1.28f, 1.49f)),
                    Profile("NaginataAttack04", new Vector2(0.21f, 0.50f)),
                    Profile("NaginataAttack05", new Vector2(0.27f, 0.56f)),
                    Profile("NaginataAttack06", new Vector2(0.20f, 0.44f), new Vector2(0.88f, 1.19f))
                });
                PrefabUtility.SaveAsPrefabAsset(contents, NaginataCombatSetup.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssetIfDirty(material);
        }

        private static CombatBladeRibbon.AttackProfile Profile(string assetName, params Vector2[] seconds)
        {
            var definition = AssetDatabase.LoadAssetAtPath<CombatAttackDefinition>(
                NaginataCombatSetup.Root + "/Config/" + assetName + ".asset");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                NaginataCombatSetup.Root + "/Animations/" + assetName + ".anim");
            if (!definition || !clip || clip.length <= 0f)
                throw new InvalidOperationException("无法读取活动攻击：" + assetName);
            var windows = seconds.Select(window => window / clip.length).ToArray();
            // 与已验收第一招相同的约33.75ms起落，避免长重击使用归一化值而变得过软。
            return new CombatBladeRibbon.AttackProfile(definition, windows, 0.03375f / clip.length);
        }
    }
}
