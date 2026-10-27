using System;
using System.Collections.Generic;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>定向新增09202并配置中架势六连，不重导旧动作或重建Avatar。</summary>
    public static class SamuraiMidSixComboSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        const string Config = Root + "/Config/Nioh3/";
        public static readonly string[] Codes = { "09200", "09201", "09202", "09203", "09204", "09205" };

        public static SamuraiAttackChain CreateChain(IDictionary<string, SamuraiActionDefinition> actions)
            => new SamuraiAttackChain { displayName = "六连 1 → 2 → 3 → 4 → 5 → 6", attacks = Codes.Select(c => actions[c]).ToArray() };

        [MenuItem("OC Design/Samurai/Install Mid Stance Six Hit Combo")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出Play模式。");
            var entries = SamuraiNiohSetup.Entries;
            int added = Array.FindIndex(entries, e => e.code == "09202");
            if (added < 0) throw new InvalidOperationException("缺少09202目录条目。");
            SamuraiNiohSetup.ImportOne(added);
            var actions = entries.ToDictionary(e => e.code,
                e => AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(Config + e.name + ".asset"));
            if (Codes.Any(c => !actions[c])) throw new InvalidOperationException("六连动作未全部导入。");
            var newAction = actions["09202"];
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.FirstOrDefault(s => s.state.name == newAction.stateName).state;
            if (!state) state = machine.AddState(newAction.stateName, new Vector3(1120, 720));
            state.motion = newAction.clip; state.speed = 1f; state.iKOnFeet = false;
            EditorUtility.SetDirty(state); EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            // Source-pose matching suggests these exits; actual damage windows always finish first.
            float[] links = { 27f / 65f, 37f / 80f, 24f / 59f, 25f / 60f, 38f / 71f, .70f };
            float[] blend = { -1f, .055f, .075f, .065f, .10f, .09f };
            float[] distance = { .90f, .70f, .85f, .75f, .90f, .85f };
            float[] start = { .03f, .01f, .02f, .01f, .04f, .03f };
            float[] end = { .25f, .12f, .22f, .16f, .27f, .21f };
            for (int i = 0; i < Codes.Length; i++)
            {
                var action = actions[Codes[i]];
                action.comboLink = links[i]; action.comboBlendSeconds = blend[i];
                action.comboInputStart = .08f;
                action.comboInputEnd = i < 5 ? Mathf.Min(.85f, links[i] + .12f / action.Duration) : .70f;
                action.stepDistance = distance[i]; action.stepStart = start[i]; action.stepEnd = end[i];
                // Normal steps keep both physical capsules; whirlwind retains its separate push policy.
                action.approachEnabled = false; action.pushEnemies = false;
                ConfigureCloseContact(Codes[i], action);
                EditorUtility.SetDirty(action); AssetDatabase.SaveAssetIfDirty(action);
            }
            var mid = AssetDatabase.LoadAssetAtPath<SamuraiStanceDefinition>(Config + "Nioh3_Stance_Mid.asset");
            mid.attackChains = new[] { CreateChain(actions) };
            EditorUtility.SetDirty(mid); AssetDatabase.SaveAssetIfDirty(mid);

            var contents = PrefabUtility.LoadPrefabContents(SamuraiCombatSetup.PlayerPath);
            try
            {
                var ribbon = contents.GetComponent<SamuraiBladeRibbon>();
                if (ribbon && SamuraiBladeVfxSetup.EnsureAttackProfile(ribbon, newAction, .10f, 1f, 1f))
                    PrefabUtility.SaveAsPrefabAsset(contents, SamuraiCombatSetup.PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        public static void ConfigureCloseContact(string code, SamuraiActionDefinition action)
        {
            // Natural playback exposed an inner-blade gap when the capsules touch.
            // Reuse the obstructed, per-window deduplicated sector only for affected cuts.
            if (code != "09200" && code != "09201" && code != "09202" && code != "09203" && code != "09205") return;
            action.closeRangeReach = .42f;
            action.closeRangeHalfAngle = 55f;
            action.closeRangeStart = action.GetDamageWindow(0).x;
            action.closeRangeEnd = action.DamageEnd;
        }
    }
}
