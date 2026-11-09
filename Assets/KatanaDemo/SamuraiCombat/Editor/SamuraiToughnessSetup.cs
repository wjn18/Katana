using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OCDesign.EditorTools
{
    /// <summary>定向初始化韧性参数；版本标记保护后续调值与明确的0。</summary>
    public static class SamuraiToughnessSetup
    {
        const string Root = "Assets/KatanaDemo/SamuraiCombat";
        public static string ReportFolder => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../斗笠剑士/动画资源/samurai-toughness/analysis/20261027"));

        [MenuItem("Katana/Samurai/Enemies/Install Enemy Toughness In Current Scene")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先退出Play。");
            foreach (string guid in AssetDatabase.FindAssets("t:SamuraiActionDefinition", new[] { Root + "/Config" }))
            {
                var action = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (InitializeAction(action)) Save(action);
            }
            foreach (string name in new[] { "Enemy1/Enemy1", "Elite1/Elite1", "SamuraiPlayer" })
            {
                var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Root + "/Config/" + name + ".asset");
                if (InitializeProfile(profile)) Save(profile);
            }
            EnsureController("Assets/KatanaDemo/Characters/Enemy1/Animations/Generic/Combat/Enemy1Combat.controller", Root + "/Config/Enemy1/Enemy1.asset");
            EnsureController("Assets/KatanaDemo/Characters/Elite1/Animations/Humanoid/Combat/Elite1Combat.controller", Root + "/Config/Elite1/Elite1.asset");
            var hud = UnityEngine.Object.FindAnyObjectByType<SamuraiHUDView>(FindObjectsInactive.Include);
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path == Root + "/Scenes/SamuraiCombatDemo.unity" && hud != null && EnsureHud(hud))
            {
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            WriteActionReport();
            Debug.Log("Enemy toughness and stagger resistance installed. Execution remains disabled until dedicated animations are available. Existing tuning preserved.");
        }
        public static bool InitializeProfile(SamuraiCombatProfile profile)
        {
            if (profile == null || profile.toughnessConfigurationVersion >= 1) return false;
            bool elite = profile.name == "Elite1";
            bool enemy = elite || profile.name == "Enemy1";
            profile.toughnessEnabled = enemy;
            if (enemy)
            {
                profile.maxToughness = elite ? 240f : 100f;
                profile.toughnessRecoveryDelay = 3f;
                profile.toughnessRecoveryPerSecond = elite ? 36f : 20f;
                profile.toughnessGuardRecoveryMultiplier = .25f;
                profile.toughnessGuardDamageMultiplier = .7f;
                profile.toughnessPerfectGuardDamageMultiplier = .25f;
                profile.deflectedToughnessFraction = .25f;
                profile.toughnessBreakSeconds = elite ? 4f : 3f;
                profile.toughnessBrokenPose = .55f;
                profile.toughnessBreakUsesDeflected = elite;
                profile.executionHealthFraction = elite ? .35f : 1f;
                profile.baseStaggerResistance = elite ? 25f : 10f;
                profile.maximumAccumulatedResistance = elite ? 120f : 90f;
                profile.resistanceDecayDelay = 1.5f;
                profile.resistanceDecayPerSecond = elite ? 40f : 30f;
                profile.chargeStaggerResistanceBonus = elite ? 25f : 15f;
                profile.resistedKnockbackMultiplier = .25f;
            }
            if (profile.name == "SamuraiPlayer")
            {
                profile.executionEnabled = false;
                profile.executionSurfaceRange = 1f;
            }
            profile.toughnessConfigurationVersion = 1;
            return true;
        }
        public static bool InitializeAction(SamuraiActionDefinition action)
        {
            if (action == null || action.toughnessConfigurationVersion >= 1) return false;
            bool attack = action.damage > 0f || action.staminaDamage > 0f;
            action.toughnessDamage = attack ? Mathf.Round(action.staminaDamage * .8f * 100f) / 100f : 0f;
            action.impactStrength = attack ? Impact(action) : 0f;
            action.toughnessDamageByWindow = new float[0]; action.impactStrengthByWindow = new float[0];
            if (attack && action.DamageWindowCount > 1)
            {
                float[] impacts = WindowImpacts(action);
                action.impactStrengthByWindow = impacts;
                float average = impacts.Average();
                action.toughnessDamageByWindow = impacts.Select(v => Mathf.Round(action.toughnessDamage * v / average * 100f) / 100f).ToArray();
            }
            action.staggerResistanceBonus = attack ? ActionBonus(action) : AnimationCurve.Constant(0f, 1f, 0f);
            if (attack && action.name.StartsWith("Elite1_", StringComparison.Ordinal))
            {
                // 大锤头采样保留；只补锤柄内圈，按身体到目标碰撞表面的距离判定。
                action.closeRangeReach = 1.5f; action.closeRangeHalfAngle = 55f;
                action.closeRangeStart = action.GetDamageWindow(0).x; action.closeRangeEnd = action.DamageEnd;
            }
            action.toughnessConfigurationVersion = 1;
            return true;
        }
        static float Impact(SamuraiActionDefinition a)
        {
            string n = a.name;
            switch (n)
            {
                case "Charged": case "Elite1_Heavy": return 70f;
                case "Heavy": case "Enemy1_Heavy": case "Elite1_Sweep": return 50f;
                case "Elite1_SweepReverse": return 55f;
                case "Elite1_Strike": return 40f;
                case "Elite1_Combo01": return 30f;
                case "Elite1_Combo02": case "Elite1_Combo03": case "Elite1_Combo04": return 50f;
                case "Enemy1_Light02": case "Combo2": case "DodgeAttack": return 35f;
                case "Combo3": case "SprintAttack": return 40f;
                case "Combo4": return 50f;
                case "Nioh3_High_Attack2_03110": return 35f;
                case "Nioh3_High_Attack3_03120": return 40f;
                case "Nioh3_High_Attack4_03210": case "Nioh3_High_Strong_03300":
                case "Nioh3_High_RisingSlash_05180": return 50f;
                case "Nioh3_High_Kick_07600": case "Nioh3_High_RisingDragon_16000": return 60f;
                case "Nioh3_Mid_Slash_09600": case "Nioh3_Mid_HorizontalSlash_09602": return 50f;
                case "Nioh3_Mid_StrongAttack2_09601": return 25f;
                case "Nioh3_Mid_StrongAttack4_09603": return 60f;
                case "Nioh3_Mid_SpinAttack_09201": return 15f;
                case "Nioh3_Mid_ComboX2_Thrust_09701": return 35f;
                case "Nioh3_Mid_ComboX3_DownwardSlash_09702": return 40f;
                case "Nioh3_FrostMoon_High_16000": return 50f;
                case "Nioh3_FrostMoon_Mid_16100": case "Nioh3_Mid_SpinningIai_05160":
                case "Nioh3_Mid_TurningSlash_16200": return 40f;
                case "Nioh3_High_Combo_09300": case "Nioh3_High_WhirlwindSlash_05080":
                case "Nioh3_Mid_Sweep_09210": case "Nioh3_Mid_UpDownCombo_09400": return 25f;
                default: return 30f;
            }
        }
        static float[] WindowImpacts(SamuraiActionDefinition action)
        {
            switch (action.name)
            {
                case "Elite1_Combo01": return new[] { 25f, 25f, 30f, 30f, 40f };
                case "Elite1_Combo02": return new[] { 40f, 40f, 70f };
                case "Elite1_Combo03": return new[] { 50f, 40f, 65f, 70f };
                case "Elite1_Combo04": return new[] { 40f, 45f, 50f, 70f };
                case "Elite1_Strike": return new[] { 35f, 45f };
                case "Nioh3_High_Combo_09300": return new[] { 20f, 30f };
                case "Nioh3_Mid_UpDownCombo_09400": return new[] { 20f, 30f };
                default: return Enumerable.Repeat(action.impactStrength, action.DamageWindowCount).ToArray();
            }
        }
        static AnimationCurve ActionBonus(SamuraiActionDefinition action)
        {
            bool elite = action.name.StartsWith("Elite1_", StringComparison.Ordinal);
            float prepare = elite ? 25f : 10f, active = elite ? 40f : 20f;
            var values = new SortedDictionary<float, float> { { 0f, prepare }, { 1f, 0f } };
            float previousEnd = 0f;
            for (int i = 0; i < action.DamageWindowCount; i++)
            {
                var w = action.GetDamageWindow(i);
                if (i > 0)
                {
                    float start = Mathf.Max(previousEnd + .001f, w.x - .15f / action.Duration);
                    if (start < w.x) values[start] = prepare;
                }
                values[w.x] = active; values[w.y] = 0f; previousEnd = w.y;
            }
            var curve = new AnimationCurve(values.Select(v => new Keyframe(v.Key, v.Value)).ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }
            return curve;
        }
        static void EnsureController(string controllerPath, string profilePath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(profilePath);
            if (controller == null || profile == null) throw new InvalidOperationException(controllerPath);
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.Select(v => v.state).FirstOrDefault(v => v.name == "ToughnessBroken");
            var clip = profile.toughnessBreakUsesDeflected && profile.deflected != null ? profile.deflected : profile.guardBreak;
            if (state != null && state.motion == clip && state.speed == 0f) return;
            if (state == null) state = machine.AddState("ToughnessBroken", new Vector3(900f, 660f, 0f));
            state.motion = clip; state.speed = 0f;
            EditorUtility.SetDirty(state); Save(controller);
        }
        public static bool EnsureHud(SamuraiHUDView hud)
        {
            if (hud.targetRoot == null || hud.targetHealthFill == null) return false;
            bool changed = false;
            if (hud.targetToughnessFill == null)
            {
                var track = new GameObject("TargetToughness", typeof(RectTransform), typeof(Image));
                var rect = track.GetComponent<RectTransform>(); rect.SetParent(hud.targetRoot, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
                rect.anchoredPosition = new Vector2(10f, -15f); rect.sizeDelta = new Vector2(180f, 7f);
                var back = track.GetComponent<Image>(); back.sprite = hud.targetHealthFill.sprite;
                back.color = new Color(.04f, .035f, .025f, .95f); back.raycastTarget = false;
                var fill = UnityEngine.Object.Instantiate(hud.targetHealthFill, rect, false);
                fill.name = "TargetToughnessFill"; fill.fillAmount = 1f; fill.color = hud.goldColor;
                fill.rectTransform.anchorMin = fill.rectTransform.anchorMax = fill.rectTransform.pivot = Vector2.up;
                fill.rectTransform.anchoredPosition = new Vector2(2f, -2f); fill.rectTransform.sizeDelta = new Vector2(176f, 3f);
                hud.targetToughnessFill = fill; changed = true;
            }
            if (changed) EditorUtility.SetDirty(hud);
            return changed;
        }
        public static void WriteActionReport()
        {
            var actions = AssetDatabase.FindAssets("t:SamuraiActionDefinition", new[] { Root + "/Config" })
                .Select(g => AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .OrderBy(a => a.name, StringComparer.Ordinal).Select(a => new {
                    name = a.name, label = a.DisplayName, path = AssetDatabase.GetAssetPath(a), attack = a.damage > 0f || a.staminaDamage > 0f,
                    a.damage, a.staminaDamage, a.staminaCost, a.toughnessDamage, a.impactStrength,
                    windows = Enumerable.Range(0, a.DamageWindowCount).Select(i => new { index = i,
                        range = new[] { a.GetDamageWindow(i).x, a.GetDamageWindow(i).y }, toughness = a.ToughnessDamageAt(i), impact = a.ImpactStrengthAt(i) }),
                    resistanceBonus = a.staggerResistanceBonus.keys.Select(k => new { time = k.time, value = k.value }),
                    a.toughnessConfigurationVersion }).ToArray();
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(Path.Combine(ReportFolder, "攻击参数.json"), Newtonsoft.Json.JsonConvert.SerializeObject(actions, Newtonsoft.Json.Formatting.Indented));
        }
        static void Save(UnityEngine.Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
    }
}
