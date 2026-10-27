using System;
using UnityEngine;

namespace OCDesign.Samurai
{
    // HoldHeavy retains its serialized value for saved equipment; it now means a held combo finisher.
    public enum SamuraiMartialArtSlot
    {
        GuardLight = 0, GuardHeavy = 1, ComboFinisher = 2, HoldHeavy = 3, Sheathed = 4, ChargedHeavy = 5
    }

    [Serializable]
    public sealed class SamuraiMartialArtSlotDefinition
    {
        public SamuraiMartialArtSlot slot;
        public SamuraiActionDefinition defaultAction;
        [Tooltip("只列出此架势、此指令允许装备的武技；默认武技自动属于候选。")]
        public SamuraiActionDefinition[] candidates = new SamuraiActionDefinition[0];
    }

    [Serializable]
    public sealed class SamuraiAttackChain
    {
        public string displayName;
        [Tooltip("按点击顺序播放。独立招式只放一项；不根据文件编号自动推断连段。")]
        public SamuraiActionDefinition[] attacks = new SamuraiActionDefinition[0];
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Attack group" : displayName;
    }

    [Serializable]
    public sealed class SamuraiNormalToStrongRoute
    {
        public SamuraiActionDefinition sourceAttack;
        [Tooltip("目标强击在 strongAttackChain 中的下标；0 为第一击。")]
        [Min(0)] public int strongIndex;
        [Tooltip("开启时完整播放原普通攻击及收势，再从目标强击起手。")]
        public bool completeSource = true;
        [Tooltip("提前分支的原动作归一化接点；不会早于原动作最后命中窗口结束。")]
        [Range(0f, 1f)] public float linkNormalizedTime = 1f;
        [Min(0f)] public float blendSeconds = .12f;
        [Tooltip("错过提前接点时，收势后从强击第一击重新起手的混合秒数。")]
        [Min(0f)] public float lateRecoveryBlendSeconds = .12f;
        [Tooltip("目标片段的起始归一化时间。通常为0；只允许跳过命中/位移之前的准备帧。")]
        [Range(0f, 1f)] public float startNormalizedTime;
    }

    [Serializable]
    public sealed class SamuraiStanceEntryTransition
    {
        [Tooltip("只匹配从这个架势进入；不按数组下标或名称猜测来源。")]
        public SamuraiStanceDefinition fromStance;
        public AnimationClip clip;
        public string stateName;
        [Min(0f)] public float blendSeconds = .08f;
    }

    [Serializable]
    public sealed class SamuraiLocomotionEntry
    {
        public AnimationClip clip;
        public string stateName;
    }

    [Serializable]
    public sealed class SamuraiStanceLocomotion
    {
        [Tooltip("前、右前、右、右后、后、左后、左、左前。片段或状态缺失时，仅该方向回退角色原Walk配置。")]
        public SamuraiLocomotionEntry[] walk = new SamuraiLocomotionEntry[8];
        [Tooltip("方向顺序同Walk。片段或状态缺失时，仅该方向回退角色原Run配置。")]
        public SamuraiLocomotionEntry[] run = new SamuraiLocomotionEntry[8];
    }

    [CreateAssetMenu(menuName = "OC Design/Samurai/Stance")]
    public sealed class SamuraiStanceDefinition : ScriptableObject
    {
        public string displayName;
        [Tooltip("可选。未配专用待机时继续使用角色现有Idle，不制造新的架势动画。")]
        public AnimationClip idle;
        public string idleStateName;
        [Tooltip("成功残心时播放的架势动作；为空或缺状态时回退角色配置。只做表现，不增加行动锁定。")]
        public AnimationClip kiPulse;
        public string kiPulseStateName;
        [Tooltip("该架势冬月专用招式。当前留空待动画接入；须为武技并具备有效动画和Animator状态，不回退其他招式。")]
        public SamuraiActionDefinition frostMoon;
        [Tooltip("进入此架势的可中断短表现，不耗耐、不造成命中或锁定行动。残心切架时优先使用，同时保留残心回耐与反馈。")]
        public SamuraiStanceEntryTransition[] entryTransitions = new SamuraiStanceEntryTransition[0];
        [Tooltip("可选的架势八向移动表现。未配置时沿用角色Profile；不改变速度、耗耐或冲刺规则。")]
        public SamuraiStanceLocomotion locomotion;
        public SamuraiAttackChain[] attackChains = new SamuraiAttackChain[0];
        public SamuraiActionDefinition strongAttack;
        [Tooltip("可选的短按右键强击连段；为空时沿用单个 strongAttack。")]
        public SamuraiAttackChain strongAttackChain;
        [Tooltip("按原普攻逐条设置转入强击的时机和目标；不根据编号或姿势自动推断。")]
        public SamuraiNormalToStrongRoute[] normalToStrongRoutes = new SamuraiNormalToStrongRoute[0];
        public SamuraiActionDefinition chargedStrongAttack;
        [Tooltip("固定组合指令与兼容武技；每个角色的装备选择由运行时服务保存。")]
        public SamuraiMartialArtSlotDefinition[] martialArtSlots = new SamuraiMartialArtSlotDefinition[0];
        public SamuraiActionDefinition[] martialArts = new SamuraiActionDefinition[0];
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    }

    /// <summary>每个角色独立保存选择；不修改共享Profile或架势资产。</summary>
    public sealed class SamuraiStanceSelection
    {
        readonly SamuraiCombatProfile profile;
        int stanceIndex = -1, chainIndex, martialArtIndex;

        public SamuraiStanceSelection(SamuraiCombatProfile profile) { this.profile = profile; Reset(); }
        public int StanceIndex => stanceIndex;
        public int AttackChainIndex => chainIndex;
        public int MartialArtIndex => martialArtIndex;
        public SamuraiStanceDefinition Stance => profile != null && profile.stances != null &&
            stanceIndex >= 0 && stanceIndex < profile.stances.Length ? profile.stances[stanceIndex] : null;
        public SamuraiAttackChain AttackChain => Stance != null && Stance.attackChains != null &&
            chainIndex >= 0 && chainIndex < Stance.attackChains.Length ? Stance.attackChains[chainIndex] : null;
        public SamuraiActionDefinition MartialArt => Stance != null && Stance.martialArts != null &&
            martialArtIndex >= 0 && martialArtIndex < Stance.martialArts.Length ? Stance.martialArts[martialArtIndex] : null;

        public void Reset()
        {
            stanceIndex = -1; chainIndex = martialArtIndex = 0;
            if (profile == null || profile.stances == null || profile.stances.Length == 0) return;
            int desired = Mathf.Clamp(profile.defaultStanceIndex, 0, profile.stances.Length - 1);
            if (profile.stances[desired] != null) stanceIndex = desired;
            else
                for (int i = 0; i < profile.stances.Length; i++)
                    if (profile.stances[i] != null) { stanceIndex = i; break; }
            SelectFirstUsableEntries();
        }

        public bool SelectStance(int index)
        {
            if (profile == null || profile.stances == null || index < 0 || index >= profile.stances.Length ||
                profile.stances[index] == null || index == stanceIndex) return false;
            stanceIndex = index; chainIndex = martialArtIndex = 0;
            SelectFirstUsableEntries();
            return true;
        }

        public bool CycleAttackChain(int step)
        {
            int count = Stance != null && Stance.attackChains != null ? Stance.attackChains.Length : 0;
            int next = FindNext(chainIndex, step, count, IsUsableChain);
            if (next == chainIndex) return false;
            chainIndex = next; return true;
        }

        public bool CycleMartialArt(int step)
        {
            int count = Stance != null && Stance.martialArts != null ? Stance.martialArts.Length : 0;
            int next = FindNext(martialArtIndex, step, count, IsUsableMartialArt);
            if (next == martialArtIndex) return false;
            martialArtIndex = next; return true;
        }

        void SelectFirstUsableEntries()
        {
            chainIndex = IsUsableChain(0) ? 0 : FindNext(0, 1,
                Stance != null && Stance.attackChains != null ? Stance.attackChains.Length : 0, IsUsableChain);
            martialArtIndex = IsUsableMartialArt(0) ? 0 : FindNext(0, 1,
                Stance != null && Stance.martialArts != null ? Stance.martialArts.Length : 0, IsUsableMartialArt);
        }

        bool IsUsableChain(int index)
        {
            if (Stance == null || Stance.attackChains == null || index < 0 || index >= Stance.attackChains.Length) return false;
            var chain = Stance.attackChains[index];
            return chain != null && chain.attacks != null && chain.attacks.Length > 0 && chain.attacks[0] != null;
        }

        bool IsUsableMartialArt(int index) => Stance != null && Stance.martialArts != null && index >= 0 &&
            index < Stance.martialArts.Length && Stance.martialArts[index] != null && Stance.martialArts[index].isMartialArt;

        static int FindNext(int current, int step, int count, Func<int, bool> usable)
        {
            if (count <= 0 || step == 0) return current;
            int direction = step > 0 ? 1 : -1;
            for (int offset = 1; offset <= count; offset++)
            {
                int index = ((current + direction * offset) % count + count) % count;
                if (usable(index)) return index;
            }
            return current;
        }
    }
}
