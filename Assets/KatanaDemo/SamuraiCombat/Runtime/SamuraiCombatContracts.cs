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
        // F按下边沿；只有同时持防才会开启及时格挡窗口。
        public bool dodgePressed, guardHeld, resetPressed, parryPressed;
    }

    public enum SamuraiHitResult { Ignored, Dodged, Hit, Blocked, PerfectGuard, GuardBroken, Killed, Parried }
    public enum SamuraiCombatMode { Locomotion, Attack, Dodge, Charge, Guard, GuardHit, Hit, GuardBreak, Dead }

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
        string StateName { get; }
        float Clock { get; }
        float StateNormalizedTime { get; }
        float ChargeElapsedSeconds { get; }
        bool IsChargeCriticalWindowOpen { get; }
        bool IsCriticalAttack { get; }
        float AttackDamageMultiplier { get; }
        bool IsSprinting { get; }
        float SprintElapsedSeconds { get; }
        bool CanAct { get; }
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

    public struct SamuraiDamageHit
    {
        public float Damage, StaminaDamage;
        public float KnockbackDistance, KnockbackSeconds;
        public bool Unblockable;
        public Vector3 SourcePosition, Point;
        public ISamuraiDamageReceiver Source;
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
}
