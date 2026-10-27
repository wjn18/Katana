using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>角色独立的武技装备；只允许槽位声明的候选，不写共享战斗资产。</summary>
    public sealed class SamuraiMartialArtLoadout
    {
        public const int SlotCount = 6;
        public static readonly SamuraiMartialArtSlot[] DisplaySlots =
        {
            SamuraiMartialArtSlot.ChargedHeavy, SamuraiMartialArtSlot.GuardLight,
            SamuraiMartialArtSlot.GuardHeavy, SamuraiMartialArtSlot.ComboFinisher,
            SamuraiMartialArtSlot.HoldHeavy, SamuraiMartialArtSlot.Sheathed
        };
        readonly SamuraiCombatProfile profile;
        readonly SamuraiActionDefinition[,] equipped;
        readonly string preferenceKey;

        [Serializable] sealed class SavedLoadout
        {
            public int version = 2;
            public SavedEntry[] entries;
        }
        [Serializable] sealed class SavedEntry
        {
            public string stance;
            public int slot;
            public string action;
        }

        public SamuraiMartialArtLoadout(SamuraiCombatProfile profile, bool loadSaved = true,
            string preferenceKeyOverride = null)
        {
            this.profile = profile;
            int count = profile != null && profile.stances != null ? profile.stances.Length : 0;
            equipped = new SamuraiActionDefinition[count, SlotCount];
            preferenceKey = string.IsNullOrEmpty(preferenceKeyOverride)
                ? "OCDesign.Samurai.MartialArts.v1." + StableProfileId(profile) : preferenceKeyOverride;
            if (loadSaved) Reload(); else ResetDefaults();
        }

        public string PreferenceKey => preferenceKey;
        public SamuraiCombatProfile Profile => profile;
        public int StanceCount => equipped.GetLength(0);

        public SamuraiMartialArtSlotDefinition GetDefinition(int stanceIndex, SamuraiMartialArtSlot slot)
        {
            if (!ValidIndex(stanceIndex, slot)) return null;
            var stance = profile.stances[stanceIndex];
            if (stance == null || stance.martialArtSlots == null) return null;
            foreach (var entry in stance.martialArtSlots)
                if (entry != null && entry.slot == slot) return entry;
            return null;
        }

        public SamuraiActionDefinition[] GetOptions(int stanceIndex, SamuraiMartialArtSlot slot)
        {
            var entry = GetDefinition(stanceIndex, slot);
            if (entry == null) return new SamuraiActionDefinition[0];
            var options = new List<SamuraiActionDefinition>();
            if (IsUsable(entry.defaultAction, slot)) options.Add(entry.defaultAction);
            if (entry.candidates != null)
                foreach (var candidate in entry.candidates)
                    if (IsUsable(candidate, slot) && !options.Contains(candidate)) options.Add(candidate);
            return options.ToArray();
        }

        public SamuraiActionDefinition Get(int stanceIndex, SamuraiMartialArtSlot slot)
        {
            if (!ValidIndex(stanceIndex, slot)) return null;
            var value = equipped[stanceIndex, (int)slot];
            return IsCompatible(stanceIndex, slot, value) ? value : null;
        }

        public bool IsCompatible(int stanceIndex, SamuraiMartialArtSlot slot, SamuraiActionDefinition action)
        {
            if (!ValidIndex(stanceIndex, slot)) return false;
            if (action == null) return true;
            if (!IsUsable(action, slot)) return false;
            var entry = GetDefinition(stanceIndex, slot);
            if (entry == null) return false;
            if (entry.defaultAction == action) return true;
            if (entry.candidates != null)
                foreach (var candidate in entry.candidates)
                    if (candidate == action) return true;
            return false;
        }

        public bool TrySet(int stanceIndex, SamuraiMartialArtSlot slot, SamuraiActionDefinition action)
        {
            if (!IsCompatible(stanceIndex, slot, action)) return false;
            equipped[stanceIndex, (int)slot] = action;
            return true;
        }

        public void ResetDefaults()
        {
            for (int stance = 0; stance < StanceCount; stance++)
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    var entry = GetDefinition(stance, (SamuraiMartialArtSlot)slot);
                    equipped[stance, slot] = entry != null && IsUsable(entry.defaultAction, (SamuraiMartialArtSlot)slot)
                        ? entry.defaultAction : null;
                }
        }

        public void Save()
        {
            var entries = new List<SavedEntry>();
            for (int stance = 0; stance < StanceCount; stance++)
            {
                if (profile.stances[stance] == null) continue;
                for (int slot = 0; slot < SlotCount; slot++)
                    entries.Add(new SavedEntry { stance = profile.stances[stance].name, slot = slot,
                        action = ActionId(Get(stance, (SamuraiMartialArtSlot)slot)) });
            }
            PlayerPrefs.SetString(preferenceKey, JsonUtility.ToJson(new SavedLoadout { entries = entries.ToArray() }));
            PlayerPrefs.Save();
        }

        public void Reload()
        {
            ResetDefaults();
            if (!PlayerPrefs.HasKey(preferenceKey)) return;
            SavedLoadout saved;
            try { saved = JsonUtility.FromJson<SavedLoadout>(PlayerPrefs.GetString(preferenceKey)); }
            catch (ArgumentException) { return; }
            if (saved == null || (saved.version != 1 && saved.version != 2) || saved.entries == null) return;
            // 显式的新槽选择（包括卸下）优先；旧收刀槽为空不能抹掉新默认。
            // 槽位候选变更后，旧旋转居合/横斩或低架势动作不再匹配，保留新的默认。
            var hasChargedSelection = new bool[StanceCount];
            foreach (var entry in saved.entries)
            {
                if (entry == null || entry.slot < 0 || entry.slot >= SlotCount) continue;
                for (int stance = 0; stance < StanceCount; stance++)
                {
                    if (profile.stances[stance] == null || profile.stances[stance].name != entry.stance) continue;
                    var slot = (SamuraiMartialArtSlot)entry.slot;
                    if (slot == SamuraiMartialArtSlot.ChargedHeavy) hasChargedSelection[stance] = true;
                    if (string.IsNullOrEmpty(entry.action)) TrySet(stance, slot, null);
                    else
                        foreach (var candidate in GetOptions(stance, slot))
                            if (ActionId(candidate) == entry.action) { TrySet(stance, slot, candidate); break; }
                }
            }
            foreach (var entry in saved.entries)
            {
                if (entry == null || entry.slot != (int)SamuraiMartialArtSlot.Sheathed || string.IsNullOrEmpty(entry.action)) continue;
                for (int stance = 0; stance < StanceCount; stance++)
                {
                    if (hasChargedSelection[stance] || profile.stances[stance] == null ||
                        profile.stances[stance].name != entry.stance) continue;
                    foreach (var candidate in GetOptions(stance, SamuraiMartialArtSlot.ChargedHeavy))
                        if (ActionId(candidate) == entry.action)
                        { TrySet(stance, SamuraiMartialArtSlot.ChargedHeavy, candidate); break; }
                }
            }
        }

        bool ValidIndex(int stanceIndex, SamuraiMartialArtSlot slot) => profile != null && profile.stances != null &&
            stanceIndex >= 0 && stanceIndex < StanceCount && stanceIndex < profile.stances.Length &&
            (int)slot >= 0 && (int)slot < SlotCount;
        // 独立长按只使用角色的原版居合，避免旧候选或旧存档把普通强击重新装入蓄力槽。
        bool IsUsable(SamuraiActionDefinition action, SamuraiMartialArtSlot slot) => action != null &&
            action.isMartialArt && (slot != SamuraiMartialArtSlot.ChargedHeavy ||
                profile != null && action == profile.charged) &&
            action.clip != null && !string.IsNullOrEmpty(action.stateName);
        static string ActionId(SamuraiActionDefinition action) => action != null ? action.name + "|" + action.stateName : "";
        static string StableProfileId(SamuraiCombatProfile value)
        {
            string identity = value != null ? value.name : "none";
            if (value != null && value.stances != null)
                foreach (var stance in value.stances) identity += "|" + (stance != null ? stance.name : "");
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in identity) { hash ^= c; hash *= 16777619; }
                return hash.ToString("x8");
            }
        }
    }

    public static class SamuraiMartialArtCommands
    {
        public static bool HasStrongChain(SamuraiStanceDefinition stance) => stance != null &&
            stance.strongAttackChain != null && stance.strongAttackChain.attacks != null &&
            stance.strongAttackChain.attacks.Length > 0;

        public static bool IsFixedStrongBranch(SamuraiStanceDefinition stance, SamuraiMartialArtSlot slot) =>
            slot == SamuraiMartialArtSlot.ComboFinisher && HasStrongChain(stance) &&
            stance.normalToStrongRoutes != null && stance.normalToStrongRoutes.Length > 0;

        public static string InputLabel(SamuraiMartialArtSlot slot)
        {
            switch (slot)
            {
                case SamuraiMartialArtSlot.GuardLight: return "防御 F + 左键";
                case SamuraiMartialArtSlot.GuardHeavy: return "防御 F + 右键";
                case SamuraiMartialArtSlot.ComboFinisher: return "普攻接段窗口 + 短按右键";
                case SamuraiMartialArtSlot.HoldHeavy: return "普攻接段窗口 + 长按右键（0.25秒）";
                case SamuraiMartialArtSlot.Sheathed: return "Q + 空格 / 暂留空";
                case SamuraiMartialArtSlot.ChargedHeavy: return "长按右键，松开释放";
                default: return "";
            }
        }
    }
}
