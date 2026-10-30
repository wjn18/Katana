using System;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 行为状态与动作时序。只协调移动/动画/资源服务，不做物理命中查询或读取键鼠。
    /// 输入、AI和未来技能入口都可调用Tick或TryStartAttack。
    /// </summary>
    public sealed class SamuraiActionController : ISamuraiCombatReactions, ISamuraiGuardReactions, ISamuraiHitReactions, ISamuraiActionView, ISamuraiActionDefense, ISamuraiToughnessReactions
    {
        readonly SamuraiCombatProfile profile;
        readonly SamuraiCharacterMotor movement;
        readonly SamuraiAnimationDriver animation;
        readonly SamuraiVitals vitals;
        readonly SamuraiDefense defense;
        readonly Transform root;
        readonly SamuraiDodgeFollowUp dodgeFollowUp = new SamuraiDodgeFollowUp();
        readonly SamuraiStanceSelection stanceSelection;
        readonly SamuraiAttackApproach approach;
        SamuraiActionDefinition queuedFinisher, chargeTapAction, chargeHeldAction;
        SamuraiActionDefinition pendingComboTap, pendingComboHold;
        bool pendingComboHeavy;
        float comboHeavyHeldSeconds;
        SamuraiActionDefinition bufferedStrongAction;
        bool pendingStrongHeavy, strongQueued, strongContinuation;
        float strongHeavyHeldSeconds, strongLinkTime, strongBlendSeconds, strongStartNormalized;
        float strongBranchDeadline, strongRecoveryBlend;
        public const float StrongBranchLateGraceSeconds = .05f;
        public const float ComboHeavyHoldSeconds = .25f;
        bool chargingSheathed;
        bool configurableCharge, chargePendingHold;
        public const float StandaloneHeavyHoldSeconds = .25f;
        const float ChargeTimeTolerance = .00001f;
        float stateTime, stateDuration = 1f, lastActionNormalized;
        float dodgeBufferedUntil = float.NegativeInfinity;
        bool comboQueued, sprintExhausted;
        bool canRaiseDeflectAtInput;
        bool guardBackhand;
        string guardContactVisualState;
        float guardContactVisualUntil = float.NegativeInfinity;
        bool playingKiPulseAnimation;
        SamuraiStanceDefinition kiPulseAnimationStance;
        bool playingStanceTransition;
        SamuraiStanceDefinition stanceTransitionTarget, stanceTransitionSourceThisTick;
        float parryInputTime;
        float stanceInputTime;
        int kiPulseStanceAtInput;
        bool stanceChangedThisTick;
        Vector3 actionDirection;
        public SamuraiCombatMode Mode { get; private set; }
        public SamuraiActionDefinition ActiveAction { get; private set; }
        public int AttackSerial { get; private set; }
        public int ComboIndex { get; private set; } = -1;
        public int StrongComboIndex { get; private set; } = -1;
        public string StateName { get; private set; } = "Idle";
        public float Clock { get; private set; }
        public SamuraiKiPulse KiPulse { get; } = new SamuraiKiPulse();
        public SamuraiFrostMoon FrostMoon { get; } = new SamuraiFrostMoon();
        public SamuraiToughness Toughness { get; }
        public bool IsToughnessBroken => Mode == SamuraiCombatMode.ToughnessBroken || Mode == SamuraiCombatMode.BeingExecuted;
        public bool IsExecutionVulnerable => Mode == SamuraiCombatMode.ToughnessBroken;
        public bool IsExecuting => Mode == SamuraiCombatMode.Executing;
        public bool IsBeingExecuted => Mode == SamuraiCombatMode.BeingExecuted;
        public float ToughnessBreakRemaining => IsExecutionVulnerable ? Mathf.Max(0f, stateDuration - stateTime) : 0f;
        public float ActionStaggerResistance => IsAttacking && ActiveAction != null ? ActiveAction.StaggerResistanceAt(StateNormalizedTime)
            : IsCharging ? Mathf.Max(0f, profile.chargeStaggerResistanceBonus) : 0f;
        public bool IsFrostMoonAttack { get; private set; }
        public bool HasSuperArmor => IsFrostMoonAttack && IsAttacking && ActiveAction != null
            && InDefenseWindow(ActiveAction.frostMoonSuperArmorWindow);
        public bool HasAttackGuard => IsFrostMoonAttack && IsAttacking && ActiveAction != null
            && InDefenseWindow(ActiveAction.frostMoonGuardWindow);
        bool InDefenseWindow(Vector2 window) => window.y > window.x
            && StateNormalizedTime >= Mathf.Clamp01(window.x) && StateNormalizedTime < Mathf.Clamp01(window.y);
        public SamuraiStanceDefinition CurrentStance => stanceSelection.Stance;
        public SamuraiAttackChain CurrentAttackChain => stanceSelection.AttackChain;
        public SamuraiAttackChain CurrentStrongAttackChain => CurrentStance != null ? CurrentStance.strongAttackChain : null;
        public SamuraiMartialArtLoadout MartialArts { get; }
        public int StanceIndex => stanceSelection.StanceIndex;
        public int AttackChainIndex => stanceSelection.AttackChainIndex;
        SamuraiActionDefinition[] CurrentCombo => CurrentStance != null ? CurrentAttackChain?.attacks : profile.combo;
        SamuraiActionDefinition[] CurrentStrongCombo => CurrentStrongAttackChain?.attacks;
        public float StateNormalizedTime => Mode == SamuraiCombatMode.Attack || Mode == SamuraiCombatMode.Dodge
            ? animation.TryProgress(StateName, out float progress) ? Mathf.Clamp01(Mathf.Max(lastActionNormalized, progress)) : lastActionNormalized
            : Mathf.Clamp01(stateTime / Mathf.Max(.02f, stateDuration));
        public float ChargeNormalizedTime => IsCharging && !chargePendingHold ? Mathf.Clamp01(stateTime / Mathf.Max(.1f, profile.chargeSeconds)) : 0f;
        public float ChargeElapsedSeconds => IsCharging && !chargePendingHold ? stateTime : 0f;
        public bool IsSheathedCharge => IsCharging && chargingSheathed;
        public SamuraiActionDefinition ChargeReleaseAction => !IsCharging ? null : configurableCharge
            ? chargePendingHold ? chargeTapAction : chargeHeldAction
            : CanReleaseCharged ? chargeHeldAction : chargeTapAction;
        bool HasFullCharge => IsCharging && !chargePendingHold && stateTime + ChargeTimeTolerance >= Mathf.Max(.1f, profile.chargeSeconds);
        bool CanReleaseCharged => HasFullCharge && animation.CanPlay(chargeHeldAction)
            && vitals.CanAfford(Arts.StaminaCost(chargeHeldAction));
        public bool IsChargeCriticalWindowOpen => CanReleaseCharged && profile.chargeCriticalWindowSeconds > 0f
            && stateTime + ChargeTimeTolerance < Mathf.Max(.1f, profile.chargeSeconds) + profile.chargeCriticalWindowSeconds;
        public bool IsCriticalAttack { get; private set; }
        public bool IsFullyChargedAttack { get; private set; }
        public float AttackDamageMultiplier { get; private set; } = 1f;
        public bool IsSprinting { get; private set; }
        public float SprintElapsedSeconds { get; private set; }
        public bool IsDead => Mode == SamuraiCombatMode.Dead;
        public bool IsAttacking => Mode == SamuraiCombatMode.Attack;
        public bool IsGuarding => defense.GuardHeld && (Mode == SamuraiCombatMode.Guard || Mode == SamuraiCombatMode.GuardHit);
        public bool IsCharging => Mode == SamuraiCombatMode.Charge;
        public bool IsDodging => Mode == SamuraiCombatMode.Dodge;
        public bool IsParrying => Mode == SamuraiCombatMode.GuardHit && StateName == "Parry";
        public bool IsGuardHit => Mode == SamuraiCombatMode.GuardHit && !IsParrying;
        public bool IsDeflected => Mode == SamuraiCombatMode.Deflected;
        public float DeflectedRemainingSeconds => IsDeflected ? Mathf.Max(0f, stateDuration - stateTime) : 0f;
        public bool CanAct => Mode == SamuraiCombatMode.Locomotion || Mode == SamuraiCombatMode.Guard;
        public bool IsInHitReaction => Mode == SamuraiCombatMode.Hit || Mode == SamuraiCombatMode.GuardHit
            || Mode == SamuraiCombatMode.GuardBreak || IsDeflected;
        public bool IsInvulnerable => IsDodging && ActiveAction != null && StateNormalizedTime >= ActiveAction.invulnerableStart
            && StateNormalizedTime < ActiveAction.invulnerableEnd;
        public bool IsParryWindowOpen => defense.WindowOpen(Clock, IsGuarding);
        public bool CanStartDeflect => profile.deflectEnabled && (Mode == SamuraiCombatMode.Locomotion
            || Mode == SamuraiCombatMode.Guard || Mode == SamuraiCombatMode.Charge || Mode == SamuraiCombatMode.GuardHit
            || (Mode == SamuraiCombatMode.Attack && ActiveAction != null && StateNormalizedTime >= ActiveAction.CancelStart));
        public float ParryCooldownRemaining => defense.CooldownRemaining(Clock);
        public event Action<string> StateChanged;
        public event Action AttackStarted;
        public event Action StaminaInsufficient;
        public event Action<float, bool> KiPulsePerformed;
        public event Action<float, bool> FluxPerformed;
        /// <summary>有效冬月指令：目标架势、是否已提交真实攻击。空槽时第二项为false。</summary>
        public event Action<int, bool> FrostMoonRequested;
        public SamuraiArtsProficiency Arts { get; private set; }
        public bool IsArtsEnhancedAttack { get; private set; }
        public bool IsArtsActivationAttack { get; private set; }
        public bool IsMartialArtAttack { get; private set; }
        public SamuraiAttackApproach AttackApproach => approach;

        public SamuraiActionController(Transform root, SamuraiCombatProfile profile, SamuraiCharacterMotor movement,
            SamuraiAnimationDriver animation, SamuraiVitals vitals, SamuraiDefense defense)
        {
            this.root = root; this.profile = profile; this.movement = movement;
            this.animation = animation; this.vitals = vitals; this.defense = defense;
            stanceSelection = new SamuraiStanceSelection(profile);
            MartialArts = new SamuraiMartialArtLoadout(profile);
            approach = new SamuraiAttackApproach(root, movement, profile);
            Arts = new SamuraiArtsProficiency(profile);
            Toughness = new SamuraiToughness(profile);
        }

        public void Reset()
        {
            ClearGuardContactVisual();
            Clock = 0f; dodgeBufferedUntil = float.NegativeInfinity;
            comboQueued = sprintExhausted = false;
            guardBackhand = false;
            stanceSelection.Reset();
            ClearKiPulse();
            Arts.Reset();
            dodgeFollowUp.Cancel();
            ResetSprint();
            defense.Reset(); animation.Reset();
            Toughness.Reset();
            // Serial不回退，避免外部持有旧招式标识后误认为仍有效。
            AttackSerial++;
            Enter(SamuraiCombatMode.Locomotion, IdleStateName, SamuraiAnimationDriver.ClipDuration(IdleClip), true);
        }
        public void CancelInput()
        {
            ClearGuardContactVisual();
            approach.Reset();
            ClearKiPulse();
            comboQueued = false; dodgeBufferedUntil = float.NegativeInfinity;
            queuedFinisher = null;
            ClearComboHeavy();
            ClearStrongHeavy();
            dodgeFollowUp.Cancel();
            ResetSprint();
            defense.CancelInput();
            if (Mode == SamuraiCombatMode.Charge || Mode == SamuraiCombatMode.Guard || playingKiPulseAnimation || playingStanceTransition)
                EnterLocomotion();
        }
        public void Tick(SamuraiInputFrame input, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            dt = Mathf.Min(dt, .1f);
            if (IsToughnessBroken || IsExecuting) input = default;
            NormalizeCommands(ref input);
            kiPulseStanceAtInput = StanceIndex;
            stanceChangedThisTick = false;
            stanceTransitionSourceThisTick = null;
            stanceInputTime = input.stanceHasTimestamp && !float.IsNaN(input.stanceTimestamp)
                && !float.IsInfinity(input.stanceTimestamp) ? Mathf.Min(Clock, input.stanceTimestamp) : Clock;
            // 先按输入读取时的战斗Clock观测动画；本帧dt不能把提前按下的Q升级为完美。
            float kiPulseInputTime = input.kiPulseHasTimestamp && !float.IsNaN(input.kiPulseTimestamp)
                && !float.IsInfinity(input.kiPulseTimestamp) ? Mathf.Min(Clock, input.kiPulseTimestamp) : Clock;
            if (!input.kiPulsePressed && input.kiPulseHeld && RequestedStance(input) >= 0)
                kiPulseInputTime = stanceInputTime;
            parryInputTime = input.parryHasTimestamp && !float.IsNaN(input.parryTimestamp)
                && !float.IsInfinity(input.parryTimestamp) ? Mathf.Min(Clock, input.parryTimestamp) : Clock;
            // 出刀或受伤时无效的F不缓存成恢复后的新窗口；dt越过取消点也不会补发。
            canRaiseDeflectAtInput = CanStartDeflect;
            if (!profile.kiPulseEnabled) ClearKiPulse();
            else if (IsAttacking && ActiveAction != null)
                KiPulse.ObserveAttackProgress(StateNormalizedTime, Clock);
            if (!profile.kiPulseFluxEnabled || profile.kiPulseFluxWindowSeconds <= 0f
                || profile.kiPulseFluxRecoveryFraction <= 0f) KiPulse.ClearFlux();
            KiPulse.Advance(Clock);
            if (!profile.frostMoonEnabled || profile.frostMoonWindowSeconds <= 0f) FrostMoon.Clear();
            FrostMoon.Advance(Clock);
            Clock += dt;
            Arts.Advance(dt);
            float previousNormalized = lastActionNormalized;
            stateTime += dt; lastActionNormalized = StateNormalizedTime;
            defense.ReadInput(input.guardHeld, input.parryPressed);
            Vector3 move = SamuraiCharacterMotor.MoveDirection(input, root);
            Vector3 facing = SamuraiCharacterMotor.FacingDirection(input.facingWorld, move);
            bool preShift = profile.frostMoonEnabled && IsAttacking && !KiPulse.IsWindowOpen
                && (input.kiPulseHeld || input.kiPulsePressed) && RequestedStance(input) >= 0;
            if (CanAct || preShift) stanceChangedThisTick = ReadSelection(input);
            if (!input.sprint) sprintExhausted = false;
            if (input.dodgePressed && !IsDeflected) dodgeBufferedUntil = Clock + Mathf.Max(0f, profile.inputBufferSeconds);
            switch (Mode)
            {
                case SamuraiCombatMode.Dead: movement.MoveCorpse(dt); return;
                case SamuraiCombatMode.ToughnessBroken:
                    movement.Move(Vector3.zero, dt);
                    if (stateTime >= stateDuration) RecoverFromToughnessBreak();
                    break;
                case SamuraiCombatMode.Executing:
                case SamuraiCombatMode.BeingExecuted:
                    movement.Move(Vector3.zero, dt);
                    break;
                case SamuraiCombatMode.Attack: TickAttack(input, move, facing, previousNormalized, dt, kiPulseInputTime); break;
                case SamuraiCombatMode.Dodge: TickDodge(input, facing, previousNormalized, dt); break;
                case SamuraiCombatMode.Charge:
                    movement.Move(Vector3.zero, dt);
                    if (TryBufferedDodge(move)) break;
                    if (TryGuardCommand(input, facing)) break;
                    if (input.guardHeld) { movement.FaceDirection(input.facingWorld, dt); BeginGuard(defense.FreshPress); }
                    else
                    {
                        if (chargePendingHold && stateTime + ChargeTimeTolerance >= StandaloneHeavyHoldSeconds)
                            ActivateHeldCharge();
                        if (chargingSheathed ? input.sheatheReleased : input.heavyReleased) ReleaseCharge(facing);
                    }
                    break;
                case SamuraiCombatMode.Hit:
                case SamuraiCombatMode.GuardBreak:
                    movement.Move(Vector3.zero, dt);
                    if (stateTime >= stateDuration)
                    {
                        if (input.guardHeld) { movement.FaceDirection(input.facingWorld, dt); BeginGuard(false); }
                        else EnterLocomotion();
                    }
                    break;
                case SamuraiCombatMode.Deflected:
                    // 硬直内不缓存攻防闪输入；结束时只恢复持防或待机，不能补发旧按键。
                    dodgeBufferedUntil = float.NegativeInfinity;
                    movement.Move(Vector3.zero, dt);
                    if (stateTime >= stateDuration)
                    {
                        if (input.guardHeld) BeginGuard(false);
                        else EnterLocomotion();
                    }
                    break;
                case SamuraiCombatMode.GuardHit:
                    if (profile.deflectEnabled)
                    {
                        if (stateTime >= stateDuration) TickLocomotion(input, move, facing, dt, kiPulseInputTime);
                        else
                        {
                            TryRaiseGuardWindow(defense.FreshPress);
                            movement.Move(Vector3.zero, dt);
                        }
                    }
                    else
                    {
                        movement.Move(Vector3.zero, dt);
                        if (stateTime >= stateDuration)
                        {
                            if (input.guardHeld) { movement.FaceDirection(input.facingWorld, dt); BeginGuard(false); }
                            else EnterLocomotion();
                        }
                    }
                    break;
                default: TickLocomotion(input, move, facing, dt, kiPulseInputTime); break;
            }
            UpdateGuardContactVisual();
            // 原有攻防闪优先；只在本帧仍可自由行动时消费流转资格。
            if (stanceChangedThisTick && Mode == SamuraiCombatMode.Locomotion) TryPerformFlux();
            if (input.frostMoonFollowUpPressed && Mode == SamuraiCombatMode.Locomotion)
                TryHandleFrostMoon(input, facing);
            float naturallyRestored = vitals.Recover(Clock, dt, Mode != SamuraiCombatMode.Attack && Mode != SamuraiCombatMode.Dodge
                && Mode != SamuraiCombatMode.Charge && Mode != SamuraiCombatMode.GuardBreak && Mode != SamuraiCombatMode.Hit
                && !IsDeflected && !IsToughnessBroken && !IsExecuting, IsGuarding);
            KiPulse.DeductNaturalRecovery(naturallyRestored);
            Toughness.Advance(Clock, dt, Mode == SamuraiCombatMode.Locomotion || Mode == SamuraiCombatMode.Guard,
                IsGuarding, IsDead || IsToughnessBroken || IsExecuting);
            KiPulse.Advance(Clock);
            FrostMoon.Advance(Clock);
        }

        internal static void NormalizeCommands(ref SamuraiInputFrame input)
        {
            if (input.sheathePressed || input.sheatheHeld || input.sheatheReleased)
            {
                input.dodgePressed = input.kiPulsePressed = input.kiPulseHeld = input.kiPulseHasTimestamp = false;
                input.frostMoonFollowUpPressed = false;
                input.lightPressed = input.heavyPressed = input.heavyReleased = false;
                input.guardLightPressed = input.guardHeavyPressed = false;
                if (input.sheathePressed) input.guardHeld = input.parryPressed = false;
                return;
            }
            input.guardLightPressed |= input.guardHeld && input.lightPressed;
            input.guardHeavyPressed |= input.guardHeld && input.heavyPressed;
            if (input.guardLightPressed || input.guardHeavyPressed)
                input.lightPressed = input.heavyPressed = input.heavyReleased = false;
        }

        bool TryGuardCommand(SamuraiInputFrame input, Vector3 facing)
        {
            if (!input.guardLightPressed && !input.guardHeavyPressed) return false;
            var slot = input.guardLightPressed ? SamuraiMartialArtSlot.GuardLight : SamuraiMartialArtSlot.GuardHeavy;
            var action = MartialArts.Get(StanceIndex, slot);
            if (action != null && action.isMartialArt && CommitAttack(action, -1, facing)) return true;
            if (input.guardHeld)
            {
                if (Mode != SamuraiCombatMode.Guard) BeginGuard(defense.FreshPress);
                else TryRaiseGuardWindow(defense.FreshPress);
            }
            else if (IsCharging) EnterLocomotion();
            return true;
        }

        bool BeginCharge(SamuraiActionDefinition tap, SamuraiActionDefinition held, bool sheathed, Vector3 facing)
        {
            if (!animation.CanPlay(tap) || !CanAffordRequestedAction(Arts.StaminaCost(tap))) return false;
            movement.TurnForAction(facing);
            bool selectable = !sheathed && MartialArts.GetDefinition(StanceIndex, SamuraiMartialArtSlot.ChargedHeavy) != null;
            Enter(SamuraiCombatMode.Charge, selectable ? IdleStateName : ChargeState(held), Mathf.Max(.1f, profile.chargeSeconds));
            chargeTapAction = tap; chargeHeldAction = held; chargingSheathed = sheathed;
            configurableCharge = chargePendingHold = selectable;
            dodgeBufferedUntil = float.NegativeInfinity;
            return true;
        }

        string ChargeState(SamuraiActionDefinition action) => action != null
            && !string.IsNullOrWhiteSpace(action.chargeStateName) && animation.HasState(action.chargeStateName)
            ? action.chargeStateName : IdleStateName;

        void ActivateHeldCharge()
        {
            // A short right click never starts the Iai preparation. Once recognized
            // as a hold, releasing keeps the chosen move, including before full charge.
            var tap = chargeTapAction; var held = chargeHeldAction;
            float duration = Mathf.Max(.1f, profile.chargeSeconds);
            float excess = Mathf.Max(0f, stateTime - StandaloneHeavyHoldSeconds);
            Enter(SamuraiCombatMode.Charge, ChargeState(held), duration, normalizedStart: excess / duration);
            chargeTapAction = tap; chargeHeldAction = held; configurableCharge = true;
        }

        void ReleaseCharge(Vector3 facing)
        {
            if (!configurableCharge && HasFullCharge && animation.CanPlay(chargeHeldAction)
                && !vitals.CanAfford(Arts.StaminaCost(chargeHeldAction)))
                StaminaInsufficient?.Invoke();
            var release = ChargeReleaseAction;
            bool full = CanReleaseCharged && release == chargeHeldAction;
            bool critical = full && IsChargeCriticalWindowOpen;
            if (!CommitAttack(release, -1, facing, critical, fullyChargedRelease: full)) EnterLocomotion();
        }

        void TickLocomotion(SamuraiInputFrame input, Vector3 move, Vector3 facing, float dt, float kiPulseInputTime)
        {
            // 收刀与持防组合先消费整组输入；空槽、体力不足也不能落成普通攻击或闪避。
            if (input.sheathePressed)
            {
                var sheathed = MartialArts.Get(StanceIndex, SamuraiMartialArtSlot.Sheathed);
                BeginCharge(sheathed, sheathed, true, facing);
                if (input.sheatheReleased && IsCharging) ReleaseCharge(facing);
                movement.Move(Vector3.zero, dt); return;
            }
            if (TryGuardCommand(input, facing)) { movement.Move(Vector3.zero, dt); return; }
            if (TryBufferedDodge(move)) { movement.Move(Vector3.zero, dt); return; }
            // 只在Q参与时提前判定持防，保留原本不使用残心时的输入优先级。
            if (profile.kiPulseEnabled && input.kiPulsePressed && input.guardHeld)
            {
                movement.FaceDirection(input.facingWorld, dt);
                if (Mode != SamuraiCombatMode.Guard) BeginGuard(defense.FreshPress);
                else TryRaiseGuardWindow(defense.FreshPress);
                movement.Move(move * profile.guardWalkSpeed, dt);
                return;
            }
            if (TryPerformKiPulse(input, kiPulseInputTime))
            {
                if (IsFrostMoonAttack) { movement.Move(Vector3.zero, dt); return; }
                movement.FaceDirection(facing, dt);
                movement.Move(move * profile.walkSpeed, dt);
                return;
            }
            if (TryHandleFrostMoon(input, facing))
            {
                movement.Move(IsFrostMoonAttack ? Vector3.zero : move * profile.walkSpeed, dt);
                return;
            }
            if (input.lightPressed)
            {
                if (dodgeFollowUp.ConsumeGrace(Clock) && CommitAttack(profile.dodgeAttack, -1, facing))
                { movement.Move(Vector3.zero, dt); return; }
                bool sprintReady = IsSprinting && input.sprint && move.sqrMagnitude > .001f
                    && SprintElapsedSeconds >= Mathf.Max(0f, profile.sprintAttackMinRunSeconds);
                var combo = CurrentCombo;
                if ((sprintReady && CommitAttack(profile.sprintAttack, -1, facing))
                    || (combo != null && combo.Length > 0 && CommitAttack(combo[0], 0, facing)))
                { movement.Move(Vector3.zero, dt); return; }
            }
            if (input.heavyPressed)
            {
                var strong = StrongAttackAt(0) ?? (CurrentStance != null ? CurrentStance.strongAttack : profile.heavy);
                var held = MartialArts.GetDefinition(StanceIndex, SamuraiMartialArtSlot.ChargedHeavy) != null
                    ? MartialArts.Get(StanceIndex, SamuraiMartialArtSlot.ChargedHeavy)
                    : CurrentStance != null ? CurrentStance.chargedStrongAttack : profile.charged;
                BeginCharge(strong, held, false, facing);
                movement.Move(Vector3.zero, dt);
                if (input.heavyReleased && IsCharging) ReleaseCharge(facing);
                return;
            }
            if (input.guardHeld)
            {
                movement.FaceDirection(input.facingWorld, dt);
                if (Mode != SamuraiCombatMode.Guard) BeginGuard(defense.FreshPress);
                else TryRaiseGuardWindow(defense.FreshPress);
                movement.Move(move * profile.guardWalkSpeed, dt);
                return;
            }
            if (Mode == SamuraiCombatMode.Guard) EnterLocomotion();
            bool moving = move.sqrMagnitude > .001f;
            float cost = Mathf.Max(0f, profile.sprintStaminaPerSecond) * dt;
            if (!input.sprint || vitals.Stamina >= vitals.MaxStamina * .15f) sprintExhausted = false;
            if (moving && input.sprint && (!vitals.CanAfford(cost) || vitals.Stamina <= .1f))
            {
                if (!sprintExhausted) StaminaInsufficient?.Invoke();
                sprintExhausted = true;
            }
            bool running = moving && input.sprint && !sprintExhausted && vitals.CanAfford(cost) && vitals.Stamina > .1f;
            if (running) { ClearKiPulse(); vitals.Spend(cost, Clock); }
            movement.FaceDirection(facing, dt);
            Vector3 beforeMove = root.position;
            movement.Move(move * (running ? profile.runSpeed : profile.walkSpeed), dt);
            Vector3 traveled = root.position - beforeMove; traveled.y = 0f;
            // 只有确实跑动才积累资格；贴墙空跑或停下不能蓄出冲刺攻击。
            IsSprinting = running && traveled.sqrMagnitude > .000001f;
            SprintElapsedSeconds = IsSprinting ? SprintElapsedSeconds + dt : 0f;
            if (!moving) animation.ResetLocomotionDirection();
            // 先让本帧攻防闪和移动接管；单独切架才播放，不能清掉同帧冲刺攻击的既有资格。
            if (!moving && stanceChangedThisTick) TryPlayStanceTransition(stanceTransitionSourceThisTick);
            // 残心和切架是可立即行动的短表现；静止时播完，新的移动/攻防输入照常接管。
            bool holdShortPresentation = (playingKiPulseAnimation && CurrentStance == kiPulseAnimationStance)
                || (playingStanceTransition && CurrentStance == stanceTransitionTarget);
            if (!moving && holdShortPresentation && stateTime < stateDuration)
                return;
            bool preserveGaitPhase = Mode == SamuraiCombatMode.Locomotion && !playingKiPulseAnimation && !playingStanceTransition;
            var selection = animation.SelectLocomotion(move, running, moving, StateName, preserveGaitPhase, CurrentStance);
            if (!moving)
            {
                selection.StateName = IdleStateName;
                selection.Clip = IdleClip;
                selection.Phase = 0f;
            }
            if (StateName != selection.StateName)
                Enter(SamuraiCombatMode.Locomotion, selection.StateName, SamuraiAnimationDriver.ClipDuration(selection.Clip), false, selection.Phase);
        }

        void TickDodge(SamuraiInputFrame input, Vector3 facing, float previousNormalized, float dt)
        {
            var dodge = ActiveAction;
            if (dodge == null) { dodgeFollowUp.Cancel(); EnterLocomotion(); movement.Move(Vector3.zero, dt); return; }
            float normalized = StateNormalizedTime;
            movement.MoveAction(dodge, actionDirection, previousNormalized, normalized, dt);
            if (input.lightPressed && !input.guardLightPressed && !input.guardHeavyPressed)
                dodgeFollowUp.BufferPress(Clock, normalized, profile.dodgeAttackInputStart, profile.inputBufferSeconds);
            float attackStart = Mathf.Max(profile.dodgeAttackStart, dodge.invulnerableEnd);
            if (dodgeFollowUp.ConsumeBuffered(Clock, normalized, attackStart)
                && CommitAttack(profile.dodgeAttack, -1, facing)) return;
            if (normalized >= 1f)
            {
                dodgeFollowUp.FinishDodge(Clock, profile.dodgeAttackGraceSeconds);
                EnterLocomotion();
            }
        }

        void TickAttack(SamuraiInputFrame input, Vector3 move, Vector3 facing, float previousNormalized, float dt, float kiPulseInputTime)
        {
            var action = ActiveAction;
            if (action == null) { EnterLocomotion(); movement.Move(Vector3.zero, dt); return; }
            float normalized = StateNormalizedTime;
            animation.SetActionPlayback(action, normalized);
            // 恢复旧四连的架势继续使用原闪避/冲刺回接；其他动作链不混入跨来源接点。
            bool legacyCombo = UsesLegacyCombo();
            bool fromDodgeAttack = legacyCombo && action == profile.dodgeAttack;
            int nextComboIndex = ComboIndex >= 0 ? ComboIndex + 1 :
                legacyCombo && (action == profile.sprintAttack || fromDodgeAttack) ? 0 : -1;
            var combo = CurrentCombo;
            var nextCombo = fromDodgeAttack ? profile.dodgeAttackToCombo1 : combo != null
                && nextComboIndex >= 0 && nextComboIndex < combo.Length ? combo[nextComboIndex] : null;
            bool comboWindow = normalized >= action.comboInputStart && normalized <= action.comboInputEnd;
            // 强击使用独立序号和一次短按缓存；长按不会偷发强击或在收势后偷偷进入居合。
            bool strongInput = ReadStrongHeavy(input, action, normalized, comboWindow, dt);
            if (input.heavyPressed && !input.guardHeavyPressed && !input.guardLightPressed && ComboIndex >= 0
                && !action.isMartialArt && comboWindow && !pendingComboHeavy && queuedFinisher == null
                && FindStrongRoute(action) == null)
            {
                var finisher = MartialArts.Get(StanceIndex, SamuraiMartialArtSlot.ComboFinisher);
                if (MartialArts.GetDefinition(StanceIndex, SamuraiMartialArtSlot.HoldHeavy) != null)
                {
                    // A fresh press must start inside this normal attack's link window.
                    // Delay the tap finisher until release so a hold never starts with a kick.
                    pendingComboHeavy = true;
                    comboHeavyHeldSeconds = 0f;
                    pendingComboTap = finisher;
                    pendingComboHold = MartialArts.Get(StanceIndex, SamuraiMartialArtSlot.HoldHeavy);
                    comboQueued = false;
                }
                else if (finisher != null && animation.CanPlay(finisher))
                { queuedFinisher = finisher; comboQueued = false; }
            }
            bool consumedComboHeavy = pendingComboHeavy;
            if (pendingComboHeavy)
            {
                if (!input.heavyPressed) comboHeavyHeldSeconds += dt;
                bool heldLongEnough = comboHeavyHeldSeconds + .00001f >= ComboHeavyHoldSeconds;
                if (heldLongEnough || input.heavyReleased || !input.heavyHeld)
                {
                    var selected = heldLongEnough ? pendingComboHold : pendingComboTap;
                    ClearComboHeavy();
                    // An empty long-press slot consumes the command, without falling back to a kick.
                    queuedFinisher = animation.CanPlay(selected) ? selected : null;
                }
            }
            if (input.lightPressed && !input.guardLightPressed && !input.guardHeavyPressed && queuedFinisher == null
                && !consumedComboHeavy && !strongInput && !strongQueued && !comboQueued && nextCombo != null && comboWindow)
                comboQueued = true;
            if (action.useRootMotion) movement.Move(Vector3.zero, dt);
            else approach.MoveAction(action, actionDirection, previousNormalized, normalized, dt);
            if (normalized >= action.CancelStart)
            {
                if (TryGuardCommand(input, facing)) return;
                if (TryBufferedDodge(move)) return;
                if (input.guardHeld) { movement.FaceDirection(input.facingWorld, dt); BeginGuard(defense.FreshPress); return; }
                if (TryPerformKiPulse(input, kiPulseInputTime)) return;
            }
            if (strongQueued && normalized >= strongLinkTime)
            {
                var strong = bufferedStrongAction;
                float blend = strongBlendSeconds, start = strongStartNormalized;
                bool continuation = strongContinuation;
                ClearStrongHeavy();
                // 只尝试支付一次。耐力不足时丢弃本次输入，恢复后不得补发。
                if (CommitAttack(strong, -1, facing, continuation: continuation,
                    transitionSeconds: blend, normalizedStart: start)) return;
            }
            if (comboQueued && normalized >= Mathf.Max(action.DamageEnd, action.comboLink))
            {
                comboQueued = false;
                if (CommitAttack(nextCombo, nextComboIndex, facing, continuation: true)) return;
            }
            if (queuedFinisher != null && normalized >= Mathf.Max(action.DamageEnd, action.comboLink))
            {
                var finisher = queuedFinisher;
                queuedFinisher = null;
                if (CommitAttack(finisher, -1, facing, continuation: true)) return;
            }
            // A late but valid press may need at most the remaining .25s to distinguish tap/hold.
            if (normalized >= 1f && !pendingComboHeavy && !pendingStrongHeavy) EnterLocomotion();
        }

        SamuraiActionDefinition StrongAttackAt(int index)
        {
            var attacks = CurrentStrongCombo;
            return attacks != null && index >= 0 && index < attacks.Length ? attacks[index] : null;
        }

        SamuraiNormalToStrongRoute FindStrongRoute(SamuraiActionDefinition action)
        {
            if (ComboIndex < 0 || action == null || action.isMartialArt || CurrentStance == null
                || CurrentStance.normalToStrongRoutes == null) return null;
            foreach (var route in CurrentStance.normalToStrongRoutes)
                if (route != null && route.sourceAttack == action) return route;
            return null;
        }

        bool ReadStrongHeavy(SamuraiInputFrame input, SamuraiActionDefinition action, float normalized, bool comboWindow, float dt)
        {
            bool consumed = pendingStrongHeavy || strongQueued;
            if (input.guardHeld || input.guardLightPressed || input.guardHeavyPressed
                || input.sheathePressed || input.sheatheHeld || input.sheatheReleased)
            { ClearStrongHeavy(); return consumed; }
            if (input.heavyPressed && comboWindow && !pendingStrongHeavy && !strongQueued)
            {
                var route = FindStrongRoute(action);
                var next = StrongComboIndex >= 0 ? StrongAttackAt(StrongComboIndex + 1)
                    : route != null ? StrongAttackAt(route.strongIndex) : null;
                if (animation.CanPlay(next))
                {
                    bufferedStrongAction = next;
                    pendingStrongHeavy = true;
                    strongHeavyHeldSeconds = 0f;
                    strongContinuation = route == null || !route.completeSource;
                    strongLinkTime = route != null ? route.completeSource ? 1f
                        : Mathf.Clamp01(Mathf.Max(action.DamageEnd, route.linkNormalizedTime))
                        : Mathf.Clamp01(Mathf.Max(action.DamageEnd, action.comboLink));
                    strongBlendSeconds = route != null ? Mathf.Max(0f, route.blendSeconds) : next.comboBlendSeconds;
                    strongRecoveryBlend = route != null ? Mathf.Max(0f, route.lateRecoveryBlendSeconds) : -1f;
                    strongBranchDeadline = route != null && !route.completeSource
                        ? strongLinkTime + StrongBranchLateGraceSeconds / action.Duration : float.PositiveInfinity;
                    // Offsets cannot skip movement or a damage window; all three clocks start together.
                    float firstDamage = next.GetDamageWindow(0).x;
                    strongStartNormalized = route != null ? Mathf.Clamp(route.startNormalizedTime, 0f,
                        Mathf.Clamp01(Mathf.Min(firstDamage, next.stepStart))) : 0f;
                    comboQueued = false;
                    consumed = true;
                }
            }
            if (pendingStrongHeavy)
            {
                if (!input.heavyPressed) strongHeavyHeldSeconds += dt;
                if (strongHeavyHeldSeconds + ChargeTimeTolerance >= ComboHeavyHoldSeconds) ClearStrongHeavy();
                else if (input.heavyReleased || !input.heavyHeld)
                {
                    pendingStrongHeavy = false; strongQueued = true;
                    if (normalized > strongBranchDeadline + ChargeTimeTolerance)
                    {
                        // The useful pose has already passed: finish the source instead of forcing a late cut.
                        bufferedStrongAction = StrongAttackAt(0);
                        strongLinkTime = 1f; strongStartNormalized = 0f;
                        strongBlendSeconds = strongRecoveryBlend;
                        strongContinuation = false;
                    }
                }
            }
            return consumed;
        }

        void ClearStrongHeavy()
        {
            bufferedStrongAction = null;
            pendingStrongHeavy = strongQueued = strongContinuation = false;
            strongHeavyHeldSeconds = strongLinkTime = strongStartNormalized = 0f;
            strongBlendSeconds = -1f;
            strongBranchDeadline = float.PositiveInfinity;
            strongRecoveryBlend = -1f;
        }

        void ClearComboHeavy()
        {
            pendingComboHeavy = false;
            comboHeavyHeldSeconds = 0f;
            pendingComboTap = pendingComboHold = null;
        }

        bool UsesLegacyCombo()
        {
            if (CurrentStance == null) return true;
            var current = CurrentCombo;
            var original = profile.combo;
            if (current == null || original == null || current.Length == 0 || current.Length != original.Length) return false;
            for (int i = 0; i < current.Length; i++)
                if (current[i] != original[i]) return false;
            return true;
        }

        public void NotifyAttackContact(int serial, SamuraiHitResult result)
        {
            if (IsAttacking && serial == AttackSerial) approach.NotifyContact(serial, result);
        }

        /// <summary>对外动作请求只接受可行动状态；连段和蓄力由Tick在合法窗口内提交。</summary>
        public bool TryStartAttack(SamuraiActionDefinition next, int nextComboIndex, Vector3 direction)
            => CanAct && CommitAttack(next, nextComboIndex, direction);

        bool CommitAttack(SamuraiActionDefinition next, int nextComboIndex, Vector3 direction, bool critical = false,
            bool continuation = false, bool fullyChargedRelease = false, float transitionSeconds = -1f,
            float normalizedStart = 0f, bool frostMoon = false)
        {
            if (!animation.CanPlay(next)) return false;
            float staminaCost = frostMoon ? 0f : Arts.StaminaCost(next);
            if (!CanAffordRequestedAction(staminaCost)) return false;
            bool enhanced = Arts.CommitAttack(next, out bool activated);
            FrostMoon.Clear();
            float beforePayment = vitals.Stamina;
            vitals.Spend(staminaCost, Clock); movement.TurnForAction(direction);
            Enter(SamuraiCombatMode.Attack, next.stateName, next.Duration,
                immediate: next.useRootMotion,
                normalizedStart: normalizedStart,
                transitionSeconds: transitionSeconds >= 0f ? transitionSeconds : continuation ? next.comboBlendSeconds : -1f,
                fullyChargedRelease: fullyChargedRelease);
            ActiveAction = next; ComboIndex = nextComboIndex; actionDirection = movement.Forward;
            animation.SetActionPlayback(next, normalizedStart);
            if (nextComboIndex < 0 && CurrentStrongCombo != null) StrongComboIndex = Array.IndexOf(CurrentStrongCombo, next);
            if (profile.kiPulseEnabled)
                KiPulse.BeginAttack(beforePayment - vitals.Stamina, profile.kiPulseRecoveryFraction, continuation,
                    Clock, next.Duration, next.KiPulseStart,
                    next.overrideKiPulseTiming ? next.kiPulseGatherSeconds : profile.kiPulseGatherSeconds,
                    next.overrideKiPulseTiming ? next.kiPulsePerfectSeconds : profile.kiPulsePerfectSeconds);
            // 将释放时的资格与倍率锁定到本次攻击；后续改配置不会改变已经出手的伤害。
            IsCriticalAttack = critical;
            IsFrostMoonAttack = frostMoon;
            IsMartialArtAttack = next.isMartialArt;
            IsArtsEnhancedAttack = enhanced; IsArtsActivationAttack = activated;
            AttackDamageMultiplier = (critical ? Mathf.Max(1f, profile.chargeCriticalDamageMultiplier) : 1f)
                * (enhanced ? Arts.DamageMultiplier(next) : 1f);
            AttackSerial++;
            approach.Begin(next, actionDirection, AttackSerial);
            AttackStarted?.Invoke();
            return true;
        }
        bool TryBufferedDodge(Vector3 direction)
        {
            if (Clock > dodgeBufferedUntil || profile.dodges == null || profile.dodges.Length != 8) return false;
            if (direction.sqrMagnitude < .001f) direction = -movement.Forward;
            direction.Normalize();
            Vector3 local = root.InverseTransformDirection(direction);
            int index = (Mathf.RoundToInt(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg / 45f) + 8) % 8;
            var dodge = profile.dodges[index];
            if (!animation.CanPlay(dodge) || !CanAffordRequestedAction(dodge.staminaCost)) return false;
            vitals.Spend(dodge.staminaCost, Clock);
            Enter(SamuraiCombatMode.Dodge, dodge.stateName, dodge.Duration);
            ActiveAction = dodge; actionDirection = direction; dodgeBufferedUntil = float.NegativeInfinity;
            dodgeFollowUp.BeginDodge();
            return true;
        }
        bool CanAffordRequestedAction(float cost)
        {
            if (vitals.CanAfford(cost)) return true;
            StaminaInsufficient?.Invoke();
            return false;
        }
        void PlayGuardContactVisual(string state, AnimationClip clip)
        {
            bool restart = guardContactVisualState == state;
            guardContactVisualState = state;
            float duration = SamuraiAnimationDriver.ClipDuration(clip);
            guardContactVisualUntil = Clock + duration;
            // 只重播原接刀表现；不Enter、不重置状态时间，也不改变行动资格。
            animation.Play(state, duration, restart);
        }
        void ClearGuardContactVisual()
        {
            guardContactVisualState = null;
            guardContactVisualUntil = float.NegativeInfinity;
        }
        void UpdateGuardContactVisual()
        {
            if (guardContactVisualState == null) return;
            if (Mode != SamuraiCombatMode.Guard || !defense.GuardHeld)
            { ClearGuardContactVisual(); return; }
            if (Clock < guardContactVisualUntil) return;
            ClearGuardContactVisual();
            bool useB = guardBackhand && profile.guardB != null && animation.HasState("GuardB");
            // 保持持防逻辑及其计时，只让接刀片段到期后回到已有持防动画。
            animation.Play(useB ? "GuardB" : "Guard",
                SamuraiAnimationDriver.ClipDuration(useB ? profile.guardB : profile.guard));
        }
        void BeginGuard(bool fresh)
        {
            bool useB = guardBackhand && profile.guardB != null && animation.HasState("GuardB");
            Enter(SamuraiCombatMode.Guard, useB ? "GuardB" : "Guard",
                SamuraiAnimationDriver.ClipDuration(useB ? profile.guardB : profile.guard));
            TryRaiseGuardWindow(fresh);
        }
        void TryRaiseGuardWindow(bool fresh)
        {
            defense.TryRaise(profile.deflectEnabled ? parryInputTime : Clock,
                fresh && (!profile.deflectEnabled || canRaiseDeflectAtInput));
        }
        void EnterLocomotion()
        {
            ResetSprint();
            defense.CloseWindow();
            Enter(SamuraiCombatMode.Locomotion, IdleStateName, SamuraiAnimationDriver.ClipDuration(IdleClip));
        }
        bool HasStanceIdle => CurrentStance != null && CurrentStance.idle != null
            && !string.IsNullOrWhiteSpace(CurrentStance.idleStateName) && animation.HasState(CurrentStance.idleStateName);
        string IdleStateName => HasStanceIdle ? CurrentStance.idleStateName : "Idle";
        AnimationClip IdleClip => HasStanceIdle ? CurrentStance.idle : profile.idle;
        bool ReadSelection(SamuraiInputFrame input)
        {
            var previous = CurrentStance;
            bool changed = false;
            if (input.highStancePressed) changed |= stanceSelection.SelectStance(0);
            else if (input.middleStancePressed) changed |= stanceSelection.SelectStance(1);
            if (changed)
            {
                if (IsAttacking) ComboIndex = StrongComboIndex = -1;
                comboQueued = false;
                queuedFinisher = null;
                ClearStrongHeavy();
                dodgeFollowUp.Cancel();
                stanceTransitionSourceThisTick = previous;
            }
            return changed;
        }
        bool TryPlayStanceTransition(SamuraiStanceDefinition fromStance)
        {
            var target = CurrentStance;
            if (fromStance == null || target == null || target == fromStance || target.entryTransitions == null)
                return false;
            foreach (var transition in target.entryTransitions)
            {
                if (transition == null || transition.fromStance != fromStance || transition.clip == null
                    || !animation.HasState(transition.stateName)) continue;
                // 同一帧切架与残心补全共用这次播放，不重新起播或再次发状态事件。
                if (playingStanceTransition && stanceTransitionTarget == target && StateName == transition.stateName
                    && stateTime < stateDuration) return true;
                ResetSprint();
                animation.ResetLocomotionDirection();
                defense.CloseWindow();
                float blend = float.IsNaN(transition.blendSeconds) || float.IsInfinity(transition.blendSeconds)
                    ? .08f : Mathf.Max(0f, transition.blendSeconds);
                Enter(SamuraiCombatMode.Locomotion, transition.stateName, SamuraiAnimationDriver.ClipDuration(transition.clip),
                    transitionSeconds: blend);
                playingStanceTransition = true;
                stanceTransitionTarget = target;
                return true;
            }
            return false;
        }
        void Enter(SamuraiCombatMode next, string state, float duration, bool immediate = false, float normalizedStart = 0f,
            float transitionSeconds = -1f, bool fullyChargedRelease = false)
        {
            ClearGuardContactVisual();
            if (next != SamuraiCombatMode.Attack) animation.SetActionPlayback(null, 0f);
            approach.Reset();
            playingKiPulseAnimation = false;
            kiPulseAnimationStance = null;
            playingStanceTransition = false;
            stanceTransitionTarget = null;
            queuedFinisher = chargeTapAction = chargeHeldAction = null;
            ClearComboHeavy();
            ClearStrongHeavy();
            chargingSheathed = false;
            configurableCharge = chargePendingHold = false;
            // 普通移动和动画自然收尾保留白条；防闪、蓄力及所有受击状态都明确放弃本轮。
            if (next != SamuraiCombatMode.Attack && next != SamuraiCombatMode.Locomotion) ClearKiPulse();
            if (next != SamuraiCombatMode.Locomotion) dodgeFollowUp.Cancel();
            Mode = next; ActiveAction = null; ComboIndex = StrongComboIndex = -1; comboQueued = false;
            IsCriticalAttack = false; AttackDamageMultiplier = 1f;
            IsFullyChargedAttack = next == SamuraiCombatMode.Attack && fullyChargedRelease;
            IsArtsEnhancedAttack = IsArtsActivationAttack = false;
            IsMartialArtAttack = false;
            IsFrostMoonAttack = false;
            stateTime = Mathf.Clamp01(normalizedStart) * Mathf.Max(.02f, duration);
            lastActionNormalized = Mathf.Clamp01(normalizedStart); stateDuration = Mathf.Max(.02f, duration); StateName = state;
            if (next != SamuraiCombatMode.Locomotion) { ResetSprint(); animation.ResetLocomotionDirection(); }
            else if (state == IdleStateName) animation.ResetLocomotionDirection();
            if (next != SamuraiCombatMode.Guard && next != SamuraiCombatMode.GuardHit) defense.CloseWindow();
            animation.Play(state, stateDuration, immediate, normalizedStart, transitionSeconds);
            StateChanged?.Invoke(state);
        }
        void ResetSprint() { IsSprinting = false; SprintElapsedSeconds = 0f; }
        public void ClearKiPulse() { KiPulse.Clear(); FrostMoon.Clear(); }

        internal static int RequestedStance(SamuraiInputFrame input) =>
            input.highStancePressed ? 0 : input.middleStancePressed ? 1 : -1;

        void TryPerformFlux()
        {
            if (!KiPulse.TryConsumeFlux(stanceInputTime, StanceIndex, out float extra, out bool pulseWasPerfect)) return;
            float restored = vitals.RestoreStamina(extra);
            if (profile.frostMoonEnabled) FrostMoon.AfterFlux(stanceInputTime, StanceIndex, profile.frostMoonWindowSeconds);
            PlayKiPulseAnimation();
            FluxPerformed?.Invoke(restored, pulseWasPerfect);
        }

        bool TryHandleFrostMoon(SamuraiInputFrame input, Vector3 facing)
        {
            if (!profile.frostMoonEnabled || Mode != SamuraiCombatMode.Locomotion || input.guardHeld
                || input.sheathePressed || input.sheatheHeld || input.sheatheReleased) return false;
            bool followUp = input.frostMoonFollowUpPressed;
            if (!followUp && (stanceChangedThisTick || !(input.kiPulseHeld || input.kiPulsePressed))) return false;
            int requestedStance = followUp ? input.frostMoonFollowUpStance : RequestedStance(input);
            float timestamp = followUp ? input.frostMoonFollowUpTimestamp : stanceInputTime;
            if (float.IsNaN(timestamp) || float.IsInfinity(timestamp)) return false;
            if (requestedStance != StanceIndex || !FrostMoon.TryConsume(Mathf.Min(Clock, timestamp), requestedStance)) return false;
            var action = CurrentStance != null ? CurrentStance.frostMoon : null;
            bool started = action != null && action.isMartialArt && animation.CanPlay(action)
                && CommitAttack(action, -1, facing, frostMoon: true);
            FrostMoonRequested?.Invoke(requestedStance, started);
            return true;
        }

        bool TryPerformKiPulse(SamuraiInputFrame input, float timestamp)
        {
            bool chord = profile.frostMoonEnabled && input.kiPulseHeld && RequestedStance(input) >= 0;
            if (!profile.kiPulseEnabled || !(input.kiPulsePressed || chord)
                || (Mode != SamuraiCombatMode.Attack && Mode != SamuraiCombatMode.Locomotion)
                || !KiPulse.TryConsume(timestamp, kiPulseStanceAtInput,
                    profile.kiPulseFluxEnabled ? profile.kiPulseFluxWindowSeconds : 0f,
                    vitals.MaxStamina * Mathf.Clamp01(profile.kiPulseFluxRecoveryFraction),
                    out float requested, out bool perfect)) return false;
            if (profile.frostMoonEnabled) FrostMoon.Open(timestamp, kiPulseStanceAtInput, profile.frostMoonWindowSeconds);
            // 窗口内跨架势先流转；此前已预切到该架势，则本次组合可直接衔接冬月。
            if (profile.kiPulseFluxEnabled || profile.frostMoonEnabled) stanceChangedThisTick |= ReadSelection(input);
            float restored = vitals.RestoreStamina(requested);
            comboQueued = false;
            dodgeBufferedUntil = float.NegativeInfinity;
            dodgeFollowUp.Cancel();
            PlayKiPulseAnimation();
            KiPulsePerformed?.Invoke(restored, perfect);
            if (stanceChangedThisTick) TryPerformFlux();
            TryHandleFrostMoon(input, movement.Forward);
            return true;
        }
        void PlayKiPulseAnimation()
        {
            // 成功Q与切架同帧、或Q后短窗口切架，都使用来源匹配的衔接动作；回耐和事件由调用者结算。
            if (stanceChangedThisTick && TryPlayStanceTransition(stanceTransitionSourceThisTick)) return;
            var stance = CurrentStance;
            bool hasStanceClip = stance != null && stance.kiPulse != null && animation.HasState(stance.kiPulseStateName);
            var clip = hasStanceClip ? stance.kiPulse : profile.kiPulseAnimation;
            string state = hasStanceClip ? stance.kiPulseStateName : profile.kiPulseStateName;
            if (clip == null || !animation.HasState(state)) { EnterLocomotion(); return; }
            ResetSprint();
            animation.ResetLocomotionDirection();
            defense.CloseWindow();
            Enter(SamuraiCombatMode.Locomotion, state, SamuraiAnimationDriver.ClipDuration(clip),
                transitionSeconds: Mathf.Min(profile.transitionSeconds, .035f));
            playingKiPulseAnimation = true;
            kiPulseAnimationStance = stance;
        }
        public void ReactToHit(float seconds)
        {
            if (IsToughnessBroken || IsDead) return;
            Enter(SamuraiCombatMode.Hit, "Hit", Mathf.Max(.05f, seconds));
            vitals.DelayRecovery(Clock);
        }
        public void ReactToHit(SamuraiDamageHit hit)
        {
            if (IsDead || IsToughnessBroken) return;
            if (!profile.directionalHitReactionsEnabled)
            {
                ReactToHit(SamuraiAnimationDriver.ClipDuration(profile.hit));
                return;
            }
            var selection = SamuraiHitReactionSelector.Select(profile, hit, root);
            bool available = selection.Clip != null && animation.HasState(selection.StateName);
            string state = available ? selection.StateName : "Hit";
            var clip = available ? selection.Clip : profile.hit;
            // 同一反应被再次命中时从头重播，避免自混合保留前一击的恢复姿势。
            bool restart = Mode == SamuraiCombatMode.Hit && StateName == state;
            Enter(SamuraiCombatMode.Hit, state, Mathf.Max(.05f, SamuraiAnimationDriver.ClipDuration(clip)), restart);
            vitals.DelayRecovery(Clock);
        }
        public void ReactToDeflected(float seconds)
        {
            if (IsDead || IsToughnessBroken) return;
            comboQueued = false;
            dodgeBufferedUntil = float.NegativeInfinity;
            canRaiseDeflectAtInput = false;
            defense.CancelInput();
            // 旧攻击标识与取消中的扫掠同时失效，新的反击只能在硬直结束后另起一次。
            AttackSerial++;
            string state = profile.deflected != null && animation.HasState("Deflected") ? "Deflected" : "Hit";
            Enter(SamuraiCombatMode.Deflected, state, Mathf.Max(.05f, seconds));
            vitals.DelayRecovery(Clock);
        }
        public void ReactToGuard(bool perfect) => ReactToGuard(perfect, default);
        public void ReactToGuard(bool perfect, SamuraiDamageHit hit)
        {
            if (!perfect)
            {
                var selection = SamuraiGuardReactionSelector.Select(profile, hit, root, guardBackhand);
                bool available = animation.HasState(selection.StateName);
                string guardState = available ? selection.StateName : "GuardHit";
                var clip = available ? selection.Clip : profile.guardHit;
                if (Toughness.Enabled && Toughness.LastHitResisted)
                {
                    // 已有反应状态保持原截止；普通持防抗住时仅补接触表现。
                    if (Mode == SamuraiCombatMode.Guard)
                    {
                        guardBackhand = available && selection.Backhand;
                        PlayGuardContactVisual(guardState, clip);
                    }
                    return;
                }
                guardBackhand = available && selection.Backhand;
                bool restart = profile.directionalHitReactionsEnabled && Mode == SamuraiCombatMode.GuardHit && StateName == guardState;
                Enter(SamuraiCombatMode.GuardHit, guardState,
                    SamuraiAnimationDriver.ClipDuration(clip), restart);
                return;
            }
            guardBackhand = false;
            string state = perfect && profile.parry != null && animation.HasState("Parry") ? "Parry" : "GuardHit";
            Enter(SamuraiCombatMode.GuardHit, state, perfect ? (profile.deflectEnabled
                ? Mathf.Max(.01f, profile.deflectSuccessRecoverySeconds) : Mathf.Max(.05f, profile.parrySuccessRecoverySeconds))
                : SamuraiAnimationDriver.ClipDuration(profile.guardHit));
        }
        public void ReactToGuardBreak()
        {
            StaminaInsufficient?.Invoke();
            Enter(SamuraiCombatMode.GuardBreak, "GuardBreak", SamuraiAnimationDriver.ClipDuration(profile.guardBreak));
        }
        public void ReactToDeath()
        {
            Arts.Reset();
            Enter(SamuraiCombatMode.Dead, "Death", SamuraiAnimationDriver.ClipDuration(profile.death));
        }

        public void ReactToToughnessBreak()
        {
            if (IsDead || IsToughnessBroken) return;
            defense.CancelInput(); AttackSerial++;
            Enter(SamuraiCombatMode.ToughnessBroken, "ToughnessBroken", Mathf.Max(.1f, profile.toughnessBreakSeconds), true);
            animation.Play("ToughnessBroken", stateDuration, true, profile.toughnessBrokenPose);
        }
        public void ResumeToughnessBreak(float remaining)
        {
            if (IsDead) return;
            if (remaining <= 0f) { RecoverFromToughnessBreak(); return; }
            Enter(SamuraiCombatMode.ToughnessBroken, "ToughnessBroken", remaining, true);
            animation.Play("ToughnessBroken", remaining, true, profile.toughnessBrokenPose);
        }
        public void RecoverFromToughnessBreak()
        {
            if (IsDead) return;
            Toughness.Reset(); defense.CancelInput();
            bool deflected = profile.toughnessBreakUsesDeflected && profile.deflected != null && animation.HasState("Deflected");
            Enter(SamuraiCombatMode.GuardBreak, deflected ? "Deflected" : "GuardBreak",
                SamuraiAnimationDriver.ClipDuration(deflected ? profile.deflected : profile.guardBreak),
                true, profile.toughnessBrokenPose);
        }
        public void BeginExecution(SamuraiActionDefinition action)
        {
            CancelInput(); AttackSerial++;
            Enter(SamuraiCombatMode.Executing, action.stateName, action.Duration, true);
        }
        public void BeginBeingExecuted(float duration)
        {
            CancelInput(); AttackSerial++;
            Enter(SamuraiCombatMode.BeingExecuted, "ToughnessBroken", duration, true);
            animation.Play("ToughnessBroken", duration, true, profile.toughnessBrokenPose);
        }
        public void FinishExecution() { if (IsExecuting) EnterLocomotion(); }
    }
}

