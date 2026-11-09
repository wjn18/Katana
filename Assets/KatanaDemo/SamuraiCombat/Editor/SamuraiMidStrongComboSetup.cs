using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>Only imports the two missing strong attacks; existing avatars and normal chains stay in place.</summary>
    public static class SamuraiMidStrongComboSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        const string Config = Root + "/Config/Nioh3/";
        public static readonly string[] Codes = { "09600", "09601", "09602", "09603" };

        [MenuItem("Katana/Samurai/Player/Install Mid Strong Four And Branches")]
        public static void Install()
        {
            RequireEdit();
            var entries = SamuraiNiohSetup.Entries;
            foreach (string code in new[] { "09601", "09603" })
            {
                int index = Array.FindIndex(entries, e => e.code == code);
                if (index < 0) throw new InvalidOperationException("Missing catalog entry: " + code);
                SamuraiNiohSetup.ImportOne(index);
            }
            var actions = entries.ToDictionary(e => e.code,
                e => Required<SamuraiActionDefinition>(Config + e.name + ".asset"));
            var controller = Required<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            var machine = controller.layers[0].stateMachine;
            foreach (string code in new[] { "09601", "09603" })
            {
                var action = actions[code];
                var state = machine.states.FirstOrDefault(s => s.state.name == action.stateName).state;
                if (!state) state = machine.AddState(action.stateName, new Vector3(1400, code == "09601" ? 720 : 810));
                state.motion = action.clip; state.speed = 1f; state.iKOnFeet = false;
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            Configure();

            var contents = PrefabUtility.LoadPrefabContents(SamuraiCombatSetup.PlayerPath);
            try
            {
                var ribbon = contents.GetComponent<SamuraiBladeRibbon>(); bool changed = false;
                foreach (string code in new[] { "09601", "09603" })
                    if (ribbon) changed |= SamuraiBladeVfxSetup.EnsureAttackProfile(ribbon, actions[code], .10f, 1f, 1f);
                if (changed) PrefabUtility.SaveAsPrefabAsset(contents, SamuraiCombatSetup.PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        public static void Configure()
        {
            RequireEdit();
            var actions = SamuraiNiohSetup.Entries.ToDictionary(e => e.code,
                e => Required<SamuraiActionDefinition>(Config + e.name + ".asset"));
            var mid = Required<SamuraiStanceDefinition>(Config + "Nioh3_Stance_Mid.asset");
            float[] linkSeconds = { 35f / 60f, 64f / 60f, 48f / 60f, 1.75f };
            float[] blends = { .12f, .07f, .075f, .09f };
            for (int i = 0; i < Codes.Length; i++)
            {
                var action = actions[Codes[i]];
                action.comboLink = linkSeconds[i] / action.Duration;
                action.comboInputStart = .035f;
                action.comboInputEnd = i < 3 ? Mathf.Min(.9f, action.comboLink + .15f / action.Duration) : .9f;
                action.comboBlendSeconds = blends[i];
                if (i == 1 || i == 3)
                {
                    // Same total damage/cost as the two existing strong clips. 09601 has two visible cuts.
                    action.damage = i == 1 ? 11f : 22f; action.staminaDamage = i == 1 ? 12.5f : 25f;
                    action.staminaCost = 14f;
                    action.stepDistance = i == 1 ? 1.05f : 1.1f;
                    action.stepStart = .03f; action.stepEnd = i == 1 ? .39f : .36f;
                    action.approachEnabled = false; action.pushEnemies = false;
                    action.cancelStart = Mathf.Max(action.DamageEnd, .55f);
                    action.closeRangeReach = .42f; action.closeRangeHalfAngle = 55f;
                    action.closeRangeStart = action.damageStart; action.closeRangeEnd = action.DamageEnd;
                }
                if (i == 0 || i == 2)
                {
                    // Touching capsules can leave the target inside these fast slashes' sampled blade arcs.
                    // The existing obstructed sector uses the same damage window and per-target deduplication.
                    action.closeRangeReach = .42f; action.closeRangeHalfAngle = 55f;
                    action.closeRangeStart = action.damageStart; action.closeRangeEnd = action.DamageEnd;
                }
                Save(action);
            }
            mid.strongAttack = actions["09600"]; mid.chargedStrongAttack = null;
            mid.strongAttackChain = new SamuraiAttackChain {
                displayName = "强击 1 → 2 → 3 → 4", attacks = Codes.Select(c => actions[c]).ToArray()
            };
            mid.normalToStrongRoutes = SamuraiMidSixComboSetup.Codes.Select((code, i) => new SamuraiNormalToStrongRoute {
                sourceAttack = actions[code], strongIndex = i == 3 ? 3 : i == 4 ? 1 : 0,
                completeSource = i != 3 && i != 4,
                linkNormalizedTime = (i == 3 ? 23f / 60f : i == 4 ? 31f / 60f : actions[code].Duration) / actions[code].Duration,
                blendSeconds = i == 3 ? .16f : i == 4 ? .18f : .12f,
                startNormalizedTime = 0f
            }).ToArray();
            ReplaceSlot(mid, SamuraiMartialArtSlot.GuardLight, actions["05160"]);
            ReplaceSlot(mid, SamuraiMartialArtSlot.GuardHeavy, actions["09210"]);
            ReplaceSlot(mid, SamuraiMartialArtSlot.ComboFinisher, null);
            ReplaceSlot(mid, SamuraiMartialArtSlot.HoldHeavy, null);
            mid.martialArts = new[] { actions["05160"], actions["09210"] };
            Save(mid);
            actions["09400"].displayName = "低架势-上下连击";
            actions["16200"].displayName = "低架势-回身斩";
            Save(actions["09400"]); Save(actions["16200"]);
            SamuraiCommandSetup.ConfigureStandaloneHoldSkills();
        }

        static void ReplaceSlot(SamuraiStanceDefinition stance, SamuraiMartialArtSlot slot, SamuraiActionDefinition action)
        {
            var definitions = stance.martialArtSlots ?? new SamuraiMartialArtSlotDefinition[0];
            var definition = definitions.FirstOrDefault(d => d != null && d.slot == slot);
            if (definition == null)
            {
                definition = new SamuraiMartialArtSlotDefinition { slot = slot };
                stance.martialArtSlots = definitions.Concat(new[] { definition }).ToArray();
            }
            definition.defaultAction = action;
            definition.candidates = action ? new[] { action } : new SamuraiActionDefinition[0];
        }
        static void RequireEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出Play模式。");
        }
        static T Required<T>(string path) where T : UnityEngine.Object
        {
            var result = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!result) throw new InvalidOperationException("Missing asset: " + path);
            return result;
        }
        static void Save(UnityEngine.Object asset) { if (asset is SamuraiActionDefinition action) SamuraiToughnessSetup.InitializeAction(action); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
    }
}
