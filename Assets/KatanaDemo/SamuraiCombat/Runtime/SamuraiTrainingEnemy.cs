using System;
using OCDesign.Samurai.BehaviorTree;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>反应式优先行为树；仅产生输入，移动、朝向和战斗由正式组件执行。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10)]
    public sealed class SamuraiTrainingEnemy : MonoBehaviour
    {
        public SamuraiCombatant actor;
        public SamuraiCombatant target;
        public SamuraiPlayerInput playerInput;
        [Tooltip("T 键切换主动训练和静止木桩；木桩仍会受击与死亡。")]
        public bool activeTraining = true;
        [Header("交战间距（角色中心到中心，米）")]
        [Tooltip("仅在预警时接近此距离；平时保持观察间距。")]
        [Min(0.5f)] public float engageDistance = 1.35f;
        [Min(0.5f)] public float minimumSafeDistance = 1.5f;
        [Min(0.5f)] public float spacingMinimum = 1.9f;
        [Min(0.5f)] public float spacingMaximum = 2.3f;
        [Min(0.5f)] public float detectionDistance = 16f;
        [Header("决策节奏")]
        [Min(0.25f)] public float telegraphSeconds = 0.8f;
        [Min(0.2f)] public float attackInterval = 1.25f;
        [Min(0f)] public float guardSeconds = 1.25f;
        [Tooltip("保留旧配置；实际转向速度由战斗 Profile 控制。")]
        [Min(30f)] public float turnSpeed = 240f;
        [Range(0.1f, 0.5f)] public float cautiousStaminaRatio = 0.28f;
        [Range(0.35f, 0.9f)] public float recoveredStaminaRatio = 0.58f;
        [Min(0.2f)] public float obstacleLookAhead = 0.65f;
        [Tooltip("失焦或释放鼠标后不发起新攻击。")]
        public bool waitForPlayerCapture = true;

        // CharacterController 的接触偏移和低幅输入不保证恰好到达浮点目标；到达与停步使用同一容差。
        private const float ArrivalTolerance = 0.04f;
        private Node decisionTree;
        private SamuraiInputFrame frame;
        private readonly SamuraiEnemySensors sensors = new SamuraiEnemySensors();
        private float dt, targetDistance, cooldown, guardRemaining, telegraphElapsed;
        private float telegraphStartDistance, sideSwitchRemaining, orbitRemaining, orbitRest;
        private int attackSequence, threatSequence, orbitSide = 1;
        private bool canTrain, lineOfSight, telegraphing, heavyPending, heavyReleaseNextFrame;
        private bool wasAttacking, wasInReaction, safetyRetreat, postAttackRetreat, cautious;
        private bool defensiveRetreat, wasThreatening, approachLatched;
        private string previousThreatState, intent = "Keeping distance";
        private Vector3 towardTarget, sideDirection;

        public bool IsTelegraphing => telegraphing && activeTraining && actor != null && !actor.IsDead;
        public float TelegraphProgress => IsTelegraphing
            ? Mathf.Clamp01(telegraphElapsed / Mathf.Max(0.25f, telegraphSeconds)) : 0f;
        public string DecisionBranch { get; private set; } = "Inactive";
        public float TargetDistance => targetDistance;
        public string Intent => actor == null ? "Unconfigured" : actor.IsDead ? "Defeated - R to reset" :
            !activeTraining ? "Training dummy" : intent;
        private float NearSpacing => Mathf.Max(minimumSafeDistance + 0.2f, spacingMinimum);
        private float FarSpacing => Mathf.Max(NearSpacing + 0.2f, spacingMaximum);
        private float RestSpacing => (NearSpacing + FarSpacing) * 0.5f;
        private float StrikeDistance => Mathf.Max(0.6f, engageDistance);

        public void Configure(SamuraiCombatant combatant, SamuraiCombatant opponent, SamuraiPlayerInput input = null)
        {
            actor = combatant; target = opponent; playerInput = input;
            ResetDecision();
        }
        private void Start() => ResetDecision();
        private void OnDisable()
        {
            ResetDecision();
            if (actor != null && Application.isPlaying) actor.CancelInput();
        }
        public void ToggleActive() => SetActiveTraining(!activeTraining);
        public void SetActiveTraining(bool active)
        {
            activeTraining = active;
            ResetDecision();
            if (actor != null) actor.CancelInput();
        }
        public void ResetTraining()
        {
            if (actor != null) actor.ResetCombat();
            ResetDecision();
        }
        private void ResetDecision()
        {
            cooldown = Mathf.Max(0.2f, attackInterval);
            guardRemaining = telegraphElapsed = sideSwitchRemaining = orbitRemaining = 0f;
            orbitRest = 0.6f;
            attackSequence = threatSequence = 0; orbitSide = 1;
            telegraphing = heavyPending = heavyReleaseNextFrame = wasAttacking = wasInReaction = false;
            safetyRetreat = postAttackRetreat = cautious = defensiveRetreat = wasThreatening = approachLatched = false;
            previousThreatState = null; intent = "Keeping distance"; DecisionBranch = "Inactive";
            if (decisionTree == null) BuildTree();
        }
        private void Update()
        {
            if (actor != null) actor.Tick(BuildFrame(Time.deltaTime), Time.deltaTime);
        }

        /// <summary>测试入口：只生成输入，不调用 Tick。手动驱动时应禁用本组件的 Update。</summary>
        public SamuraiInputFrame BuildFrame(float deltaTime)
        {
            // 顿帧只暂停本角色的动作和决策计时，不禁用组件或重置行为树。
            if (actor != null && actor.IsHitStopped)
                return new SamuraiInputFrame { guardHeld = frame.guardHeld };
            frame = new SamuraiInputFrame();
            if (decisionTree == null) BuildTree();
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return frame;
            dt = Mathf.Min(deltaTime, 0.1f);
            PrepareContext();
            decisionTree.Tick();
            return frame;
        }
        private void BuildTree()
        {
            // 每帧从高优先级重评估；动作内部保存承诺，较高优先条件可中止预警。
            decisionTree = new Selector(
                Branch("Inactive", () => !canTrain, StopTraining),
                Branch("Committed action", () => heavyReleaseNextFrame || actor.IsAttacking || actor.IsCharging || actor.IsDodging, KeepCommitment),
                Branch("Hit reaction", () => !actor.CanAct, RecoverFromHit),
                Branch("Restore spacing", () => safetyRetreat || postAttackRetreat, RestoreSpacing),
                Branch("Defensive response", () => cautious || guardRemaining > 0f, RespondToThreat),
                Branch("Telegraphed attack", () => telegraphing || ReadyToThreaten(), TelegraphAttack),
                Branch("Approach", () => approachLatched || !lineOfSight, ApproachTarget),
                new ActionNode(() => { DecisionBranch = "Spacing / idle"; return HoldSpacing(); }));
        }
        private Node Branch(string name, Func<bool> condition, Func<NodeStatus> action)
        {
            return new Sequence(new ConditionNode(condition), new ActionNode(() =>
            {
                DecisionBranch = name;
                return action();
            }));
        }
        private void PrepareContext()
        {
            canTrain = activeTraining && actor != null && actor.Profile != null && target != null &&
                !actor.IsDead && !target.IsDead && actor.Team != target.Team &&
                (!waitForPlayerCapture || playerInput == null || playerInput.HasCursorCapture);
            if (!canTrain) return;
            Vector3 delta = target.transform.position - actor.transform.position;
            delta.y = 0f; targetDistance = delta.magnitude;
            towardTarget = targetDistance > 0.001f ? delta / targetDistance : actor.transform.forward;
            sideDirection = Vector3.Cross(Vector3.up, towardTarget);
            frame.facingWorld = towardTarget;
            lineOfSight = sensors.HasLineOfSight(actor, target, actor.Profile.obstructionMask);
            if (targetDistance > Mathf.Max(FarSpacing, detectionDistance)) { canTrain = false; return; }
            cooldown = Mathf.Max(0f, cooldown - dt);
            guardRemaining = Mathf.Max(0f, guardRemaining - dt);
            sideSwitchRemaining = Mathf.Max(0f, sideSwitchRemaining - dt);
            orbitRest = Mathf.Max(0f, orbitRest - dt);
            float ratio = actor.Stamina / actor.MaxStamina;
            if (ratio < cautiousStaminaRatio) cautious = true;
            else if (ratio >= Mathf.Max(cautiousStaminaRatio + 0.15f, recoveredStaminaRatio)) cautious = false;
            if (wasAttacking && !actor.IsAttacking && !actor.IsCharging)
            {
                postAttackRetreat = true;
                CancelTelegraph(attackInterval);
            }
            wasAttacking = actor.IsAttacking || actor.IsCharging;
            if (actor.IsInHitReaction && !wasInReaction)
            {
                CancelTelegraph(attackInterval);
                if (actor.StateName != "GuardHit") { guardRemaining = 0f; postAttackRetreat = true; }
            }
            wasInReaction = actor.IsInHitReaction;
            // 预警接近属于有意踏入攻击距离；其他时候不同的进入/退出阈值避免走停抖动。
            float unsafeDistance = telegraphing ? Mathf.Max(0.8f, StrikeDistance - 0.18f) :
                Mathf.Max(minimumSafeDistance, NearSpacing - 0.12f);
            if (targetDistance < unsafeDistance) safetyRetreat = true;
            else if (targetDistance >= NearSpacing - ArrivalTolerance) safetyRetreat = false;
            if (targetDistance >= RestSpacing - ArrivalTolerance) postAttackRetreat = false;
            if (targetDistance > FarSpacing + 0.12f) approachLatched = true;
            else if (targetDistance <= RestSpacing + ArrivalTolerance) approachLatched = false;
            bool threatening = lineOfSight && targetDistance < FarSpacing + 0.6f && (target.IsAttacking || target.IsCharging);
            if (threatening && (!wasThreatening || previousThreatState != target.StateName))
            {
                threatSequence++;
                defensiveRetreat = cautious || target.IsCharging || threatSequence % 3 == 0;
                guardRemaining = Mathf.Max(0.5f, guardSeconds);
                CancelTelegraph(Mathf.Max(0.6f, attackInterval));
            }
            wasThreatening = threatening;
            previousThreatState = threatening ? target.StateName : null;
        }
        private NodeStatus StopTraining()
        {
            if (actor != null && (telegraphing || guardRemaining > 0f || heavyReleaseNextFrame || actor.IsCharging || actor.IsGuarding)) actor.CancelInput();
            telegraphing = heavyReleaseNextFrame = wasAttacking = wasThreatening = false;
            safetyRetreat = postAttackRetreat = approachLatched = false;
            guardRemaining = 0f; cooldown = Mathf.Max(0.2f, attackInterval);
            frame = new SamuraiInputFrame();
            intent = !activeTraining ? "Training dummy" : target == null || target.IsDead ? "Waiting for opponent" :
                waitForPlayerCapture && playerInput != null && !playerInput.HasCursorCapture ? "Waiting for mouse capture" : "Opponent out of range";
            return NodeStatus.Success;
        }
        private NodeStatus KeepCommitment()
        {
            intent = actor.IsDodging ? "Evading" : "Committed strike";
            if (heavyReleaseNextFrame)
            {
                heavyReleaseNextFrame = false;
                if (!lineOfSight || targetDistance > StrikeDistance + 0.5f)
                {
                    actor.CancelInput(); postAttackRetreat = true; CancelTelegraph(attackInterval);
                }
                else if (actor.IsCharging) frame.heavyReleased = true;
            }
            return NodeStatus.Running;
        }
        private NodeStatus RecoverFromHit()
        {
            intent = actor.StateName == "GuardHit" ? "Absorbing blocked hit" : "Recovering from hit";
            frame.guardHeld = actor.StateName == "GuardHit" && !defensiveRetreat && guardRemaining > 0f && !cautious;
            return NodeStatus.Running;
        }
        private NodeStatus RestoreSpacing()
        {
            CancelTelegraph(Mathf.Max(0.45f, cooldown));
            intent = postAttackRetreat ? "Recovering distance after strike" : "Too close - stepping back";
            float destination = postAttackRetreat ? RestSpacing : NearSpacing;
            float amount = MovementAmount(targetDistance < minimumSafeDistance ? 0.85f : 0.55f, destination - targetDistance);
            if (!MoveSafely(-towardTarget, -1f, amount))
            {
                frame.guardHeld = !cautious && lineOfSight && (target.IsAttacking || target.IsCharging);
                intent = "Retreat blocked - holding safe position";
            }
            return NodeStatus.Running;
        }
        private NodeStatus RespondToThreat()
        {
            CancelTelegraph(Mathf.Max(0.55f, cooldown));
            if (cautious || defensiveRetreat)
            {
                intent = cautious ? "Low stamina - recovering at distance" : "Reading attack - withdrawing";
                if (targetDistance < FarSpacing + 0.1f && !MoveSafely(-towardTarget, -1f,
                    MovementAmount(cautious ? 0.6f : 0.8f, FarSpacing + 0.1f - targetDistance)))
                {
                    frame.guardHeld = !cautious && target.IsAttacking && lineOfSight;
                    intent = "Withdrawal blocked - watching opponent";
                }
            }
            else
            {
                frame.guardHeld = lineOfSight && targetDistance <= FarSpacing + 0.5f;
                intent = frame.guardHeld ? "Guarding incoming attack" : "Watching opponent recover";
            }
            return NodeStatus.Running;
        }
        private bool ReadyToThreaten()
        {
            return cooldown <= 0f && lineOfSight && targetDistance <= FarSpacing &&
                targetDistance >= minimumSafeDistance && CanAffordNextStrike();
        }
        private bool CanAffordNextStrike()
        {
            var p = actor.Profile;
            var next = attackSequence % 3 == 2 ? p.heavy : p.combo != null && p.combo.Length > 0 ? p.combo[0] : null;
            return next != null && actor.Stamina >= Mathf.Max(0f, next.staminaCost) + actor.MaxStamina * 0.12f;
        }
        private NodeStatus TelegraphAttack()
        {
            if (!telegraphing)
            {
                telegraphing = true; heavyPending = attackSequence % 3 == 2;
                telegraphElapsed = 0f; telegraphStartDistance = targetDistance; orbitRemaining = 0f;
            }
            if (!lineOfSight || targetDistance > Mathf.Max(FarSpacing + 0.4f, telegraphStartDistance + 0.65f) || !CanAffordNextStrike())
            {
                CancelTelegraph(attackInterval);
                intent = "Attack cancelled - target escaped or obstructed";
                return NodeStatus.Success;
            }
            telegraphElapsed += dt;
            intent = heavyPending ? "HEAVY ATTACK INCOMING" : "ATTACK INCOMING";
            float rangeError = targetDistance - StrikeDistance;
            if (rangeError > 0.06f && !MoveSafely(towardTarget, 1f, MovementAmount(0.75f, rangeError)))
            {
                CancelTelegraph(attackInterval); intent = "Attack cancelled - approach blocked";
                return NodeStatus.Success;
            }
            bool facing = Vector3.Angle(actor.transform.forward, towardTarget) < 25f;
            if (telegraphElapsed >= Mathf.Max(0.25f, telegraphSeconds) && rangeError <= 0.12f && facing)
            {
                frame.moveWorld = Vector3.zero; telegraphing = false; attackSequence++;
                if (heavyPending)
                {
                    frame.heavyPressed = frame.heavyHeld = true; heavyReleaseNextFrame = true;
                }
                else frame.lightPressed = true;
                cooldown = Mathf.Max(0.2f, attackInterval);
            }
            else if (telegraphElapsed > Mathf.Max(0.25f, telegraphSeconds) + 0.65f)
            {
                frame.moveWorld = Vector3.zero; CancelTelegraph(attackInterval);
                intent = "Attack cancelled - no clear opening";
            }
            return NodeStatus.Running;
        }
        private NodeStatus ApproachTarget()
        {
            intent = lineOfSight ? "Approaching observation distance" : "Obstacle ahead - seeking a clear side";
            if (!lineOfSight) return Orbit(false);
            float rangeError = targetDistance - RestSpacing;
            if (rangeError > ArrivalTolerance)
            {
                frame.sprint = targetDistance > FarSpacing + 2f && !cautious;
                if (!MoveSafely(towardTarget, 1f, MovementAmount(0.8f, rangeError, frame.sprint)))
                {
                    frame.sprint = false; intent = "Approach blocked - waiting for an opening";
                }
            }
            return NodeStatus.Running;
        }
        private NodeStatus HoldSpacing()
        {
            intent = "Keeping distance - watching opponent";
            return Orbit(true);
        }
        private NodeStatus Orbit(bool keepRing)
        {
            if (orbitRemaining <= 0f)
            {
                if (orbitRest > 0f) return NodeStatus.Success;
                orbitRemaining = 0.5f;
            }
            float radial = keepRing ? Mathf.Clamp((targetDistance - RestSpacing) / 0.25f, -0.6f, 0.6f) : 0f;
            Vector3 direction = (sideDirection * orbitSide + towardTarget * radial).normalized;
            if (!MoveSafely(direction, radial, 0.3f, true)) orbitRemaining = 0f;
            else orbitRemaining -= dt;
            if (orbitRemaining <= 0f)
            {
                orbitRest = 0.85f;
                if (sideSwitchRemaining <= 0f) { orbitSide = -orbitSide; sideSwitchRemaining = 0.65f; }
            }
            return NodeStatus.Running;
        }
        private float MovementAmount(float maximum, float remainingDistance, bool running = false)
        {
            float speed = running ? actor.Profile.runSpeed : actor.Profile.walkSpeed;
            return Mathf.Clamp(remainingDistance / Mathf.Max(0.01f, speed * dt), 0f, maximum);
        }
        private bool MoveSafely(Vector3 preferred, float radial, float amount, bool orbit = false)
        {
            if (amount <= 0.001f) return true;
            if (ClearPath(preferred, amount)) { frame.moveWorld = preferred.normalized * amount; return true; }
            Vector3 side = sideDirection * orbitSide;
            Vector3 alternate = (towardTarget * radial * 0.65f + side).normalized;
            if (orbit) alternate = (-side + towardTarget * radial).normalized;
            if (ClearPath(alternate, amount))
            {
                frame.moveWorld = alternate * amount;
                if (orbit && sideSwitchRemaining <= 0f) { orbitSide = -orbitSide; sideSwitchRemaining = 0.65f; }
                return true;
            }
            alternate = (towardTarget * radial * 0.65f - side).normalized;
            if (!orbit && ClearPath(alternate, amount))
            {
                frame.moveWorld = alternate * amount;
                if (sideSwitchRemaining <= 0f) { orbitSide = -orbitSide; sideSwitchRemaining = 0.65f; }
                return true;
            }
            if (!orbit && radial < 0f)
            {
                if (ClearPath(side, amount)) { frame.moveWorld = side * amount; return true; }
                if (ClearPath(-side, amount)) { frame.moveWorld = -side * amount; return true; }
            }
            return false;
        }
        private bool ClearPath(Vector3 direction, float amount) =>
            sensors.HasClearPath(actor, direction, amount, obstacleLookAhead, actor.Profile.obstructionMask);
        private void CancelTelegraph(float delay)
        {
            telegraphing = false; telegraphElapsed = 0f;
            cooldown = Mathf.Max(cooldown, Mathf.Max(0.2f, delay));
        }

    }
}
