using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>定向接入闪避追击。再次执行只修复引用，不重置已经调过的招式参数。</summary>
    public static class SamuraiDodgeAttackSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        public const string AttackPath = Root + "/Config/DodgeAttack.asset";
        public const string ReturnPath = Root + "/Config/DodgeAttackToCombo1.asset";

        [MenuItem("OC Design/Samurai/Install Dodge Follow Up")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出Play模式后接入闪避追击。");
            var attackClip = Required<AnimationClip>(Root + "/Animations/Samurai_DodgeAttack.anim");
            var returnClip = Required<AnimationClip>(Root + "/Animations/Samurai_DodgeAttackToCombo1.anim");
            var player = Required<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            var controller = Required<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            Required<GameObject>(SamuraiCombatSetup.PlayerPath);
            if (!attackClip.humanMotion || !returnClip.humanMotion || controller.layers.Length == 0)
                throw new InvalidOperationException("需要已完成Blender原地化的Humanoid片段与基础动画层。");

            var attack = LoadOrCreate(AttackPath, "DodgeAttack", attackClip, false, player);
            var combo = LoadOrCreate(ReturnPath, "DodgeAttackToCombo1", returnClip, true, player);
            var nativeFirst = SamuraiNativeHighSetup.PreparedFirstAttack();
            var returnTarget = nativeFirst && nativeFirst.IsUsable ? nativeFirst : combo;
            if (player.dodgeAttack != attack || player.dodgeAttackToCombo1 != returnTarget)
            {
                player.dodgeAttack = attack;
                player.dodgeAttackToCombo1 = returnTarget;
                EditorUtility.SetDirty(player);
                AssetDatabase.SaveAssetIfDirty(player);
            }
            var machine = controller.layers[0].stateMachine;
            bool changed = SetState(machine, attack.stateName, attackClip);
            changed |= SetState(machine, combo.stateName, returnClip);
            if (changed)
            {
                EditorUtility.SetDirty(machine);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
            }
            var contents = PrefabUtility.LoadPrefabContents(SamuraiCombatSetup.PlayerPath);
            try
            {
                var ribbon = contents.GetComponent<SamuraiBladeRibbon>();
                if (ribbon)
                {
                    bool ribbonChanged = SamuraiBladeVfxSetup.EnsureAttackProfile(ribbon, attack, .11f, 1.05f, 1f);
                    ribbonChanged |= SamuraiBladeVfxSetup.EnsureAttackProfile(ribbon, combo, .10f, 1f, 1f);
                    if (ribbonChanged) PrefabUtility.SaveAsPrefabAsset(contents, SamuraiCombatSetup.PlayerPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        public static bool InstallIfPrepared()
        {
            if (!AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Animations/Samurai_DodgeAttack.anim")
                || !AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Animations/Samurai_DodgeAttackToCombo1.anim")) return false;
            Install();
            return true;
        }

        static SamuraiActionDefinition LoadOrCreate(string path, string state, AnimationClip clip,
            bool returningToCombo, SamuraiCombatProfile player)
        {
            var action = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(path);
            if (!action)
            {
                action = ScriptableObject.CreateInstance<SamuraiActionDefinition>();
                if (returningToCombo && player.combo != null && player.combo.Length > 0 && player.combo[0])
                    EditorUtility.CopySerialized(player.combo[0], action);
                action.name = state;
                action.toughnessConfigurationVersion = 0;
                action.clip = clip; action.stateName = state;
                action.invulnerableStart = action.invulnerableEnd = 0f;
                action.closeRangeReach = 0f;
                if (returningToCombo)
                {
                    // 此片段包含完整第一击，随后直接进入Combo2；只调整与动作匹配的时序。
                    action.damageStart = .24f; action.damageEnd = .38f;
                    action.comboInputStart = .10f; action.comboInputEnd = .75f;
                    action.comboLink = .42f; action.cancelStart = .42f;
                    action.stepStart = .02f; action.stepEnd = .35f;
                }
                else
                {
                    action.damage = 28f; action.staminaDamage = 38f; action.staminaCost = 18f;
                    action.damageStart = .20f; action.damageEnd = .52f;
                    action.comboInputStart = .15f; action.comboInputEnd = .82f;
                    // Blender回接片段首段匹配反斩57%的姿态与速度。
                    action.comboLink = .57f; action.cancelStart = .58f;
                    action.stepDistance = 1.35f; action.stepStart = .02f; action.stepEnd = .46f;
                    action.knockbackDistance = .45f; action.knockbackSeconds = .18f;
                }
                SamuraiToughnessSetup.InitializeAction(action);
                AssetDatabase.CreateAsset(action, path);
            }
            else if (action.clip != clip || action.stateName != state)
            {
                action.clip = clip; action.stateName = state;
                EditorUtility.SetDirty(action);
            }
            AssetDatabase.SaveAssetIfDirty(action);
            return action;
        }

        static bool SetState(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            var state = machine.states.FirstOrDefault(item => item.state.name == name).state;
            bool changed = !state;
            if (!state)
                state = machine.AddState(name, new Vector3(240 * (machine.states.Length % 5), 90 * (machine.states.Length / 5)));
            if (state.motion != clip || state.speed != 1f || state.iKOnFeet)
            {
                state.motion = clip; state.speed = 1f; state.iKOnFeet = false;
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(state);
            return changed;
        }

        static T Required<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("缺少接入资源：" + path);
            return asset;
        }
    }
}
