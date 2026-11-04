using System;
using UnityEngine;

namespace OCDesign.Samurai
{
    public struct SamuraiInputFrame
    {
        public Vector2 move;
        public Vector3 moveWorld;
        // 锁定朝向独立于移动方向；零向量回退到移动方向。
        public Vector3 facingWorld;
        public bool sprint, lightPressed, heavyPressed, heavyHeld, heavyReleased;
        // 架势组合使用按下边沿；冬月开启时，攻击中可按Q+架势预切，当前攻击动画继续。
        public bool highStancePressed, middleStancePressed;
        // 架势边沿保留原战斗时刻，供残心后切架势的短窗口结算。
        public bool stanceHasTimestamp;
        public float stanceTimestamp;
        public bool guardLightPressed, guardHeavyPressed;
        // Q+Space 一旦按出，直到松开 Space 都属于收刀命令，先松 Q 也不会变成闪避。
        public bool sheathePressed, sheatheHeld, sheatheReleased;
        // F按下边沿；只有同时持防才会开启及时格挡窗口。
        public bool dodgePressed, guardHeld, resetPressed, parryPressed;
        public bool executionPressed;
        // 新式弹反保存原始按下时刻；命中停顿不能延迟赠送一个新窗口。
        public bool parryHasTimestamp;
        public float parryTimestamp;
        // Q按下边沿；时间戳使用角色战斗Clock，命中停顿期间也不能升级到更好的时机。
        public bool kiPulsePressed, kiPulseHeld, kiPulseHasTimestamp;
        public float kiPulseTimestamp;
        // HitStop内第二次合法架势组合按输入顺序恢复，不能与首次流转合并。
        public bool frostMoonFollowUpPressed;
        public int frostMoonFollowUpStance;
        public float frostMoonFollowUpTimestamp;
    }

    public enum SamuraiHitResult { Ignored, Dodged, Hit, Blocked, PerfectGuard, GuardBroken, Killed, Parried }
    public enum SamuraiCombatMode { Locomotion, Attack, Dodge, Charge, Guard, GuardHit, Hit, GuardBreak, Dead, Deflected,
        ToughnessBroken, Executing, BeingExecuted, ParryReady }
    public enum SamuraiGuardSide { Automatic, Guard, GuardB }

    public interface ISamuraiVitalsView
    {
        float Health { get; }
        float Stamina { get; }
        float MaxHealth { get; }
        float MaxStamina { get; }
        event Action Changed;
    }

    /// <summary>对外只读状态及合法动作请求；底层状态写入保留在协调器内部。</summary>
    public interface ISamuraiActionView
    {
        SamuraiCombatMode Mode { get; }
        SamuraiActionDefinition ActiveAction { get; }
        int AttackSerial { get; }
        int ComboIndex { get; }
        int StrongComboIndex { get; }
        string StateName { get; }
        float Clock { get; }
        float StateNormalizedTime { get; }
        float ChargeElapsedSeconds { get; }
        bool IsSheathedCharge { get; }
        SamuraiActionDefinition ChargeReleaseAction { get; }
        bool IsChargeCriticalWindowOpen { get; }
        bool IsCriticalAttack { get; }
        bool IsFullyChargedAttack { get; }
        float AttackDamageMultiplier { get; }
        bool IsSprinting { get; }
        float SprintElapsedSeconds { get; }
        bool CanAct { get; }
        SamuraiStanceDefinition CurrentStance { get; }
        SamuraiAttackChain CurrentAttackChain { get; }
        SamuraiAttackChain CurrentStrongAttackChain { get; }
        SamuraiMartialArtLoadout MartialArts { get; }
        int StanceIndex { get; }
        int AttackChainIndex { get; }
        event Action<string> StateChanged;
        bool TryStartAttack(SamuraiActionDefinition action, int comboIndex, Vector3 direction);
    }

    /// <summary>可由角色、可破坏物或其他受击对象实现，不要求继承SamuraiCombatant。</summary>
    public interface ISamuraiDamageReceiver
    {
        Transform DamageRoot { get; }
        int Team { get; }
        bool CanReceiveDamage { get; }
        SamuraiHitResult ReceiveHit(SamuraiDamageHit hit);
        void Stagger(float seconds);
    }

    /// <summary>可选的被弹反反应；未实现的受击对象继续使用通用 Stagger。</summary>
    public interface ISamuraiDeflectReaction
    {
        void ReceiveDeflect(float seconds);
    }

    public struct SamuraiDamageHit
    {
        public float Damage, StaminaDamage;
        public float ToughnessDamage, ImpactStrength;
        public float KnockbackDistance, KnockbackSeconds;
        public bool Unblockable;
        public Vector3 SourcePosition, Point;
        /// <summary>实际接触刀段在本次扫掠中的世界位移，用于判定挥刀来向。</summary>
        public Vector3 BladeMotion;
        /// <summary>刀段没有明确横向运动时，使用攻击配置给出的防御侧回退。</summary>
        public SamuraiGuardSide GuardReactionSide;
        public ISamuraiDamageReceiver Source;
    }

    /// <summary>防御结算后的表现快照；保留物理命中点，刀身锚点只用于金属碰撞效果。</summary>
    public struct SamuraiGuardContact
    {
        public SamuraiHitResult Result;
        public Vector3 Point, ClashPoint;
        public bool HasClashPoint;
        public ISamuraiDamageReceiver Source;
        public float RestoredStamina;
        /// <summary>来袭招式的削精力值；成功弹反不扣精力，用此值衡量冲击。</summary>
        public float IncomingStaminaDamage;
        /// <summary>本次防御实际扣除的精力，不含弹反恢复量。</summary>
        public float ConsumedStamina;
    }

    /// <summary>伤害规则要求的反应接口；不依赖具体状态机或Animator实现。</summary>
    public interface ISamuraiCombatReactions
    {
        bool IsDead { get; }
        bool IsInvulnerable { get; }
        bool IsGuarding { get; }
        void ReactToHit(float seconds);
        void ReactToGuard(bool perfect);
        void ReactToGuardBreak();
        void ReactToDeath();
    }

    /// <summary>可选的命中上下文扩展；旧反应实现继续使用bool入口。</summary>
    public interface ISamuraiGuardReactions
    {
        void ReactToGuard(bool perfect, SamuraiDamageHit hit);
    }

    /// <summary>攻击中的可选防御能力；窗口随真实动作进度，不替换防御输入或受击接口。</summary>
    public interface ISamuraiActionDefense
    {
        bool HasSuperArmor { get; }
        bool HasAttackGuard { get; }
    }

    /// <summary>普通受击的可选命中上下文扩展；旧反应实现继续使用时长入口。</summary>
    public interface ISamuraiHitReactions
    {
        void ReactToHit(SamuraiDamageHit hit);
    }

    public interface ISamuraiToughnessReactions
    {
        SamuraiToughness Toughness { get; }
        float ActionStaggerResistance { get; }
        bool IsToughnessBroken { get; }
        void ReactToToughnessBreak();
    }
}
