using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>只装配打刀刀光；不重建角色、动画、动作配置或场景。</summary>
    public static class SamuraiBladeVfxSetup
    {
        public const string MaterialPath = SamuraiCombatSetup.Root + "/VFX/SamuraiBladeWind.mat";
        const string SourceMaterial = "Assets/KatanaDemo/NaginataCombat/VFX/Attack01BladeWind.mat";

        [MenuItem("OC Design/Samurai/Install Blade Wind")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请在 Edit 模式装配刀光。");
            var source = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterial);
            if (!source || !source.shader || ShaderUtil.ShaderHasError(source.shader))
                throw new InvalidOperationException("薙刀刀光材质或共享 Shader 不可用。");
            string folder = SamuraiCombatSetup.Root + "/VFX";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(SamuraiCombatSetup.Root, "VFX");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(source) { name = "SamuraiBladeWind" };
                material.SetFloat("_EdgeWidth", .045f);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            InstallActor(SamuraiCombatSetup.PlayerPath, material);
            InstallActor(SamuraiCombatSetup.EnemyPath, material);
            AssetDatabase.SaveAssetIfDirty(material);
        }

        static void InstallActor(string path, Material material)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actor = contents.GetComponent<SamuraiCombatant>();
                if (!actor || !actor.Profile || actor.Profile.combo == null || actor.Profile.combo.Length != 4)
                    throw new InvalidOperationException("缺少完整打刀角色及四段连击：" + path);
                // 读取角色实际使用的挂点引用，不按名称猜测或重设武器位置。
                var serialized = new SerializedObject(actor);
                var bladeBase = serialized.FindProperty("weaponBase").objectReferenceValue as Transform;
                var bladeTip = serialized.FindProperty("weaponTip").objectReferenceValue as Transform;
                if (!bladeBase || !bladeTip)
                    throw new InvalidOperationException("缺少打刀刀根/刀尖引用：" + path);
                var profiles = actor.Profile.combo.Select((action, index) =>
                    Profile(action, index == 3 ? .105f : .095f, index == 3 ? 1.1f : .92f, .95f)).ToList();
                profiles.Add(Profile(actor.Profile.heavy, .12f, 1.2f, 1f));
                profiles.Add(Profile(actor.Profile.charged, .15f, 1.4f, 1f));
                var sprint = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(SamuraiSprintCombatSetup.SprintActionPath);
                if (sprint) profiles.Add(Profile(sprint, .115f, 1.15f, 1f));
                var effect = contents.GetComponent<SamuraiBladeRibbon>();
                bool changed = false;
                if (!effect)
                {
                    effect = contents.AddComponent<SamuraiBladeRibbon>();
                    effect.Configure(actor, bladeBase, bladeTip, material, profiles.ToArray());
                    changed = true;
                }
                else
                    foreach (var profile in profiles)
                        changed |= EnsureAttackProfile(effect, profile.attack, profile.lifetime, profile.strength, profile.bladeCoverage);
                if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        /// <summary>只补缺失的动作条目；重复安装保留每招窗口、强度、覆盖范围和原材质引用。</summary>
        public static bool EnsureAttackProfile(SamuraiBladeRibbon effect, SamuraiActionDefinition action,
            float lifetime, float strength, float coverage)
        {
            if (!effect || !action || !action.clip) return false;
            var serialized = new SerializedObject(effect);
            var profiles = serialized.FindProperty("profiles");
            for (int i = 0; i < profiles.arraySize; i++)
                if (profiles.GetArrayElementAtIndex(i).FindPropertyRelative("attack").objectReferenceValue == action)
                    return false;
            var defaults = Profile(action, lifetime, strength, coverage);
            int index = profiles.arraySize;
            profiles.InsertArrayElementAtIndex(index);
            var entry = profiles.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("attack").objectReferenceValue = action;
            entry.FindPropertyRelative("emissionWindow").vector2Value = defaults.emissionWindow;
            entry.FindPropertyRelative("lifetime").floatValue = defaults.lifetime;
            entry.FindPropertyRelative("strength").floatValue = defaults.strength;
            entry.FindPropertyRelative("bladeCoverage").floatValue = defaults.bladeCoverage;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(effect);
            return true;
        }

        static SamuraiBladeRibbon.AttackProfile Profile(SamuraiActionDefinition action,
            float lifetime, float strength, float coverage)
        {
            if (!action || !action.clip)
                throw new InvalidOperationException("缺少打刀攻击配置/动画。");
            return new SamuraiBladeRibbon.AttackProfile
            {
                attack = action,
                // 第一版围绕有效挥斩略提前起笔、略延后停止；不改伤害窗口。
                emissionWindow = new Vector2(Mathf.Max(0f, action.damageStart - .02f),
                    Mathf.Min(1f, action.damageEnd + .015f)),
                lifetime = lifetime,
                strength = strength,
                bladeCoverage = coverage
            };
        }
    }
}
