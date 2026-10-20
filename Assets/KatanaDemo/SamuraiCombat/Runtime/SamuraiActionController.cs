using System;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 行为状态与动作时序。只协调移动/动画/资源服务，不做物理命中查询或读取键鼠。
    /// 输入、AI和未来技能入口都可调用Tick或TryStartAttack。
    /// </summary>
    public sealed class SamuraiActionController : ISamuraiCombatReactions, ISamuraiActionView
    {
        readonly SamuraiCombatProfile profile;
        readonly SamuraiCharacterMotor movement;
        readonly SamuraiAnimationDriver animation;
        readonly SamuraiVitals vitals;
        readonly SamuraiDefense defense;
        readonly Transform root;
        const float ChargeTimeTolerance = .00001f;
        float stateTime, stateDuration = 1f, lastActionNormalized;
        float dodgeBufferedUntil = float.NegativeInfinity;
        bool comboQueued, sprintExhausted;
        Vector3 actionDirection;
        public SamuraiCombatMode Mode { get; private set; }
        public SamuraiActionDefinition ActiveAction { get; private set; }
        public int AttackSerial { get; private set; }
        public int ComboIndex { get; private set; } = -1;
        public string StateName { get; private set; } = "Idle";
        public float Clock { get; private set; }
        public float StateNormalizedTime => Mode == SamuraiCombatMode.Attack || Mode == SamuraiCombatMode.Dodge
            ? animation.TryProgress(StateName, out float progress) ? Mathf.Clamp01(Mathf.Max(lastActionNormalized, progress)) : lastActionNormalized
            : Mathf.Clamp01(stateTime / Mathf.Max(.02f, stateDuration));
        public float ChargeNormalizedTime => IsCharging ? Mathf.Clamp01(stateTime / Mathf.Max(.1f, profile.chargeSeconds)) : 0f;
        public float ChargeElapsedSeconds => IsCharging ? stateTime : 0f;
        bool HasFullCharge => IsCharging && stateTime + ChargeTimeTolerance >= Mathf.Max(.1f, profile.chargeSeconds);
        public bool IsChargeCriticalWindowOpen => HasFullCharge && profile.chargeCriticalWindowSeconds > 0f
            && stateTime + ChargeTimeTolerance < Mathf.Max(.1f, profile.chargeSeconds) + profile.chargeCriticalWindowSeconds;
        public bool IsCriticalAttack { get; private set; }
        public float AttackDamageMultiplier { get; private set; } = 1f;
        public bool IsDead => Mode == SamuraiCombatMode.Dead;
        public bool IsAttacking => Mode == SamuraiCombatMode.Attack;
        public bool IsGuarding => defense.GuardHeld && (Mode == SamuraiCombatMode.Guard || Mode == SamuraiCombatMode.GuardHit);
        public bool IsCharging => Mode == SamuraiCombatMode.Charge;
        public bool IsDodging => Mode == SamuraiCombatMode.Dodge;
        public bool IsParrying => Mode == SamuraiCombatMode.GuardHit && StateName == "Parry";
        public bool CanAct => Mode == SamuraiCombatMode.Locomotion || Mode == SamuraiCombatMode.Guard;
        public bool IsInHitReaction => Mode == SamuraiCombatMode.Hit || Mode == SamuraiCombatMode.GuardHit || Mode == SamuraiCombatMode.GuardBreak;
        public bool IsInvulnerable => IsDodging && ActiveAction != null && StateNormalizedTime >= ActiveAction.invulnerableStart
            && StateNormalizedTime < ActiveAction.invulnerableEnd;
        public bool IsParryWindowOpen => defense.WindowOpen(Clock, IsGuarding);
        public float ParryCooldownRemaining => defense.CooldownRemaining(Clock);
        public event Action<string> StateChanged;
        public event Action AttackStarted;

        public SamuraiActionController(Transform root, SamuraiCombatProfile profile, SamuraiCharacterMotor movement,
            SamuraiAnimationDriver animation, SamuraiVitals vitals, SamuraiDefense defense)
        {
            this.root = root; this.profile = profile; this.movement = movement;
            this.animation = animation; this.vitals = vitals; this.defense = defense;
        }

        public void Reset()
        {
            Clock = 0f; dodgeBufferedUntil = float.NegativeInfinity;
            comboQueued = sprintExhausted = false;
            defense.Reset(); animation.Reset();
            // Serial不回退，避免外部持有旧招式标识后误认为仍有效。
            AttackSerial++;
            Enter(SamuraiCombatMode.Locomotion, "Idle", SamuraiAnimationDriver.ClipDuration(profile.idle), true);
        }
        public void CancelInput()
        {
            comboQueued = false; dodgeBufferedUntil = float.NegativeInfinity;
            defense.CancelInput();
            if (Mode == SamuraiCombatMode.Charge || Mode == SamuraiCombatMode.Guard) EnterLocomotion();
        }
        public void Tick(SamuraiInputFrame input, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            dt = Mathf.Min(dt, .1f);
            Clock += dt;
            float previousNormalized = lastActionNormalized;
            stateTime += dt; lastActionNormalized = StateNormalizedTime;
            defense.ReadInput(input.guardHeld, input.parryPressed);
            Vector3 move = SamuraiCharacterMotor.MoveDirection(input, root);
            Vector3 facing = SamuraiCharacterMotor.FacingDirection(input.facingWorld, move);
            if (!input.sprint) sprintExhausted = false;
            if (input.dodgePressed) dodgeBufferedUntil = Clock + Mathf.Max(0f, profile.inputBufferSeconds);
            switch (Mode)
            {
                case SamuraiCombatMode.Dead: movement.Move(Vector3.zero, dt); return;
                case SamuraiCombatMode.Attack: TickAttack(input, move, facing, previousNormalized, dt); break;
                case SamuraiCombatMode.Dodge:
                    movement.MoveAction(ActiveAction, actionDirection, previousNormalized, StateNormalizedTime, dt);
                    if (StateNormalizedTime >= 1f) EnterLocomotion();
                    break;
                case SamuraiCombatMode.Charge:
                    movement.Move(Vector3.zero, dt);
                    if (TryBufferedDodge(move)) break;
                    if (input.guardHeld) { movement.FaceDirection(input.facingWorld, dt); BeginGuard(defense.FreshPress); }
                    else if (input.heavyReleased)
                    {
                        bool useCharged = HasFullCharge && animation.CanPlay(profile.charged)
                            && vitals.CanAfford(profile.charged.staminaCost);
                        var release = useCharged ? profile.charged : profile.heavy;
                        bool critical = useCharged && IsChargeCriticalWindowOpen;
                        if (!CommitAttack(release, -1, facing, critical)) EnterLocomotion();
                    }
                    break;
                case SamuraiCombatMode.Hit:
                case SamuraiCombatMode.GuardBreak:
                case SamuraiCombatMode.GuardHit:
                    movement.Move(Vector3.zero, dt);
                    if (stateTime >= stateDuration)
                    {
                        if (input.guardHeld) { movement.FaceDirection(input.facingWorld, dt); BeginGuard(false); }
                        else EnterLocomotion();
                    }
                    break;
                default: TickLocomotion(input, move, facing, dt); break;
            }
            vitals.Recover(Clock, dt, Mode != SamuraiCombatMode.Attack && Mode != SamuraiCombatMode.Dodge
                && Mode != SamuraiCombatMode.Charge && Mode != SamuraiCombatMode.GuardBreak && Mode != SamuraiCombatMode.Hit, IsGuarding);
        }

        void TickLocomotion(SamuraiInputFrame input, Vector3 move, Vector3 facing, float dt)
        {
            if (TryBufferedDodge(move)) { movement.Move(Vector3.zero, dt); return; }
            if (input.lightPressed && profile.combo != null && profile.combo.Length > 0
                && CommitAttack(profile.combo[0], 0, facing)) { movement.Move(Vector3.zero, dt); return; }
            if (input.heavyPressed && animation.CanPlay(profile.heavy) && vitals.CanAfford(profile.heavy.staminaCost))
            {
                movement.TurnForAction(facing);
                Enter(SamuraiCombatMode.Charge, "Charge", Mathf.Max(.1f, profile.chargeSeconds));
                movement.Move(Vector3.zero, dt);
                if (input.heavyReleased && !CommitAttack(profile.heavy, -1, facing)) EnterLocomotion();
                return;
            }
            if (input.guardHeld)
            {
                movement.FaceDirection(input.facingWorld, dt);
                if (Mode != SamuraiCombatMode.Guard) BeginGuard(defense.FreshPress);
                else defense.TryRaise(Clock, defense.FreshPress);
                movement.Move(move * profile.guardWalkSpeed, dt);
                return;
            }
            if (Mode == SamuraiCombatMode.Guard) EnterLocomotion();
            bool moving = move.sqrMagnitude > .001f;
            float cost = Mathf.Max(0f, profile.sprintStaminaPerSecond) * dt;
            if (!input.sprint || vitals.Stamina >= vitals.MaxStamina * .15f) sprintExhausted = false;
            if (moving && input.sprint && (!vitals.CanAfford(cost) || vitals.Stamina <= .1f)) sprintExhausted = true;
            bool running = moving && input.sprint && !sprintExhausted && vitals.CanAfford(cost) && vitals.Stamina > .1f;
            if (running) vitals.Spend(cost, Clock);
            movement.FaceDirection(facing, dt);
            movement.Move(move * (running ? profile.runSpeed : profile.walkSpeed), dt);
            var selection = animation.SelectLocomotion(move, running, moving, StateName, Mode == SamuraiCombatMode.Locomotion);
            if (StateName != selection.StateName)
                Enter(SamuraiCombatMode.Locomotion, selection.StateName, SamuraiAnimationDriver.ClipDuration(selection.Clip), false, selection.Phase);
        }

        void TickAttack(SamuraiInputFrame input, Vector3 move, Vector3 facing, float previousNormalized, float dt)
        {
            var action = ActiveAction;
            if (action == null) { EnterLocomotion(); movement.Move(Vector3.zero, dt); return; }
            float normalized = StateNormalizedTime;
            if (input.lightPressed && !comboQueued && ComboIndex >= 0 && profile.combo != null
                && ComboIndex + 1 < profile.combo.Length && normalized >= action.comboInputStart && normalized <= action.comboInputEnd)
                comboQueued = true;
            movement.MoveAction(action, actionDirection, previousNormalized, normalized, dt);
            if (normalized >= action.CancelStart)
            {
                if (TryBufferedDodge(move)) return;
                if (input.guardHeld) { movement.FaceDirection(input.facingWorld, dt); BeginGuard(defense.FreshPress); return; }
            }
            if (comboQueued && normalized >= Mathf.Max(action.damageEnd, action.comboLink))
            {
                comboQueued = false;
                if (CommitAttack(profile.combo[ComboIndex + 1], ComboIndex + 1, facing)) return;
            }
            if (normalized >= 1f) EnterLocomotion();
        }

        /// <summary>对外动作请求只接受可行动状态；连段和蓄力由Tick在合法窗口内提交。</summary>
        public bool TryStartAttack(SamuraiActionDefinition next, int nextComboIndex, Vector3 direction)
            => CanAct && CommitAttack(next, nextComboIndex, direction);

        bool CommitAttack(SamuraiActionDefinition next, int nextComboIndex, Vector3 direction, bool critical = false)
        {
            if (!animation.CanPlay(next) || !vitals.CanAfford(next.staminaCost)) return false;
            vitals.Spend(next.staminaCost, Clock); movement.TurnForAction(direction);
            Enter(SamuraiCombatMode.Attack, next.stateName, next.Duration);
            ActiveAction = next; ComboIndex = nextComboIndex; actionDirection = movement.Forward;
            // 将释放时的资格与倍率锁定到本次攻击；后续改配置不会改变已经出手的伤害。
            IsCriticalAttack = critical;
            AttackDamageMultiplier = critical ? Mathf.Max(1f, profile.chargeCriticalDamageMultiplier) : 1f;
            AttackSerial++; AttackStarted?.Invoke();
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
            if (!animation.CanPlay(dodge) || !vitals.CanAfford(dodge.staminaCost)) return false;
            vitals.Spend(dodge.staminaCost, Clock);
            Enter(SamuraiCombatMode.Dodge, dodge.stateName, dodge.Duration);
            ActiveAction = dodge; actionDirection = direction; dodgeBufferedUntil = float.NegativeInfinity;
            return true;
        }
        void BeginGuard(bool fresh)
        {
            Enter(SamuraiCombatMode.Guard, "Guard", SamuraiAnimationDriver.ClipDuration(profile.guard));
            defense.TryRaise(Clock, fresh);
        }
        void EnterLocomotion()
        {
            defense.CloseWindow();
            Enter(SamuraiCombatMode.Locomotion, "Idle", SamuraiAnimationDriver.ClipDuration(profile.idle));
        }
        void Enter(SamuraiCombatMode next, string state, float duration, bool immediate = false, float normalizedStart = 0f)
        {
            Mode = next; ActiveAction = null; ComboIndex = -1; comboQueued = false;
            IsCriticalAttack = false; AttackDamageMultiplier = 1f;
            stateTime = Mathf.Clamp01(normalizedStart) * Mathf.Max(.02f, duration);
            lastActionNormalized = 0f; stateDuration = Mathf.Max(.02f, duration); StateName = state;
            if (next != SamuraiCombatMode.Locomotion) animation.ResetLocomotionDirection();
            if (next != SamuraiCombatMode.Guard && next != SamuraiCombatMode.GuardHit) defense.CloseWindow();
            animation.Play(state, stateDuration, immediate, normalizedStart);
            StateChanged?.Invoke(state);
        }
        public void ReactToHit(float seconds)
        {
            Enter(SamuraiCombatMode.Hit, "Hit", Mathf.Max(.05f, seconds));
            vitals.DelayRecovery(Clock);
        }
        public void ReactToGuard(bool perfect)
        {
            string state = perfect && profile.parry != null && animation.HasState("Parry") ? "Parry" : "GuardHit";
            Enter(SamuraiCombatMode.GuardHit, state, perfect ? Mathf.Max(.05f, profile.parrySuccessRecoverySeconds)
                : SamuraiAnimationDriver.ClipDuration(profile.guardHit));
        }
        public void ReactToGuardBreak() => Enter(SamuraiCombatMode.GuardBreak, "GuardBreak", SamuraiAnimationDriver.ClipDuration(profile.guardBreak));
        public void ReactToDeath() => Enter(SamuraiCombatMode.Dead, "Death", SamuraiAnimationDriver.ClipDuration(profile.death));
    }
}

