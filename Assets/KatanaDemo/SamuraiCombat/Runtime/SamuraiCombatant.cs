using System;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 角色组合入口：持有场景引用并连接各独立服务。输入/AI只调用一次Tick；
    /// LateUpdate在骨骼求值后提交武器采样。伤害、状态、运动及检测实现均在各自模块。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class SamuraiCombatant : MonoBehaviour, ISamuraiDamageReceiver, ISamuraiDeflectReaction
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
        SamuraiKnockback knockback;
        SamuraiInputFrame pausedEdges;
        WeaponTrailGizmos diagnostics;
        Func<bool> canContinueSweep;
        bool initialized, evaluatingWeapon;
        int rootMotionAttackSerial = -1;
        int suppressedRootMotionAttackSerial = -1;
        float rootMotionNormalized;
        Quaternion rootMotionFacing;
        int evaluatingAttackSerial;
        CharacterController deathCollisionMotor;
        bool deathMotorWasEnabled;

        public Animator Animator => animator;
        public CharacterController Motor => motor;
        public SamuraiCombatProfile Profile => profile;
        public ISamuraiVitalsView Vitals => vitals;
        public SamuraiCharacterMotor Movement => movement;
        public SamuraiAnimationDriver Animation => animationDriver;
        public ISamuraiActionView Actions => actions;
        public SamuraiMartialArtLoadout MartialArts => actions != null ? actions.MartialArts : null;
        public SamuraiWeaponAttack Weapon => weapon;
        public bool IsHitStopped => hitStop != null && hitStop.IsActive;
        public float HitStopRemaining => hitStop != null ? hitStop.Remaining : 0f;
        public bool IsBeingKnockedBack => knockback != null && knockback.IsActive;
        public bool IsSprinting => actions != null && actions.IsSprinting;
        public float SprintElapsedSeconds => actions != null ? actions.SprintElapsedSeconds : 0f;
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
        public bool IsSheathedCharge => actions != null && actions.IsSheathedCharge;
        public SamuraiActionDefinition ChargeReleaseAction => actions != null ? actions.ChargeReleaseAction : null;
        public bool IsChargeCriticalWindowOpen => actions != null && actions.IsChargeCriticalWindowOpen;
        public bool IsCriticalAttack => actions != null && actions.IsCriticalAttack;
        public float AttackDamageMultiplier => actions != null ? actions.AttackDamageMultiplier : 1f;
        public int ComboIndex => actions != null ? actions.ComboIndex : -1;
        public bool IsDead => actions != null && (actions.IsDead || initialized && vitals.Health <= 0f);
        public bool IsAttacking => actions != null && actions.IsAttacking;
        public bool IsGuarding => actions != null && actions.IsGuarding;
        public bool IsInvulnerable => actions != null && actions.IsInvulnerable;
        public bool IsCharging => actions != null && actions.IsCharging;
        public bool IsDodging => actions != null && actions.IsDodging;
        public bool IsParrying => actions != null && actions.IsParrying;
        public bool IsGuardHit => actions != null && actions.IsGuardHit;
        public bool IsDeflected => actions != null && actions.IsDeflected;
        public SamuraiToughness Toughness => actions != null ? actions.Toughness : null;
        public bool IsToughnessBroken => actions != null && actions.IsToughnessBroken;
        public bool IsExecutionVulnerable => actions != null && actions.IsExecutionVulnerable;
        public bool IsExecuting => actions != null && actions.IsExecuting;
        public bool IsBeingExecuted => actions != null && actions.IsBeingExecuted;
        public float ToughnessBreakRemaining => actions != null ? actions.ToughnessBreakRemaining : 0f;
        public bool LastHitResisted => Toughness != null && Toughness.Enabled && Toughness.LastHitResisted;
        public float CurrentStaggerResistance => Toughness != null ? Toughness.Resistance(actions.ActionStaggerResistance) : 0f;
        public float DeflectedRemainingSeconds => actions != null ? actions.DeflectedRemainingSeconds : 0f;
        public bool CanStartDeflect => actions != null && actions.CanStartDeflect;
        public bool IsParryWindowOpen => actions != null && actions.IsParryWindowOpen;
        public float ParryCooldownRemaining => actions != null ? actions.ParryCooldownRemaining : 0f;
        public bool IsKiPulseWindowOpen => actions != null && actions.KiPulse.IsWindowOpen;
        public bool IsPerfectKiPulseWindow => actions != null && actions.KiPulse.IsPerfectWindow;
        public float KiPulsePending => actions != null ? actions.KiPulse.PendingAmount : 0f;
        public bool IsKiPulseFluxWindowOpen => actions != null && actions.KiPulse.IsFluxWindowOpen;
        public bool IsFrostMoonWindowOpen => actions != null && actions.FrostMoon.IsWindowOpen;
        public bool IsFrostMoonAttack => actions != null && actions.IsFrostMoonAttack;
        public float KiPulseRecoverable => actions != null ? actions.KiPulse.RecoverableAmount : 0f;
        public float KiPulseProgress => actions != null ? actions.KiPulse.Progress : 0f;
        public float ArtsGauge => actions != null ? actions.Arts.Gauge : 0f;
        public float ArtsGaugeMaximum => actions != null ? actions.Arts.Maximum : 100f;
        public bool IsArtsReady => actions != null && actions.Arts.IsReady;
        public bool IsArtsActive => actions != null && actions.Arts.IsActive;
        public float ArtsRemainingSeconds => actions != null ? actions.Arts.Remaining : 0f;
        public float ArtsDurationSeconds => actions != null ? actions.Arts.Duration : 0f;
        public bool IsArtsEnhancedAttack => actions != null && actions.IsArtsEnhancedAttack;
        public bool IsArtsActivationAttack => actions != null && actions.IsArtsActivationAttack;
        public bool IsMartialArtAttack => actions != null && actions.IsMartialArtAttack;
        public SamuraiAttackApproach AttackApproach => actions != null ? actions.AttackApproach : null;
        public bool CanAct => actions != null && actions.CanAct;
        public bool IsInHitReaction => actions != null && actions.IsInHitReaction;
        public bool CanReceiveDamage => profile != null && isActiveAndEnabled && !IsDead && !IsExecuting && !IsBeingExecuted;
        public SamuraiHitResult LastHitResult { get; private set; }
        public event Action<string> StateChanged;
        public event Action<SamuraiHitResult> HitReceived;
        /// <summary>已结算的实际攻击接触及来源；含格挡和抗住硬直，不含忽略或闪避。</summary>
        public event Action<SamuraiDamageHit, SamuraiHitResult> AttackReceived;
        /// <summary>仅实际扣血时通知表现层；闪避、普通格挡及弹反不发出。</summary>
        public event Action<SamuraiDamageFeedback> DamageReceived;
        public event Action CombatReset;
        public event Action StaminaInsufficient;
        // 原事件保持兼容；DamageDealt支持任何实现受击接口的对象。
        public event Action<SamuraiCombatant, SamuraiHitResult> HitDealt;
        public event Action<ISamuraiDamageReceiver, SamuraiHitResult, Vector3> DamageDealt;
        public event Action Died;
        /// <summary>成功提交暴击蓄力斩并完成武器倍率快照后，每次出招仅触发一次。</summary>
        public event Action CriticalAttackStarted;
        public event Action<float, bool> KiPulsePerformed;
        public event Action<float, bool> FluxPerformed;
        public event Action<int, bool> FrostMoonRequested;
        /// <summary>新式弹反成功：实际恢复耐力、命中点和来袭来源。每次成功结算一次。</summary>
        public event Action<float, Vector3, ISamuraiDamageReceiver> DeflectPerformed;
        /// <summary>本角色的攻击被弹反且实际进入Deflected；不同于本角色成功弹反别人。</summary>
        public event Action<float> DeflectReceived;
        /// <summary>普通/完美格挡各结算一次，供独立碰撞表现使用，不参与伤害判定。</summary>
        public event Action<SamuraiGuardContact> GuardContacted;

        void Awake() { if (profile != null) ResetCombat(); }
        void OnEnable() { SamuraiBodyCollision.Register(this); }
        void OnDestroy() { SamuraiBodyCollision.Unregister(this); }
        void OnDisable()
        {
            // 停用后旧动作的未消费根位移不能在重新启用时追补；新出招或复位恢复。
            if (actions != null) suppressedRootMotionAttackSerial = actions.AttackSerial;
            AbortExecution();
            SuspendExecutionPose();
            ClearHitStop();
            actions?.CancelInput();
            actions?.ClearKiPulse();
            actions?.Arts.Reset();
            knockback?.Clear();
            weapon?.CancelSwing();
            if (diagnostics != null) diagnostics.ClearHistory();
        }
        void Update() { if (Time.timeScale > 0f) AdvanceHitStop(Time.unscaledDeltaTime); }
        // Esc 设置暂停期间冻结命中查询，避免静止的攻击窗口继续结算接触。
        void LateUpdate() { EvaluateExecutionPose(); if (Time.timeScale > 0f) EvaluateWeaponContacts(); }

        /// <summary>只消费当前根运动动作的Animator增量；保持世界方向、横移与负向回收，不做距离夹紧。</summary>
        public void ApplyAnimationRootMotion(Animator source)
        {
            if (Time.timeScale <= 0f || !isActiveAndEnabled || !initialized || source != animator || actions == null || movement == null
                || IsHitStopped || !actions.IsAttacking || actions.ActiveAction == null
                || actions.AttackSerial == suppressedRootMotionAttackSerial
                || !actions.ActiveAction.useRootMotion) return;
            // 动作独立保存的米制根轨迹作为来源：避开Humanoid质心投影及跨状态参考朝向漂移。
            // 仍随真实Animator求值触发，不依赖Update顺序或自建计时器。
            if (rootMotionAttackSerial != actions.AttackSerial)
            {
                rootMotionAttackSerial = actions.AttackSerial;
                rootMotionNormalized = 0f;
                rootMotionFacing = transform.rotation;
            }
            float normalized = Mathf.Max(rootMotionNormalized, actions.StateNormalizedTime);
            var action = actions.ActiveAction;
            Vector3 delta = rootMotionFacing * (action.RootMotionPosition(normalized)
                - action.RootMotionPosition(rootMotionNormalized));
            rootMotionNormalized = normalized;
            if (!Finite(delta)) return;
            movement.DisplaceRootMotion(delta);
        }

        public void Configure(Animator targetAnimator, CharacterController targetMotor,
            SamuraiCombatProfile targetProfile, Transform bladeBase, Transform bladeTip, int targetTeam)
        {
            ClearHitStop();
            weapon?.Reset();
            RestoreDeathCollision();
            animator = targetAnimator; motor = targetMotor; profile = targetProfile;
            weaponBase = bladeBase; weaponTip = bladeTip; team = targetTeam;
            initialized = false;
            ResetCombat();
        }
        bool Compose()
        {
            if (profile == null) return false;
            // Unity热重载可恢复布尔字段，却不会恢复这些非序列化C#服务。
            if (initialized && vitals != null && actions != null && weapon != null && movement != null
                && knockback != null && hitStop != null && damageResolver != null) return true;
            bool recoveringAfterReload = initialized;
            vitals = new SamuraiVitals(profile);
            defense = new SamuraiDefense(profile);
            movement = new SamuraiCharacterMotor(transform, motor, profile);
            knockback = new SamuraiKnockback(transform, movement);
            animationDriver = new SamuraiAnimationDriver(animator, transform, profile);
            actions = new SamuraiActionController(transform, profile, movement, animationDriver, vitals, defense);
            damageResolver = new SamuraiDamageResolver(profile, vitals, defense, actions);
            weapon = new SamuraiWeaponAttack(this, weaponBase, weaponTip, profile, animator);
            hitStop = new SamuraiHitStop(animator);
            diagnostics = GetComponent<WeaponTrailGizmos>();
            if (diagnostics != null) diagnostics.Configure(weaponBase, weaponTip, profile.bladeRadius);
            actions.StateChanged += OnActionStateChanged;
            actions.AttackStarted += OnAttackStarted;
            actions.StaminaInsufficient += OnStaminaInsufficient;
            actions.KiPulsePerformed += OnKiPulsePerformed;
            actions.FluxPerformed += OnFluxPerformed;
            actions.FrostMoonRequested += OnFrostMoonRequested;
            weapon.SweepQueried += OnSweepQueried;
            weapon.VolumeQueried += OnVolumeQueried;
            weapon.ContactResolved += OnContactResolved;
            weapon.HistoryCleared += OnHistoryCleared;
            canContinueSweep = CanContinueSweep;
            initialized = true;
            if (recoveringAfterReload) actions.Reset();
            return true;
        }
        public void ResetCombat()
        {
            AbortExecution();
            ClearExecutionPose();
            ClearHitStop();
            rootMotionAttackSerial = -1;
            suppressedRootMotionAttackSerial = -1;
            rootMotionNormalized = 0f;
            if (!Compose()) return;
            weapon.Reset(); knockback.Clear(); movement.ResetPose(); vitals.Reset();
            LastHitResult = SamuraiHitResult.Ignored;
            actions.Reset();
            CombatReset?.Invoke();
        }
        public void Tick(SamuraiInputFrame input, float dt)
        {
            if (!isActiveAndEnabled || !Compose()) return;
            if (input.resetPressed) { ResetCombat(); return; }
            SamuraiActionController.NormalizeCommands(ref input);
            if (input.parryPressed && !input.parryHasTimestamp)
            {
                input.parryTimestamp = actions.Clock;
                input.parryHasTimestamp = true;
            }
            if (input.kiPulsePressed && !input.kiPulseHasTimestamp)
            {
                input.kiPulseTimestamp = actions.Clock;
                input.kiPulseHasTimestamp = true;
            }
            if ((input.highStancePressed || input.middleStancePressed) && !input.stanceHasTimestamp)
            {
                input.stanceTimestamp = actions.Clock;
                input.stanceHasTimestamp = true;
            }
            if (IsHitStopped)
            {
                // 被弹反期间的停顿只冻结硬直，不保留能在恢复后自动触发的旧边沿。
                if (IsDeflected) { pausedEdges = default; return; }
                // 只记第一次有效按下的冻结时刻；窗前按键不能在恢复后自动变为残心。
                bool frostChord = profile.frostMoonEnabled && input.kiPulseHeld
                    && SamuraiActionController.RequestedStance(input) >= 0;
                if ((input.kiPulsePressed || frostChord) && IsKiPulseWindowOpen && !pausedEdges.kiPulsePressed)
                {
                    pausedEdges.kiPulsePressed = true;
                    pausedEdges.kiPulseHasTimestamp = true;
                    pausedEdges.kiPulseTimestamp = input.kiPulsePressed ? input.kiPulseTimestamp : input.stanceTimestamp;
                }
                // 只接收合法Q之后的架势边沿；冻结时Clock相同，不能把先切架势后Q倒序补发。
                if ((input.highStancePressed || input.middleStancePressed)
                    && (profile.kiPulseFluxEnabled || profile.frostMoonEnabled)
                    && (IsKiPulseFluxWindowOpen || IsFrostMoonWindowOpen || pausedEdges.kiPulsePressed))
                {
                    if (!pausedEdges.stanceHasTimestamp)
                    {
                        pausedEdges.highStancePressed = input.highStancePressed;
                        pausedEdges.middleStancePressed = !input.highStancePressed && input.middleStancePressed;
                        pausedEdges.stanceHasTimestamp = true;
                        pausedEdges.stanceTimestamp = input.stanceTimestamp;
                        pausedEdges.kiPulseHeld = input.kiPulseHeld || input.kiPulsePressed;
                    }
                    else if (profile.frostMoonEnabled && !pausedEdges.frostMoonFollowUpPressed
                        && (input.kiPulseHeld || input.kiPulsePressed))
                    {
                        pausedEdges.frostMoonFollowUpPressed = true;
                        pausedEdges.frostMoonFollowUpStance = SamuraiActionController.RequestedStance(input);
                        pausedEdges.frostMoonFollowUpTimestamp = input.stanceTimestamp;
                    }
                }
                pausedEdges.lightPressed |= input.lightPressed;
                if (CanAct || IsCharging)
                {
                    pausedEdges.guardLightPressed |= input.guardLightPressed;
                    pausedEdges.guardHeavyPressed |= input.guardHeavyPressed;
                }
                if (CanAct) pausedEdges.sheathePressed |= input.sheathePressed;
                pausedEdges.sheatheReleased |= input.sheatheReleased;
                pausedEdges.heavyPressed |= input.heavyPressed;
                pausedEdges.heavyReleased |= input.heavyReleased;
                pausedEdges.dodgePressed |= input.dodgePressed;
                if (profile.deflectEnabled)
                {
                    if (!input.guardHeld)
                    {
                        pausedEdges.parryPressed = pausedEdges.parryHasTimestamp = false;
                        defense.CancelInput();
                    }
                    else if (input.parryPressed && actions.CanStartDeflect && !pausedEdges.parryPressed)
                    {
                        pausedEdges.parryPressed = pausedEdges.parryHasTimestamp = true;
                        pausedEdges.parryTimestamp = input.parryTimestamp;
                    }
                }
                else pausedEdges.parryPressed = input.guardHeld && (pausedEdges.parryPressed || input.parryPressed);
                return;
            }
            input.lightPressed |= pausedEdges.lightPressed;
            input.guardLightPressed |= pausedEdges.guardLightPressed;
            input.guardHeavyPressed |= pausedEdges.guardHeavyPressed;
            input.sheathePressed |= pausedEdges.sheathePressed;
            input.sheatheReleased |= pausedEdges.sheatheReleased;
            input.heavyPressed |= pausedEdges.heavyPressed;
            input.heavyReleased |= pausedEdges.heavyReleased;
            input.dodgePressed |= pausedEdges.dodgePressed;
            if (input.guardHeld && pausedEdges.parryPressed)
            {
                input.parryPressed = true;
                input.parryHasTimestamp = pausedEdges.parryHasTimestamp;
                input.parryTimestamp = pausedEdges.parryTimestamp;
            }
            if (pausedEdges.kiPulsePressed)
            {
                input.kiPulsePressed = true;
                input.kiPulseHasTimestamp = true;
                input.kiPulseTimestamp = pausedEdges.kiPulseTimestamp;
            }
            if (pausedEdges.stanceHasTimestamp)
            {
                if (!pausedEdges.frostMoonFollowUpPressed && profile.frostMoonEnabled
                    && SamuraiActionController.RequestedStance(input) >= 0 && (input.kiPulseHeld || input.kiPulsePressed))
                {
                    pausedEdges.frostMoonFollowUpPressed = true;
                    pausedEdges.frostMoonFollowUpStance = SamuraiActionController.RequestedStance(input);
                    pausedEdges.frostMoonFollowUpTimestamp = input.stanceTimestamp;
                }
                input.highStancePressed = pausedEdges.highStancePressed;
                input.middleStancePressed = pausedEdges.middleStancePressed;
                input.stanceHasTimestamp = true;
                input.stanceTimestamp = pausedEdges.stanceTimestamp;
                input.kiPulseHeld |= pausedEdges.kiPulseHeld;
            }
            input.frostMoonFollowUpPressed = pausedEdges.frostMoonFollowUpPressed;
            input.frostMoonFollowUpStance = pausedEdges.frostMoonFollowUpStance;
            input.frostMoonFollowUpTimestamp = pausedEdges.frostMoonFollowUpTimestamp;
            pausedEdges = default;
            if (input.executionPressed && TryExecute(FindExecutionTarget())) input = default;
            if (IsExecuting || IsBeingExecuted || IsToughnessBroken) input = default;
            actions.Tick(input, dt);
            AdvanceExecution(dt);
            if (dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt)) knockback.Tick(Mathf.Min(dt, .1f));
        }
        public void ApplyHitStop(float seconds)
        {
            if (isActiveAndEnabled && Compose()) hitStop.Request(seconds);
        }
        public void ApplyReceivedHitStop(float seconds)
        {
            if (!LastHitResisted && !IsToughnessBroken) ApplyHitStop(seconds);
        }
        /// <summary>局部停顿的真实时间推进入口，不改变战斗Clock或全局Time.timeScale。</summary>
        public void AdvanceHitStop(float unscaledDelta) => hitStop?.Advance(unscaledDelta);
        public void ClearHitStop() { hitStop?.Clear(); pausedEdges = default; }
        public void CancelInput() { pausedEdges = default; if (Compose()) actions.CancelInput(); }
        public void FaceDirection(Vector3 direction, float dt) { if (!IsHitStopped && Compose() && !IsDeflected) movement.FaceDirection(direction, dt); }

        /// <summary>AI在每击准备期显式请求有限转向；只旋转后续根增量，不追补或重算已走路径。</summary>
        public float ReorientCommittedAttack(Vector3 direction, float maximumDegrees)
        {
            if (Time.timeScale <= 0f || !isActiveAndEnabled || !initialized || actions == null || !actions.IsAttacking
                || IsHitStopped || IsDeflected || IsToughnessBroken || IsDead
                || actions.AttackSerial == suppressedRootMotionAttackSerial || !Finite(direction)
                || float.IsNaN(maximumDegrees) || float.IsInfinity(maximumDegrees) || maximumDegrees <= 0f) return 0f;
            direction.y = 0f;
            if (direction.sqrMagnitude < .000001f) return 0f;
            Quaternion before = transform.rotation;
            Quaternion after = Quaternion.RotateTowards(before, Quaternion.LookRotation(direction), maximumDegrees);
            Vector3 beforeForward = before * Vector3.forward, afterForward = after * Vector3.forward;
            float turned = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(beforeForward.x, beforeForward.z) * Mathf.Rad2Deg,
                Mathf.Atan2(afterForward.x, afterForward.z) * Mathf.Rad2Deg));
            if (turned <= 0f) return 0f;
            transform.rotation = after;
            if (rootMotionAttackSerial == actions.AttackSerial)
                rootMotionFacing = after * Quaternion.Inverse(before) * rootMotionFacing;
            return turned;
        }

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
            { Damage = damage, StaminaDamage = staminaDamage, SourcePosition = attackerPosition, Source = attacker,
                Point = transform.position + Vector3.up * 1.1f, Unblockable = unblockable });
        public SamuraiHitResult ReceiveHit(SamuraiDamageHit hit)
        {
            if (!Compose()) return SamuraiHitResult.Ignored;
            float staminaBefore = vitals.Stamina;
            float healthBefore = vitals.Health;
            // 在反应动画启动前取刀位；物理胶囊表面的点常在躯干后，不能直接当金属交击位置。
            Vector3 clashPoint;
            bool hasClashPoint = TryGuardClashPoint(hit.Point, out clashPoint);
            bool protectedAttack = !hit.Unblockable && (actions.HasSuperArmor || actions.HasAttackGuard);
            SamuraiHitResult result = damageResolver.Resolve(hit, this, actions.Clock);
            if (result == SamuraiHitResult.Ignored) return result;
            float healthDamage = Mathf.Max(0f, healthBefore - vitals.Health);
            if (result == SamuraiHitResult.Killed && IsDead && deathCollisionMotor != null)
                knockback.Clear();
            else if (IsToughnessBroken || protectedAttack && actions.IsAttacking
                && (result == SamuraiHitResult.Hit || result == SamuraiHitResult.Blocked))
                knockback.Clear();
            else if (result == SamuraiHitResult.Hit || result == SamuraiHitResult.GuardBroken || result == SamuraiHitResult.Killed)
                knockback.Apply(hit.SourcePosition, hit.KnockbackDistance * (LastHitResisted ? profile.resistedKnockbackMultiplier : 1f), hit.KnockbackSeconds);
            else if (result == SamuraiHitResult.Blocked || result == SamuraiHitResult.PerfectGuard)
            {
                int tier = SamuraiGuardReactionSelector.StrengthTier(profile, hit.StaminaDamage);
                float strength = tier == 0 ? profile.guardLightKnockbackMultiplier
                    : tier == 2 ? profile.guardHeavyKnockbackMultiplier : 1f;
                float distance = profile.guardKnockbackDistance
                    * Mathf.Max(0f, strength)
                    * (result == SamuraiHitResult.PerfectGuard ? Mathf.Clamp01(profile.parryKnockbackMultiplier) : 1f);
                if (LastHitResisted) distance *= profile.resistedKnockbackMultiplier;
                if (distance > 0f)
                    knockback.Apply(hit.SourcePosition, distance, profile.guardKnockbackSeconds, rapidDecay: true);
            }
            LastHitResult = result;
            actions.Arts.RecordDefense(result);
            if (result == SamuraiHitResult.Blocked || result == SamuraiHitResult.PerfectGuard)
                GuardContacted?.Invoke(new SamuraiGuardContact
                {
                    Result = result, Point = hit.Point, ClashPoint = clashPoint, HasClashPoint = hasClashPoint,
                    Source = hit.Source, RestoredStamina = Mathf.Max(0f, vitals.Stamina - staminaBefore),
                    IncomingStaminaDamage = hit.StaminaDamage,
                    ConsumedStamina = Mathf.Max(0f, staminaBefore - vitals.Stamina)
                });
            if (result == SamuraiHitResult.PerfectGuard && profile.deflectEnabled)
                DeflectPerformed?.Invoke(Mathf.Max(0f, vitals.Stamina - staminaBefore), hit.Point, hit.Source);
            HitReceived?.Invoke(result);
            if (result != SamuraiHitResult.Dodged) AttackReceived?.Invoke(hit, result);
            if (healthDamage > 0f)
                DamageReceived?.Invoke(new SamuraiDamageFeedback(hit, result, healthDamage));
            if (result == SamuraiHitResult.Killed) Died?.Invoke();
            return result;
        }
        bool TryGuardClashPoint(Vector3 hitPoint, out Vector3 point)
        {
            point = hitPoint;
            if (weaponBase == null || weaponTip == null || !Finite(hitPoint)) return false;
            Vector3 from = weaponBase.position, blade = weaponTip.position - from;
            if (!Finite(from) || !Finite(blade) || blade.sqrMagnitude < .0001f) return false;
            Vector3 nearest = from + blade * Mathf.Clamp01(Vector3.Dot(hitPoint - from, blade) / blade.sqrMagnitude);
            // 只在同一近身接触附近锚定防御刀，不把远处接触搬到玩家身边。
            if ((nearest - hitPoint).sqrMagnitude > .85f * .85f) return false;
            point = nearest;
            return true;
        }
        static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        public void Stagger(float seconds)
        {
            if (!Compose() || IsDead || IsToughnessBroken || IsExecuting) return;
            if (actions.Toughness.Deflected(actions.Clock)) actions.ReactToToughnessBreak();
            else actions.ReactToHit(seconds);
        }
        public void ReceiveDeflect(float seconds)
        {
            if (!isActiveAndEnabled || !Compose() || IsDead || IsToughnessBroken || IsExecuting || IsBeingExecuted) return;
            knockback.Clear();
            if (actions.Toughness.Deflected(actions.Clock)) actions.ReactToToughnessBreak();
            else
            {
                actions.ReactToDeflected(seconds);
                DeflectReceived?.Invoke(actions.DeflectedRemainingSeconds);
            }
        }
        void OnActionStateChanged(string state)
        {
            HandleExecutionStateChanged();
            ClearHitStop();
            weapon.CancelSwing();
            if (IsDead && profile.disableCollisionOnDeath)
            {
                if (deathCollisionMotor == null && motor != null)
                {
                    deathCollisionMotor = motor;
                    deathMotorWasEnabled = motor.enabled;
                    // 真正移除胶囊阻挡；死亡态由Motor的独立竖直落地处理接管。
                    motor.enabled = false;
                }
            }
            else RestoreDeathCollision();
            StateChanged?.Invoke(state);
        }
        void RestoreDeathCollision()
        {
            if (deathCollisionMotor != null)
                deathCollisionMotor.enabled = deathMotorWasEnabled;
            deathCollisionMotor = null;
        }
        void OnAttackStarted()
        {
            weapon.BeginSwing(actions.AttackDamageMultiplier, actions.ActiveAction);
            if (actions.IsCriticalAttack) CriticalAttackStarted?.Invoke();
        }
        void OnKiPulsePerformed(float restored, bool perfect) => KiPulsePerformed?.Invoke(restored, perfect);
        void OnStaminaInsufficient() => StaminaInsufficient?.Invoke();
        void OnFluxPerformed(float restored, bool pulseWasPerfect) => FluxPerformed?.Invoke(restored, pulseWasPerfect);
        void OnFrostMoonRequested(int stance, bool started) => FrostMoonRequested?.Invoke(stance, started);
        void OnSweepQueried(WeaponTrailSweep sweep) { if (diagnostics != null) diagnostics.RecordSweep(sweep); }
        void OnVolumeQueried(SamuraiAttackVolume region, Vector3 position, Quaternion rotation, float phase)
        { if (diagnostics != null) diagnostics.RecordVolume(region, position, rotation, phase); }
        void OnHistoryCleared() { if (diagnostics != null) diagnostics.ClearHistory(); }
        void OnContactResolved(ISamuraiDamageReceiver target, SamuraiHitResult result, Vector3 point)
        {
            // 接触回调可同步引发弹反或新状态；旧扫掠序号不得减速下一招。
            actions.NotifyAttackContact(evaluatingAttackSerial, result);
            actions.Arts.RecordAttack(actions.AttackSerial, result);
            if (diagnostics != null) diagnostics.RecordContact(point, result);
            DamageDealt?.Invoke(target, result, point);
            if (target is SamuraiCombatant combatant) HitDealt?.Invoke(combatant, result);
        }
    }
}
