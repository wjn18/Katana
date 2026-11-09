using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>定向接入架势命令槽；不重新导入动画，不改既有高架势招式数值。</summary>
    public static class SamuraiCommandSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        const string Config = Root + "/Config/Nioh3";

        [MenuItem("Katana/Samurai/Player/Install Command Slots And Customization")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出Play模式。");
            var profile = Required<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            var actions = SamuraiNiohSetup.Entries.ToDictionary(e => e.code,
                e => Required<SamuraiActionDefinition>(Config + "/" + e.name + ".asset"));
            var high = Required<SamuraiStanceDefinition>(Config + "/Nioh3_Stance_High.asset");
            var mid = Required<SamuraiStanceDefinition>(Config + "/Nioh3_Stance_Mid.asset");
            if (profile.combo == null || profile.combo.Length != 4 || profile.combo.Any(a => !a))
                throw new InvalidOperationException("原高架势四连资源不完整。");
            bool nativeHigh = SamuraiNativeHighSetup.ApplyPreparedAttacks(profile, high);
            if (!nativeHigh)
                high.attackChains = new[] { new SamuraiAttackChain { displayName = "普通攻击 1 → 2 → 3 → 4", attacks = profile.combo.ToArray() } };
            mid.attackChains = new[] { SamuraiMidSixComboSetup.CreateChain(actions) };
            if (!nativeHigh) high.strongAttack = actions["05180"];
            high.chargedStrongAttack = null;
            mid.strongAttack = actions["09600"];
            mid.chargedStrongAttack = null;
            Slot(high, SamuraiMartialArtSlot.GuardLight, actions["09300"]);
            Slot(high, SamuraiMartialArtSlot.GuardHeavy, actions["16000"]);
            Slot(high, SamuraiMartialArtSlot.ComboFinisher, actions["07600"]);
            Slot(high, SamuraiMartialArtSlot.HoldHeavy, actions["05080"]);
            Slot(mid, SamuraiMartialArtSlot.GuardLight, actions["05160"]);
            Slot(mid, SamuraiMartialArtSlot.GuardHeavy, actions["09210"]);
            Slot(mid, SamuraiMartialArtSlot.ComboFinisher, null);
            EditorUtility.SetDirty(high); AssetDatabase.SaveAssetIfDirty(high);
            EditorUtility.SetDirty(mid); AssetDatabase.SaveAssetIfDirty(mid);

            Approach(actions["09700"], 1.2f, .04f, .20f);
            Approach(actions["09701"], .9f, .01f, .12f);
            Approach(actions["09702"], 1.1f, .04f, .23f);

            var contents = PrefabUtility.LoadPrefabContents(SamuraiCombatSetup.PlayerPath);
            try
            {
                var actor = contents.GetComponent<SamuraiCombatant>();
                var input = contents.GetComponent<SamuraiPlayerInput>();
                var menu = contents.GetComponent<SamuraiMartialArtMenu>();
                if (!menu) menu = contents.AddComponent<SamuraiMartialArtMenu>();
                menu.Configure(actor, input);
                PrefabUtility.SaveAsPrefabAsset(contents, SamuraiCombatSetup.PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            RestoreOriginalIaiAndWhirlwindTiming();
            SamuraiMidStrongComboSetup.Configure();
        }

        public static void RestoreOriginalIaiAndWhirlwindTiming()
        {
            ConfigureStandaloneHoldSkills();
            var whirlwind = Required<SamuraiActionDefinition>(Config + "/Nioh3_High_WhirlwindSlash_05080.asset");
            SamuraiNiohSetup.ConfigureWhirlwindMoveWindow(whirlwind);
            EditorUtility.SetDirty(whirlwind); AssetDatabase.SaveAssetIfDirty(whirlwind);
        }

        public static void ConfigureStandaloneHoldSkills()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出Play模式。");
            var originalIai = Required<SamuraiActionDefinition>(Root + "/Config/Charged.asset");
            originalIai.displayName = "居合斩（原版）";
            originalIai.chargeStateName = "Charge";
            EditorUtility.SetDirty(originalIai); AssetDatabase.SaveAssetIfDirty(originalIai);
            foreach (string stanceId in new[] { "High", "Mid" })
            {
                var stance = Required<SamuraiStanceDefinition>(Config + "/Nioh3_Stance_" + stanceId + ".asset");
                var definitions = stance.martialArtSlots ?? new SamuraiMartialArtSlotDefinition[0];
                var held = definitions.FirstOrDefault(d => d != null && d.slot == SamuraiMartialArtSlot.ChargedHeavy);
                if (held == null)
                {
                    held = new SamuraiMartialArtSlotDefinition {
                        slot = SamuraiMartialArtSlot.ChargedHeavy,
                        defaultAction = originalIai
                    };
                    definitions = definitions.Concat(new[] { held }).ToArray();
                }
                held.defaultAction = originalIai;
                held.candidates = new[] { originalIai };
                // Reserve Q+Space. Existing per-player choices are migrated by the loadout service.
                var sheathed = definitions.FirstOrDefault(d => d != null && d.slot == SamuraiMartialArtSlot.Sheathed);
                if (sheathed == null)
                {
                    sheathed = new SamuraiMartialArtSlotDefinition { slot = SamuraiMartialArtSlot.Sheathed };
                    definitions = definitions.Concat(new[] { sheathed }).ToArray();
                }
                sheathed.defaultAction = null;
                sheathed.candidates = new SamuraiActionDefinition[0];
                stance.martialArtSlots = definitions;
                EditorUtility.SetDirty(stance); AssetDatabase.SaveAssetIfDirty(stance);
            }
        }

        static void Slot(SamuraiStanceDefinition stance, SamuraiMartialArtSlot slot, SamuraiActionDefinition action)
        {
            var definitions = stance.martialArtSlots ?? new SamuraiMartialArtSlotDefinition[0];
            var definition = definitions.FirstOrDefault(d => d != null && d.slot == slot);
            if (definition == null)
            {
                definition = new SamuraiMartialArtSlotDefinition { slot = slot };
                stance.martialArtSlots = definitions.Concat(new[] { definition }).ToArray();
            }
            // Keep future compatible choices; installing this feature does not erase a catalog extension.
            definition.defaultAction = action;
            definition.candidates = (definition.candidates ?? new SamuraiActionDefinition[0])
                .Where(a => a != null).Concat(new[] { action }).Distinct().ToArray();
        }

        static void Approach(SamuraiActionDefinition action, float distance, float start, float end)
        {
            action.stepDistance = distance; action.stepStart = start; action.stepEnd = end;
            action.approachEnabled = true; action.approachStopGap = .35f; action.approachSlowSpeed = .25f;
            EditorUtility.SetDirty(action); AssetDatabase.SaveAssetIfDirty(action);
        }

        static T Required<T>(string path) where T : UnityEngine.Object
        {
            var result = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!result) throw new InvalidOperationException("缺少资源：" + path);
            return result;
        }
    }
}
