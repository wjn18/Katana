using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>武技装备草稿服务。界面和暂停由 SamuraiSettingsUI 管理，本组件不绘制界面或监听按键。</summary>
    [DisallowMultipleComponent]
    public sealed class SamuraiMartialArtMenu : MonoBehaviour
    {
        public SamuraiCombatant actor;
        public SamuraiPlayerInput input;
        // 保留旧 prefab 的序列化字段；新界面统一使用 Canvas 缩放。
        [Range(.75f, 2f)] public float uiScale = 1f;
        SamuraiMartialArtLoadout draft;
        bool mayEdit;
        int selectedStanceIndex;
        SamuraiMartialArtSlot selectedSlot;
        public bool IsOpen => draft != null;
        public SamuraiMartialArtLoadout Draft => draft;
        public int SelectedStanceIndex => selectedStanceIndex;
        public SamuraiMartialArtSlot SelectedSlot => selectedSlot;
        public bool CanEditDraft => IsOpen && mayEdit && actor != null && actor.CanAct && !actor.IsDead;

        public void Configure(SamuraiCombatant combatant, SamuraiPlayerInput playerInput)
        {
            DiscardDraft();
            actor = combatant;
            input = playerInput;
        }
        void Awake()
        {
            if (actor == null) actor = GetComponent<SamuraiCombatant>();
            if (input == null) input = GetComponent<SamuraiPlayerInput>();
        }
        /// <summary>可以在零时标创建只读草稿；只有打开时可行动的角色可以修改装备。</summary>
        public bool BeginDraft(bool allowChanges = true)
        {
            if (IsOpen) return true;
            if (!isActiveAndEnabled || !Application.isPlaying || actor == null || actor.MartialArts == null ||
                actor.Profile == null || actor.Profile.stances == null || actor.Profile.stances.Length == 0) return false;
            draft = new SamuraiMartialArtLoadout(actor.Profile, false);
            for (int stance = 0; stance < draft.StanceCount; stance++)
                for (int slot = 0; slot < SamuraiMartialArtLoadout.SlotCount; slot++)
                    draft.TrySet(stance, (SamuraiMartialArtSlot)slot, actor.MartialArts.Get(stance, (SamuraiMartialArtSlot)slot));
            mayEdit = allowChanges && actor.CanAct && !actor.IsDead;
            selectedStanceIndex = Mathf.Clamp(actor.Actions != null ? actor.Actions.StanceIndex : 0, 0, draft.StanceCount - 1);
            selectedSlot = SamuraiMartialArtSlot.ChargedHeavy;
            return true;
        }
        // 兼容旧调用方；这些 API 只负责草稿生命周期，不改变全局时标和鼠标。
        public bool TryOpen() => IsOpen || actor != null && actor.CanAct && !actor.IsDead && BeginDraft();
        public bool SelectStance(int index)
        {
            if (!IsOpen || index < 0 || index >= draft.StanceCount || actor.Profile.stances[index] == null) return false;
            selectedStanceIndex = index;
            return true;
        }
        public bool SelectSlot(SamuraiMartialArtSlot slot)
        {
            if (!IsOpen || (int)slot < 0 || (int)slot >= SamuraiMartialArtLoadout.SlotCount) return false;
            selectedSlot = slot;
            return true;
        }
        public bool SelectCandidate(SamuraiMartialArtSlot slot, SamuraiActionDefinition action)
        {
            if (!CanEditDraft || SamuraiMartialArtCommands.IsFixedStrongBranch(actor.Profile.stances[selectedStanceIndex], slot)) return false;
            return draft.TrySet(selectedStanceIndex, slot, action);
        }
        public void RestoreDefaults() { if (CanEditDraft) draft.ResetDefaults(); }
        public bool ApplyDraft(bool save = true)
        {
            if (!CanEditDraft || actor.MartialArts == null || actor.MartialArts.StanceCount != draft.StanceCount) return false;
            // 全部验证后整体提交，避免配置变更时只应用一部分。
            for (int stance = 0; stance < draft.StanceCount; stance++)
                for (int slot = 0; slot < SamuraiMartialArtLoadout.SlotCount; slot++)
                    if (!actor.MartialArts.IsCompatible(stance, (SamuraiMartialArtSlot)slot,
                        draft.Get(stance, (SamuraiMartialArtSlot)slot))) return false;
            for (int stance = 0; stance < draft.StanceCount; stance++)
                for (int slot = 0; slot < SamuraiMartialArtLoadout.SlotCount; slot++)
                    actor.MartialArts.TrySet(stance, (SamuraiMartialArtSlot)slot, draft.Get(stance, (SamuraiMartialArtSlot)slot));
            if (save) actor.MartialArts.Save();
            return true;
        }
        public bool ApplyAndClose(bool save = true)
        {
            if (!ApplyDraft(save)) return false;
            DiscardDraft();
            return true;
        }
        public void DiscardDraft() { draft = null; mayEdit = false; }
        public void CancelAndClose() => DiscardDraft();
        void OnDisable() => DiscardDraft();
    }
}
