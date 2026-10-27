using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>
    /// 定向接入Blender制作的持刀跑步和冲刺攻击，不重建角色、Walk、场景或既有特效。
    /// Install可重复执行；已有动作数值保持。后退初值有单独的显式初始化入口。
    /// </summary>
    public static class SamuraiSprintCombatSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        public const string SprintActionPath = Root + "/Config/SprintAttack.asset";
        static readonly string[] Directions =
            { "", "ForwardRight", "Right", "BackwardRight", "Backward", "BackwardLeft", "Left", "ForwardLeft" };

        [MenuItem("OC Design/Samurai/Install Held Run And Sprint Attack")]
        public static void Install()
        {
            RequireEditMode();
            // 写入前先验证整组输入，避免缺片段时只改了一部分引用。
            var runs = Directions.Select(d => Required<AnimationClip>(Root + "/Animations/Samurai_HeldRun" + d + ".anim")).ToArray();
            var sprintClip = Required<AnimationClip>(Root + "/Animations/Samurai_SprintAttack.anim");
            if (runs.Any(clip => !clip.humanMotion) || !sprintClip.humanMotion)
                throw new InvalidOperationException("持刀奔跑和冲刺攻击必须是已完成Blender处理的Humanoid片段。");
            var player = Required<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            var enemy = Required<SamuraiCombatProfile>(Root + "/Config/TrainingOpponent.asset");
            var controller = Required<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            Required<GameObject>(SamuraiCombatSetup.PlayerPath);
            Required<GameObject>(SamuraiCombatSetup.EnemyPath);
            if (controller.layers.Length == 0)
                throw new InvalidOperationException("SamuraiCombat控制器缺少基础层。");

            var sprint = LoadOrCreateSprint(sprintClip);
            UpdateProfile(player, runs, sprint);
            UpdateProfile(enemy, runs, null);
            var machine = controller.layers[0].stateMachine;
            bool controllerChanged = false;
            for (int i = 0; i < Directions.Length; i++)
                controllerChanged |= SetState(machine, "Run" + Directions[i], runs[i]);
            controllerChanged |= SetState(machine, "SprintAttack", sprintClip);
            if (controllerChanged)
            {
                EditorUtility.SetDirty(machine);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
            }
            AppendSprintRibbon(SamuraiCombatSetup.PlayerPath, sprint);
            AppendSprintRibbon(SamuraiCombatSetup.EnemyPath, sprint);
        }

        /// <summary>旧完整Build入口的最后一步；整组新片段尚未准备好时不做半套迁移。</summary>
        public static bool InstallIfPrepared()
        {
            if (!AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Animations/Samurai_SprintAttack.anim")
                || Directions.Any(d => !AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Animations/Samurai_HeldRun" + d + ".anim")))
                return false;
            Install();
            return true;
        }

        [MenuItem("OC Design/Samurai/Initialize Attack Knockback Defaults")]
        public static void InitializeKnockbackDefaults()
        {
            RequireEditMode();
            // 这是显式重设这两个字段的入口，Install不会调用，也不会覆盖其他动作参数。
            string[] names = { "Combo1", "Combo2", "Combo3", "Combo4", "Heavy", "Charged", "SprintAttack", "TrainingCombo1" };
            float[] distances = { .15f, .20f, .25f, .6f, .9f, 1.4f, .8f, .18f };
            float[] seconds = { .12f, .14f, .16f, .22f, .25f, .3f, .22f, .14f };
            var actions = names.Select(name => Required<SamuraiActionDefinition>(Root + "/Config/" + name + ".asset")).ToArray();
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                if (action.knockbackDistance == distances[i] && action.knockbackSeconds == seconds[i]) continue;
                action.knockbackDistance = distances[i];
                action.knockbackSeconds = seconds[i];
                EditorUtility.SetDirty(action);
                AssetDatabase.SaveAssetIfDirty(action);
            }
        }

        static SamuraiActionDefinition LoadOrCreateSprint(AnimationClip clip)
        {
            var action = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(SprintActionPath);
            if (!action)
            {
                action = ScriptableObject.CreateInstance<SamuraiActionDefinition>();
                action.name = "SprintAttack";
                action.clip = clip; action.stateName = "SprintAttack";
                action.damage = 32f; action.staminaDamage = 44f; action.staminaCost = 20f;
                action.stepDistance = 2.1f; action.stepStart = .02f; action.stepEnd = .48f;
                action.damageStart = .30f; action.damageEnd = .58f;
                action.comboInputStart = .25f; action.comboInputEnd = .85f;
                action.comboLink = .72f; action.cancelStart = .62f;
                action.closeRangeReach = 0f;
                action.knockbackDistance = .8f; action.knockbackSeconds = .22f;
                SamuraiToughnessSetup.InitializeAction(action);
                AssetDatabase.CreateAsset(action, SprintActionPath);
            }
            else if (action.clip != clip || action.stateName != "SprintAttack")
            {
                action.clip = clip; action.stateName = "SprintAttack";
                EditorUtility.SetDirty(action);
            }
            AssetDatabase.SaveAssetIfDirty(action);
            return action;
        }

        static void UpdateProfile(SamuraiCombatProfile profile, AnimationClip[] runs, SamuraiActionDefinition sprint)
        {
            bool changed = profile.run != runs[0] || profile.directionalRun == null
                || !profile.directionalRun.SequenceEqual(runs) || profile.sprintAttack != sprint;
            if (!changed) return;
            profile.run = runs[0];
            profile.directionalRun = (AnimationClip[])runs.Clone();
            profile.sprintAttack = sprint;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        static bool SetState(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            var state = machine.states.FirstOrDefault(item => item.state.name == name).state;
            bool changed = false;
            if (!state)
            {
                int index = machine.states.Length;
                state = machine.AddState(name, new Vector3(240 * (index % 5), 90 * (index / 5)));
                state.writeDefaultValues = true;
                changed = true;
            }
            if (state.motion != clip || state.speed != 1f || state.iKOnFeet)
            {
                state.motion = clip; state.speed = 1f; state.iKOnFeet = false;
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(state);
            return changed;
        }

        static void AppendSprintRibbon(string prefabPath, SamuraiActionDefinition sprint)
        {
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var ribbon = contents.GetComponent<SamuraiBladeRibbon>();
                // 此接入器只扩充已有刀光组件，不能借机重配其他组件或武器挂点。
                if (ribbon && SamuraiBladeVfxSetup.EnsureAttackProfile(ribbon, sprint, .115f, 1.15f, 1f))
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        static T Required<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("缺少接入资源：" + path);
            return asset;
        }

        static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出Play模式后接入冲刺战斗资源。");
        }
    }
}
