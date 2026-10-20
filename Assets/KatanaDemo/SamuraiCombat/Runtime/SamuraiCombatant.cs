using System;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 角色组合入口：持有场景引用并连接各独立服务。输入/AI只调用一次Tick；
    /// LateUpdate在骨骼求值后提交武器采样。伤害、状态、运动及检测实现均在各自模块。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SamuraiCombatant : MonoBehaviour, ISamuraiDamageReceiver
    {
        // 保留原序列化字段与脚本GUID，现有Prefab/场景引用不需要重建。
        [SerializeField] Animator animator;
        [SerializeField] CharacterController motor;
        [SerializeField] SamuraiCombatProfile profile;
        [SerializeField] Transform weaponBase;
        [SerializeField] Transform weaponTip;
        [SerializeField] int team;
        SamuraiVitals vitals;
        SamuraiDefense defense;
        SamuraiCharacterMotor movement;
        SamuraiAnimationDriver animationDriver;
        SamuraiActionController actions;
        SamuraiDamageResolver damageResolver;
        SamuraiWeaponAttack weapon;
        SamuraiHitStop hitStop;
        SamuraiInputFrame pausedEdges;
        WeaponTrailGizmos diagnostics;
        Func<bool> canContinueSweep;
        bool initialized, evaluatingWeapon;
        int evaluatingAttackSerial;

        public Animator Animator => animator;
        public CharacterController Motor => motor;
        public SamuraiCombatProfile Profile => profile;
        public ISamuraiVitalsView Vitals => vitals;
        public SamuraiCharacterMotor Movement => movement;
        public SamuraiAnimationDriver Animation => animationDriver;
        public ISamuraiActionView Actions => actions;
        public SamuraiWeaponAttack Weapon => weapon;
        public bool IsHitStopped => hitStop != null && hitStop.IsActive;
        public float HitStopRemaining => hitStop != null ? hitStop.Remaining : 0f;
        public Transform DamageRoot => transform;
        public int Team => team;
        public float Health => vitals != null ? vitals.Health : MaxHealth;
        public float Stamina => vitals != null ? vitals.Stamina : MaxStamina;
        public float MaxHealth => profile != null ? Mathf.Max(1f, profile.maxHealth) : 1f;
        public float MaxStamina => profile != null ? Mathf.Max(1f, profile.maxStamina) : 1f;
        public string StateName => actions != null ? actions.StateName : "Idle";
        public float StateNormalizedTime => actions != null ? actions.StateNormalizedTime : 0f;
        public float ChargeNormalizedTime => actions != null ? actions.ChargeNormalizedTime : 0f;
        public float ChargeElapsedSeconds => actions != null ? actions.ChargeElapsedSeconds : 0f;
        public bool IsChargeCriticalWindowOpen => actions != null && actions.IsChargeCriticalWindowOpen;
        public bool IsCriticalAttack => actions != null && actions.IsCriticalAttack;
        public float AttackDamageMultiplier => actions != null ? actions.AttackDamageMultiplier : 1f;
        public int ComboIndex => actions != null ? actions.ComboIndex : -1;
        public bool IsDead => actions != null && actions.IsDead;
        public bool IsAttacking => actions != null && actions.IsAttacking;
        public bool IsGuarding => actions != null && actions.IsGuarding;
        public bool IsInvulnerable => actions != null && actions.IsInvulnerable;
        public bool IsCharging => actions != null && actions.IsCharging;
        public bool IsDodging => actions != null && actions.IsDodging;
        public bool IsParrying => actions != null && actions.IsParrying;
        public bool IsParryWindowOpen => actions != null && actions.IsParryWindowOpen;
        public float ParryCooldownRemaining => actions != null ? actions.ParryCooldownRemaining : 0f;
        public bool CanAct => actions != null && actions.CanAct;
        public bool IsInHitReaction => actions != null && actions.IsInHitReaction;
        public bool CanReceiveDamage => profile != null && isActiveAndEnabled && !IsDead;
        public SamuraiHitResult LastHitResult { get; private set; }
        public event Action<string> StateChanged;
        public event Action<SamuraiHitResult> HitReceived;
        // 原事件保持兼容；DamageDealt支持任何实现受击接口的对象。
        public event Action<SamuraiCombatant, SamuraiHitResult> HitDealt;
        public event Action<ISamuraiDamageReceiver, SamuraiHitResult, Vector3> DamageDealt;
        public event Action Died;
        /// <summary>成功提交暴击蓄力斩并完成武器倍率快照后，每次出招仅触发一次。</summary>
        public event Action CriticalAttackStarted;

        void Awake() { if (profile != null) ResetCombat(); }
        void OnDisable()
        {
            ClearHitStop();
            weapon?.CancelSwing();
            if (diagnostics != null) diagnostics.ClearHistory();
        }
        void Update() { if (Time.timeScale > 0f) AdvanceHitStop(Time.unscaledDeltaTime); }
        void LateUpdate() => EvaluateWeaponContacts();

        public void Configure(Animator targetAnimator, CharacterController targetMotor,
            SamuraiCombatProfile targetProfile, Transform bladeBase, Transform bladeTip, int targetTeam)
        {
            ClearHitStop();
            weapon?.Reset();
            animator = targetAnimator; motor = targetMotor; profile = targetProfile;
            weaponBase = bladeBase; weaponTip = bladeTip; team = targetTeam;
            initialized = false;
            ResetCombat();
        }
        bool Compose()
        {
            if (profile == null) return false;
            if (initialized) return true;
            vitals = new SamuraiVitals(profile);
            defense = new SamuraiDefense(profile);
            movement = new SamuraiCharacterMotor(transform, motor, profile);
            animationDriver = new SamuraiAnimationDriver(animator, transform, profile);
            actions = new SamuraiActionController(transform, profile, movement, animationDriver, vitals, defense);
            damageResolver = new SamuraiDamageResolver(profile, vitals, defense, actions);
            weapon = new SamuraiWeaponAttack(this, weaponBase, weaponTip, profile);
            hitStop = new SamuraiHitStop(animator);
            diagnostics = GetComponent<WeaponTrailGizmos>();
            if (diagnostics != null) diagnostics.Configure(weaponBase, weaponTip, profile.bladeRadius);
            actions.StateChanged += OnActionStateChanged;
            actions.AttackStarted += OnAttackStarted;
            weapon.SweepQueried += OnSweepQueried;
            weapon.ContactResolved += OnContactResolved;
            weapon.HistoryCleared += OnHistoryCleared;
            canContinueSweep = CanContinueSweep;
            initialized = true;
            return true;
        }
        public void ResetCombat()
        {
            ClearHitStop();
            if (!Compose()) return;
            weapon.Reset(); movement.ResetPose(); vitals.Reset();
            LastHitResult = SamuraiHitResult.Ignored;
            actions.Reset();
        }
        public void Tick(SamuraiInputFrame input, float dt)
        {
            if (!isActiveAndEnabled || !Compose()) return;
            if (input.resetPressed) { ResetCombat(); return; }
            if (IsHitStopped)
            {
                pausedEdges.lightPressed |= input.lightPressed;
                pausedEdges.heavyPressed |= input.heavyPressed;
                pausedEdges.heavyReleased |= input.heavyReleased;
                pausedEdges.dodgePressed |= input.dodgePressed;
                pausedEdges.parryPressed = input.guardHeld && (pausedEdges.parryPressed || input.parryPressed);
                return;
            }
            input.lightPressed |= pausedEdges.lightPressed;
            input.heavyPressed |= pausedEdges.heavyPressed;
            input.heavyReleased |= pausedEdges.heavyReleased;
            input.dodgePressed |= pausedEdges.dodgePressed;
            input.parryPressed |= input.guardHeld && pausedEdges.parryPressed;
            pausedEdges = default;
            actions.Tick(input, dt);
        }
        public void ApplyHitStop(float seconds)
        {
            if (isActiveAndEnabled && Compose()) hitStop.Request(seconds);
        }
        /// <summary>局部停顿的真实时间推进入口，不改变战斗Clock或全局Time.timeScale。</summary>
        public void AdvanceHitStop(float unscaledDelta) => hitStop?.Advance(unscaledDelta);
        public void ClearHitStop() { hitStop?.Clear(); pausedEdges = default; }
        public void CancelInput() { pausedEdges = default; if (Compose()) actions.CancelInput(); }
        public void FaceDirection(Vector3 direction, float dt) { if (!IsHitStopped && Compose()) movement.FaceDirection(direction, dt); }

        /// <summary>通常由LateUpdate调用；手动模拟时须在Animator.Update和Physics同步之后调用。</summary>
        public void EvaluateWeaponContacts()
        {
            if (!initialized || !isActiveAndEnabled || IsHitStopped || !IsAttacking || actions.ActiveAction == null || evaluatingWeapon) return;
            evaluatingWeapon = true;
            evaluatingAttackSerial = actions.AttackSerial;
            try
            {
                Vector3 body = motor != null ? transform.TransformPoint(motor.center) : transform.position + Vector3.up;
                weapon.Sample(actions.StateNormalizedTime, actions.ActiveAction, body, canContinueSweep);
            }
            finally { evaluatingWeapon = false; }
        }
        bool CanContinueSweep() => isActiveAndEnabled && IsAttacking && actions.AttackSerial == evaluatingAttackSerial;
        public SamuraiHitResult ReceiveHit(float damage, float staminaDamage, Vector3 attackerPosition,
            SamuraiCombatant attacker, bool unblockable = false) => ReceiveHit(new SamuraiDamageHit
            { Damage = damage, StaminaDamage = staminaDamage, SourcePosition = attackerPosition, Source = attacker, Unblockable = unblockable });
        public SamuraiHitResult ReceiveHit(SamuraiDamageHit hit)
        {
            if (!Compose()) return SamuraiHitResult.Ignored;
            SamuraiHitResult result = damageResolver.Resolve(hit, this, actions.Clock);
            if (result == SamuraiHitResult.Ignored) return result;
            LastHitResult = result;
            HitReceived?.Invoke(result);
            if (result == SamuraiHitResult.Killed) Died?.Invoke();
            return result;
        }
        public void Stagger(float seconds) { if (Compose() && !IsDead) actions.ReactToHit(seconds); }
        void OnActionStateChanged(string state)
        {
            ClearHitStop();
            weapon.CancelSwing();
            StateChanged?.Invoke(state);
        }
        void OnAttackStarted()
        {
            weapon.BeginSwing(actions.AttackDamageMultiplier);
            if (actions.IsCriticalAttack) CriticalAttackStarted?.Invoke();
        }
        void OnSweepQueried(WeaponTrailSweep sweep) { if (diagnostics != null) diagnostics.RecordSweep(sweep); }
        void OnHistoryCleared() { if (diagnostics != null) diagnostics.ClearHistory(); }
        void OnContactResolved(ISamuraiDamageReceiver target, SamuraiHitResult result, Vector3 point)
        {
            if (diagnostics != null) diagnostics.RecordContact(point, result);
            DamageDealt?.Invoke(target, result, point);
            if (target is SamuraiCombatant combatant) HitDealt?.Invoke(combatant, result);
        }
    }
}
