using System;
using OCDesign.Samurai.BehaviorTree;
using UnityEngine;

namespace OCDesign.Samurai
{
    public enum SamuraiTrainingMode { Normal, TripleCombo, MixedDeflect }

    /// <summary>反应式优先行为树；产生输入并请求合法准备期对准，移动和战斗由正式组件执行。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10)]
    public sealed class SamuraiTrainingEnemy : MonoBehaviour
    {
        public SamuraiCombatant actor;
        public SamuraiCombatant target;
        public SamuraiPlayerInput playerInput;
        [Tooltip("T 键切换主动训练和静止木桩；木桩仍会受击与死亡。")]
        public bool activeTraining = true;
        [Tooltip("G 键切换常规对练、固定三连和单刀/二连/延迟重击；T 独立控制启停。")]
        public SamuraiTrainingMode trainingMode = SamuraiTrainingMode.Normal;
        [Tooltip("混合练习中的重击在原地保持蓄势后释放；仍使用正式蓄力和重击逻辑。")]
        [Min(0f)] public float delayedHeavySeconds = 0.55f;
        [Header("交战间距（角色中心到中心，米）")]
        [Tooltip("常规招式没有可用攻击区域时的出手距离；有区域时按首击覆盖自动计算。")]
        [Min(0.5f)] public float engageDistance = 1.35f;
        [Tooltip("弹反练习使用较近的起手距离，使三连第一刀也能覆盖站定的目标；常规对练仍用原距离。")]
        [Min(0.5f)] public float practiceEngageDistance = 1.05f;
        [Min(0.5f)] public float minimumSafeDistance = 1.5f;
        [Min(0.5f)] public float spacingMinimum = 1.9f;
        [Min(0.5f)] public float spacingMaximum = 2.3f;
        [Min(0.5f)] public float detectionDistance = 16f;
        [Tooltip("仅初次发现目标使用；180为左右各90度。发现后在检测距离内持续追踪，不再限制朝向。")]
        [Range(0f, 360f)] public float horizontalViewAngle = 180f;
        [Tooltip("垂直视野总角度，兼容高个敌人观察近处玩家。")]
        [Range(0f, 180f)] public float verticalViewAngle = 160f;
        [HideInInspector] public int perceptionConfigurationVersion;
        [Header("决策节奏")]
        [Min(0.25f)] public float telegraphSeconds = 0.8f;
        [Min(0.2f)] public float attackInterval = 1.25f;
        [Min(0f)] public float guardSeconds = 1.25f;
        [Tooltip("仅常规AI：看到新攻击后持续观察多久再决定防守；不改变格挡或弹反窗口。")]
        [Min(0f)] public float defenseReactionSeconds = .25f;
        [Tooltip("仅常规AI：一轮持防结束后，多久内不重新举防；连段不能刷新已开始的持防。")]
        [Min(0f)] public float defenseRearmSeconds = .75f;
        [Tooltip("仅常规AI：被弹反恢复后暂不举防，仍可观察、预警和反击；不延长动作硬直。")]
        [Min(0f)] public float postDeflectDefenseDelay = .85f;
        [Tooltip("常规对练中看到持续蓄力后，短暂观察再接近抢攻；固定弹反练习保持原节奏。")]
        [Min(0.1f)] public float chargeReactionSeconds = 0.35f;
        [Tooltip("常规模式每轮单刀、连击、重击中的连击上限；1保留旧训练对手行为。")]
        [Range(1, 3)] public int normalComboStrikeCount = 1;
        [Tooltip("可选常规攻击轮换；完整多段动作按各自伤害窗命中。空数组沿用单刀/连击/重击，G练习模式仍沿用原规则。")]
        public SamuraiActionDefinition[] normalAttackActions = new SamuraiActionDefinition[0];
        private SamuraiActionDefinition telegraphedAction;
        private SamuraiActionDefinition telegraphedRangeAction;
        [Tooltip("有逐窗攻击区域的常规招式，按首击对当前目标的实际覆盖距离向内预留此余量。无区域和固定练习保持原距离。")]
        [Min(0f)] public float attackDistanceMargin = .35f;
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
        private readonly SamuraiEnemyAttackAim attackAim = new SamuraiEnemyAttackAim();
        private float dt, targetDistance, cooldown, guardRemaining, telegraphElapsed;
        private float telegraphStartDistance, sideSwitchRemaining, orbitRemaining, orbitRest;
        private int attackSequence, threatSequence, orbitSide = 1;
        private bool canTrain, lineOfSight, telegraphing, heavyPending, heavyReleaseNextFrame;
        private bool wasAttacking, wasInReaction, safetyRetreat, postAttackRetreat, cautious;
        private bool defensiveRetreat, wasThreatening, approachLatched;
        private bool wasDeflected, observedThreatCharging, defenseThreatConsumed;
        private int observedThreatSerial = -1;
        private float defenseObservationElapsed, defenseRearmRemaining, postDeflectDefenseRemaining;
        private SamuraiCombatant defenseObservedTarget;
        private string previousThreatState, intent = "Keeping distance";
        private Vector3 towardTarget, sideDirection;
        private bool practiceSequenceActive, practiceHeavyHolding;
        private int practiceStrikeLimit, queuedPracticeSerial = -1;
        private float practiceHeavyRemaining;
        private float observedChargeSeconds;
        private float retreatElapsed, retreatRest;
        private float perceptionScanRemaining;
        private bool visibleTarget;
        private SamuraiCombatant trackedTarget;
        private SamuraiCombatant subscribedActor;
        private SamuraiActionDefinition distanceAction;
        private SamuraiCombatant distanceTarget;
        private float plannedStrikeDistance, maximumStrikeDistance, distanceRefreshRemaining, distanceTargetHeight;
        private bool actionDistanceAvailable;
        public bool HasVisibleTarget => visibleTarget;
        public bool HasTrackedTarget => trackedTarget != null;
        public SamuraiActionDefinition PlannedAttack => telegraphing ? telegraphedAction ?? telegraphedRangeAction : NextPlanningAction;
        private SamuraiActionDefinition NextPlanningAction => NextCustomAction != null ? NextCustomAction : actor == null || actor.Profile == null ? null :
            NextStrikeIsHeavy ? actor.Profile.heavy : actor.Profile.combo != null && actor.Profile.combo.Length > 0 ? actor.Profile.combo[0] : null;
        public bool UsingActionAttackDistance => !IsPracticeMode && actionDistanceAvailable;
        public float PreferredAttackDistance => StrikeDistance;
        public float MaximumAttackDistance => UsingActionAttackDistance ? maximumStrikeDistance : StrikeDistance;
        public int AttackAimWindowIndex => attackAim.WindowIndex;
        public float AttackAimTurnedDegrees => attackAim.TurnedDegrees;
        public float AttackAimSolutionPhase => attackAim.LastSolutionPhase;
        public Vector3 AttackAimDesiredDirection => attackAim.LastDesiredDirection;

        public bool IsTelegraphing => telegraphing && activeTraining && actor != null && !actor.IsDead;
        public float TelegraphProgress => IsTelegraphing
            ? Mathf.Clamp01(telegraphElapsed / Mathf.Max(0.25f, telegraphSeconds)) : 0f;
        public string DecisionBranch { get; private set; } = "Inactive";
        public string ModeLabel => trainingMode == SamuraiTrainingMode.TripleCombo ? "Triple deflect" :
            trainingMode == SamuraiTrainingMode.MixedDeflect ? "Mixed deflect" : "Normal";
        private SamuraiActionDefinition NextCustomAction => !IsPracticeMode && normalAttackActions != null && normalAttackActions.Length > 0
            ? normalAttackActions[attackSequence % normalAttackActions.Length] : null;
        public string PatternLabel => !IsPracticeMode && normalAttackActions != null && normalAttackActions.Length > 0 && (telegraphing ? telegraphedAction : actor != null && actor.IsAttacking ? actor.Actions.ActiveAction : NextCustomAction) is SamuraiActionDefinition custom
            ? custom.DisplayName : trainingMode == SamuraiTrainingMode.Normal ?
            ((telegraphing || practiceSequenceActive ? heavyPending : NextStrikeIsHeavy) ? "Heavy strike" :
            (telegraphing || practiceSequenceActive ? practiceStrikeLimit : NextStrikeCount) > 1 ? "Two-strike combo" : "Single strike") :
            (telegraphing || practiceSequenceActive ? heavyPending : NextStrikeIsHeavy) ? "Delayed heavy" :
            (telegraphing || practiceSequenceActive ? practiceStrikeLimit : NextStrikeCount) >= 3 ? "Three-strike combo" :
            (telegraphing || practiceSequenceActive ? practiceStrikeLimit : NextStrikeCount) == 2 ? "Two-strike combo" : "Single strike";
        public int PatternStrikeCount => !IsPracticeMode && actor != null && actor.IsAttacking && actor.Actions.ActiveAction != null && normalAttackActions != null && normalAttackActions.Length > 0
            ? actor.Actions.ActiveAction.DamageWindowCount : telegraphing || practiceSequenceActive ? Mathf.Max(1, practiceStrikeLimit) : NextStrikeCount;
        public int PatternStrikeIndex => !practiceSequenceActive || actor == null ? 0 :
            actor.IsCharging ? 1 : actor.IsAttacking ? Mathf.Clamp(actor.ComboIndex + 1, 1, PatternStrikeCount) : 0;
        public float TargetDistance => targetDistance;
        public string Intent => actor == null ? "Unconfigured" : actor.IsDead ? "Defeated - R to reset" :
            !activeTraining ? "Training dummy" : intent;
        private float NearSpacing => Mathf.Max(minimumSafeDistance + 0.2f, spacingMinimum);
        private float FarSpacing => Mathf.Max(NearSpacing + 0.2f, spacingMaximum);
        private float RestSpacing => (NearSpacing + FarSpacing) * 0.5f;
        private float StrikeDistance => Mathf.Max(0.6f, IsPracticeMode ? practiceEngageDistance :
            actionDistanceAvailable ? plannedStrikeDistance : engageDistance);
        // 先进入与本招配套的预警圈，再按原预警时间接近出手点；短招不会从旧观察圈反复超时。
        private float ThreatenDistance => UsingActionAttackDistance ? StrikeDistance + .5f : FarSpacing;
        private float ObservationDistance => UsingActionAttackDistance ? StrikeDistance + .25f : RestSpacing;
        private bool IsPracticeMode => trainingMode != SamuraiTrainingMode.Normal;
        private bool NextStrikeIsHeavy => NextCustomAction == null && trainingMode != SamuraiTrainingMode.TripleCombo && attackSequence % 3 == 2;
        private int NextStrikeCount => NextCustomAction != null ? NextCustomAction.DamageWindowCount : trainingMode == SamuraiTrainingMode.Normal ?
            (attackSequence % 3 == 1 ? Mathf.Clamp(normalComboStrikeCount, 1,
                Mathf.Max(1, actor != null && actor.Profile != null && actor.Profile.combo != null ? actor.Profile.combo.Length : 1)) : 1) :
            trainingMode == SamuraiTrainingMode.TripleCombo ? 3 :
            trainingMode == SamuraiTrainingMode.MixedDeflect && attackSequence % 3 == 1 ? 2 : 1;

        public void Configure(SamuraiCombatant combatant, SamuraiCombatant opponent, SamuraiPlayerInput input = null)
        {
            actor = combatant; target = opponent; playerInput = input;
            attackAim.Reset();
            ResetDecision();
            SubscribeToAttacks();
        }
        private void OnEnable() => SubscribeToAttacks();
        private void Start()
        {
            ResolveReferences();
            // 出生同帧可能已经挨打；启动不能抹掉命中事件建立的交战目标。
            ResetDecision(clearTracking: false);
        }
        private void ResolveReferences()
        {
            // 完整敌人Prefab不能保存场景玩家引用；启动及无目标时只从视野内发现玩家。
            if (actor == null) actor = GetComponent<SamuraiCombatant>();
            if (actor == null) return;
            SubscribeToAttacks();
            if (target == null)
            {
                float nearest = float.PositiveInfinity;
                foreach (var input in FindObjectsByType<SamuraiPlayerInput>())
                {
                    var candidate = input.actor != null ? input.actor : input.GetComponent<SamuraiCombatant>();
                    if (candidate == null || candidate == actor || candidate.Team == actor.Team || candidate.IsDead
                        || !candidate.isActiveAndEnabled || candidate.gameObject.scene != gameObject.scene
                        || !CanSeeTarget(candidate)) continue;
                    float distance = (candidate.transform.position - actor.transform.position).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance; target = candidate;
                }
            }
            if (playerInput == null && target != null) playerInput = target.GetComponent<SamuraiPlayerInput>();
        }
        private void OnDisable()
        {
            UnsubscribeFromAttacks();
            ResetDecision();
            if (actor != null && Application.isPlaying) actor.CancelInput();
        }
        private void SubscribeToAttacks()
        {
            if (!isActiveAndEnabled) return;
            if (actor == null) actor = GetComponent<SamuraiCombatant>();
            if (subscribedActor == actor) return;
            UnsubscribeFromAttacks();
            subscribedActor = actor;
            if (subscribedActor != null)
            {
                subscribedActor.AttackReceived += OnAttackReceived;
                subscribedActor.CombatReset += attackAim.Reset;
                subscribedActor.CombatReset += ResetNormalDefenseTiming;
            }
        }
        private void UnsubscribeFromAttacks()
        {
            if (subscribedActor != null)
            {
                subscribedActor.AttackReceived -= OnAttackReceived;
                subscribedActor.CombatReset -= attackAim.Reset;
                subscribedActor.CombatReset -= ResetNormalDefenseTiming;
            }
            subscribedActor = null;
        }
        private void OnAttackReceived(SamuraiDamageHit hit, SamuraiHitResult result)
        {
            if (!isActiveAndEnabled || !activeTraining || actor == null || !actor.isActiveAndEnabled
                || actor.Profile == null || actor.IsDead) return;
            var attacker = hit.Source as SamuraiCombatant;
            if (attacker == null || attacker == actor || !attacker.isActiveAndEnabled || attacker.IsDead
                || attacker.Team == actor.Team || attacker.gameObject.scene != gameObject.scene
                || attacker.GetComponent<SamuraiPlayerInput>() == null
                || !sensors.IsInsideView(actor, attacker, detectionDistance, 360f, 180f)) return;
            // 命中本身提供来源，不再要求攻击者在前方或另过一次视线检测。
            // 不重置预警、连段、受击/破韧计时，也不强制取消动作或跳过转身。
            if (trackedTarget == attacker) return;
            target = trackedTarget = attacker;
            playerInput = attacker.GetComponent<SamuraiPlayerInput>();
            perceptionScanRemaining = 0f;
            intent = "Attacked - tracking opponent";
        }
        public void ToggleActive() => SetActiveTraining(!activeTraining);
        public void CycleTrainingMode() => SetTrainingMode((SamuraiTrainingMode)(((int)trainingMode + 1) % 3));
        public void SetTrainingMode(SamuraiTrainingMode mode)
        {
            trainingMode = (int)mode >= (int)SamuraiTrainingMode.Normal && (int)mode <= (int)SamuraiTrainingMode.MixedDeflect
                ? mode : SamuraiTrainingMode.Normal;
            ResetDecision(clearTracking: false);
            // 取消的是尚未提交的蓄力/接段；已经出刀的动作继续正常收招。
            if (actor != null) actor.CancelInput();
        }
        public void SetActiveTraining(bool active)
        {
            activeTraining = active;
            ResetDecision();
            if (actor != null) actor.CancelInput();
        }
        public void ResetTraining()
        {
            if (actor != null) actor.ResetCombat();
            attackAim.Reset();
            ResetDecision();
        }
        private void ResetDecision(bool clearTracking = true)
        {
            cooldown = Mathf.Max(0.2f, attackInterval);
            guardRemaining = telegraphElapsed = sideSwitchRemaining = orbitRemaining = 0f;
            orbitRest = 0.6f;
            attackSequence = threatSequence = 0; orbitSide = 1;
            telegraphing = heavyPending = heavyReleaseNextFrame = wasAttacking = wasInReaction = false;
            safetyRetreat = postAttackRetreat = cautious = defensiveRetreat = wasThreatening = approachLatched = false;
            previousThreatState = null; intent = "Keeping distance"; DecisionBranch = "Inactive";
            observedChargeSeconds = 0f;
            retreatElapsed = retreatRest = 0f;
            perceptionScanRemaining = 0f; visibleTarget = false;
            if (clearTracking) trackedTarget = null;
            telegraphedAction = null;
            telegraphedRangeAction = null;
            distanceAction = null; distanceTarget = null; actionDistanceAvailable = false;
            distanceRefreshRemaining = 0f;
            ResetNormalDefenseTiming();
            ClearPracticeIntent();
            if (decisionTree == null) BuildTree();
        }
        private void Update()
        {
            if (actor != null) actor.Tick(BuildFrame(Time.deltaTime), Time.deltaTime);
        }

        /// <summary>测试入口：只生成输入，不调用 Tick。手动驱动时应禁用本组件的 Update。</summary>
        public SamuraiInputFrame BuildFrame(float deltaTime)
        {
            bool deflected = actor != null && actor.IsDeflected;
            if (deflected)
            {
                // 连段被当场截断；即使正处于刀刃交击顿帧，也不保存下一刀或重击释放。
                if (IsPracticeMode || !wasDeflected)
                {
                    ClearPracticeIntent();
                    heavyReleaseNextFrame = false;
                    guardRemaining = 0f;
                    CancelTelegraph(attackInterval);
                    if (!IsPracticeMode)
                    {
                        wasAttacking = false;
                        defenseRearmRemaining = postDeflectDefenseRemaining = 0f;
                        ClearObservedDefenseThreat();
                    }
                }
                frame = new SamuraiInputFrame();
            }
            else if (!IsPracticeMode && wasDeflected)
            {
                postDeflectDefenseRemaining = Mathf.Max(0f, postDeflectDefenseDelay);
                ClearObservedDefenseThreat();
            }
            wasDeflected = deflected;
            // 顿帧只暂停本角色的动作和决策计时，不禁用组件或重置行为树。
            if (actor != null && actor.IsHitStopped)
            {
                // 失焦、停用或任一方死亡仍须立刻丢弃未提交意图，不能借顿帧保存旧连段。
                bool tracking = UpdateTracking();
                if (!TrainingAllowed() || !tracking) { frame = new SamuraiInputFrame(); StopTraining(); return frame; }
                return new SamuraiInputFrame { guardHeld = frame.guardHeld };
            }
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
                Branch("Toughness broken / execution", () => actor.IsToughnessBroken || actor.IsExecuting, WaitForToughness),
                Branch("Committed action", () => heavyReleaseNextFrame || practiceHeavyHolding || actor.IsAttacking || actor.IsCharging || actor.IsDodging, KeepCommitment),
                Branch("Hit reaction", () => !actor.CanAct, RecoverFromHit),
                Branch("Close defense / counter", () => !IsPracticeMode && lineOfSight && targetDistance <= NearSpacing, RespondAtCloseRange),
                Branch("Pressure charge", () => !IsPracticeMode && !cautious && lineOfSight && target.IsCharging
                    && observedChargeSeconds >= Mathf.Max(0.1f, chargeReactionSeconds), PressureCharge),
                Branch("Defensive response", () => cautious || guardRemaining > 0f && lineOfSight
                    && targetDistance <= FarSpacing + 0.1f, RespondToThreat),
                Branch("Telegraphed attack", () => telegraphing || ReadyToThreaten(), TelegraphAttack),
                Branch("Restore spacing", () => retreatRest <= 0f && (safetyRetreat || postAttackRetreat), RestoreSpacing),
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
        private bool TrainingAllowed() => activeTraining && actor != null && actor.isActiveAndEnabled && actor.Profile != null && target != null &&
            !actor.IsDead && !target.IsDead && target.isActiveAndEnabled && actor.Team != target.Team && target.gameObject.scene == gameObject.scene &&
            (!waitForPlayerCapture || playerInput == null || playerInput.HasCursorCapture);
        private bool CanSeeTarget(SamuraiCombatant opponent) => actor != null && actor.Profile != null && opponent != null
            && sensors.CanSee(actor, opponent, detectionDistance, horizontalViewAngle, verticalViewAngle, actor.Profile.obstructionMask);
        private bool UpdateTracking()
        {
            // target可能只是场景预置引用，不能把它直接当作已经发现的目标。
            if (trackedTarget != target) trackedTarget = null;
            bool valid = actor != null && actor.isActiveAndEnabled && actor.Profile != null && !actor.IsDead
                && target != null && target.isActiveAndEnabled && !target.IsDead && actor.Team != target.Team
                && target.gameObject.scene == gameObject.scene
                && sensors.IsInsideView(actor, target, detectionDistance, 360f, 180f);
            if (!valid) { trackedTarget = null; return false; }
            // 朝向与遮挡只用于初次发现。交战后的遮挡仍单独限制出刀，不会清除追踪。
            if (trackedTarget == null && TrainingAllowed() && CanSeeTarget(target)) trackedTarget = target;
            return trackedTarget != null;
        }
        private void PrepareContext()
        {
            perceptionScanRemaining = Mathf.Max(0f, perceptionScanRemaining - dt);
            if ((target == null || !target.isActiveAndEnabled || target.IsDead) && perceptionScanRemaining <= 0f)
            {
                target = null; playerInput = null; ResolveReferences(); perceptionScanRemaining = .2f;
            }
            visibleTarget = false;
            bool tracking = UpdateTracking();
            canTrain = TrainingAllowed() && tracking;
            if (!canTrain) return;
            Vector3 delta = target.transform.position - actor.transform.position;
            delta.y = 0f; targetDistance = delta.magnitude;
            towardTarget = targetDistance > 0.001f ? delta / targetDistance : actor.transform.forward;
            sideDirection = Vector3.Cross(Vector3.up, towardTarget);
            lineOfSight = sensors.HasLineOfSight(actor, target, actor.Profile.obstructionMask);
            visibleTarget = lineOfSight;
            RefreshAttackDistance();
            // 已发现目标可在身后或短暂遮挡时持续转向/寻路；攻击仍要求无遮挡。
            frame.facingWorld = towardTarget;
            observedChargeSeconds = lineOfSight && target.IsCharging ? observedChargeSeconds + dt : 0f;
            cooldown = Mathf.Max(0f, cooldown - dt);
            bool hadGuard = guardRemaining > 0f;
            guardRemaining = Mathf.Max(0f, guardRemaining - dt);
            if (!IsPracticeMode)
            {
                defenseRearmRemaining = Mathf.Max(0f, defenseRearmRemaining - dt);
                postDeflectDefenseRemaining = Mathf.Max(0f, postDeflectDefenseRemaining - dt);
            }
            sideSwitchRemaining = Mathf.Max(0f, sideSwitchRemaining - dt);
            orbitRest = Mathf.Max(0f, orbitRest - dt);
            retreatRest = Mathf.Max(0f, retreatRest - dt);
            if (DecisionBranch != "Restore spacing") retreatElapsed = 0f;
            float ratio = actor.Stamina / actor.MaxStamina;
            if (ratio < cautiousStaminaRatio) cautious = true;
            else if (ratio >= Mathf.Max(cautiousStaminaRatio + 0.15f, recoveredStaminaRatio)) cautious = false;
            if (wasAttacking && !actor.IsAttacking && !actor.IsCharging)
            {
                ClearPracticeIntent();
                postAttackRetreat = true;
                CancelTelegraph(attackInterval);
            }
            wasAttacking = actor.IsAttacking || actor.IsCharging;
            if (actor.IsInHitReaction && !wasInReaction)
            {
                ClearPracticeIntent();
                if (actor.IsGuardHit || actor.IsParrying)
                {
                    // 成功防守后应能迅速转入反击；每次格挡不能重置整轮攻击等待。
                    CancelTelegraph(.12f); cooldown = Mathf.Min(cooldown, .12f);
                }
                else
                {
                    // 常规被弹反的取消已在进入时执行，不能逐帧延后反击时机。
                    if (IsPracticeMode || !actor.IsDeflected) CancelTelegraph(attackInterval);
                    guardRemaining = 0f; postAttackRetreat = true;
                }
            }
            wasInReaction = actor.IsInHitReaction;
            // 预警接近属于有意踏入攻击距离；其他时候不同的进入/退出阈值避免走停抖动。
            float unsafeDistance = telegraphing ? Mathf.Max(0.8f, StrikeDistance - 0.18f) :
                Mathf.Max(minimumSafeDistance, NearSpacing - 0.12f);
            if (targetDistance < unsafeDistance) safetyRetreat = true;
            else if (targetDistance >= NearSpacing - ArrivalTolerance) safetyRetreat = false;
            if (targetDistance >= ObservationDistance - ArrivalTolerance) postAttackRetreat = false;
            if (targetDistance > ThreatenDistance + 0.12f) approachLatched = true;
            else if (targetDistance <= ObservationDistance + ArrivalTolerance) approachLatched = false;
            bool threatening = lineOfSight && targetDistance < FarSpacing + 0.6f && (target.IsAttacking || target.IsCharging);
            if (!IsPracticeMode)
            {
                if (hadGuard && guardRemaining <= 0f)
                    defenseRearmRemaining = Mathf.Max(defenseRearmRemaining, Mathf.Max(0f, defenseRearmSeconds));
                ObserveNormalDefenseThreat(threatening);
            }
            else if (threatening && (!wasThreatening || previousThreatState != target.StateName))
            {
                threatSequence++;
                defensiveRetreat = cautious || target.IsCharging || threatSequence % 3 == 0;
                guardRemaining = Mathf.Max(0.5f, guardSeconds);
                // 贴身预警不能被玩家每一段攻击反复取消，或反复重置反击冷却。
                if (targetDistance > NearSpacing && !telegraphing)
                    CancelTelegraph(Mathf.Max(0.6f, attackInterval));
            }
            wasThreatening = threatening;
            previousThreatState = threatening ? target.StateName : null;
        }

        private bool NormalGuardApproved => guardRemaining > 0f && postDeflectDefenseRemaining <= 0f
            && actor != null && !actor.IsDeflected;

        private void ClearObservedDefenseThreat()
        {
            defenseObservedTarget = null;
            observedThreatSerial = -1;
            observedThreatCharging = defenseThreatConsumed = false;
            defenseObservationElapsed = 0f;
        }

        private void ResetNormalDefenseTiming()
        {
            wasDeflected = false;
            defenseRearmRemaining = postDeflectDefenseRemaining = 0f;
            ClearObservedDefenseThreat();
            if (!IsPracticeMode) guardRemaining = 0f;
        }

        private void ObserveNormalDefenseThreat(bool threatening)
        {
            // 已承诺的攻闪和全部硬直不提前存下一次举防，恢复后重新观察当前威胁。
            if (!threatening || target.Actions == null)
            { ClearObservedDefenseThreat(); return; }
            // 硬直不采集新威胁，但保留已消费身份，避免同一长攻击在恢复后重启守势。
            if (!actor.CanAct) { defenseObservationElapsed = 0f; return; }
            int serial = target.Actions.AttackSerial;
            bool charging = target.IsCharging;
            bool fresh = defenseObservedTarget != target || observedThreatSerial != serial
                || observedThreatCharging != charging;
            if (fresh)
            {
                defenseObservedTarget = target;
                observedThreatSerial = serial;
                observedThreatCharging = charging;
                defenseObservationElapsed = 0f;
                defenseThreatConsumed = false;
            }
            // 持防和重整期间看到的攻击不排队，不能在计时结束时立即补举旧防。
            if (guardRemaining > 0f || defenseRearmRemaining > 0f)
            { defenseThreatConsumed = true; defenseObservationElapsed = 0f; return; }
            if (defenseThreatConsumed) return;
            if (!fresh) defenseObservationElapsed += dt;
            if (postDeflectDefenseRemaining > 0f
                || defenseObservationElapsed < Mathf.Max(0f, defenseReactionSeconds)) return;
            defenseThreatConsumed = true;
            threatSequence++;
            defensiveRetreat = cautious || charging || threatSequence % 3 == 0;
            guardRemaining = Mathf.Max(0.5f, guardSeconds);
            // 远距改为防守时只取消预警一次，不能暂停旧预警后在守势结束直接续出。
            if (targetDistance > NearSpacing)
                CancelTelegraph(Mathf.Max(0.6f, attackInterval));
        }
        private NodeStatus StopTraining()
        {
            visibleTarget = false;
            if (actor != null && (telegraphing || guardRemaining > 0f || heavyReleaseNextFrame || practiceSequenceActive || actor.IsCharging || actor.IsGuarding)) actor.CancelInput();
            ClearPracticeIntent();
            telegraphing = heavyReleaseNextFrame = wasAttacking = wasThreatening = false;
            safetyRetreat = postAttackRetreat = approachLatched = false;
            guardRemaining = 0f; cooldown = Mathf.Max(0.2f, attackInterval);
            ResetNormalDefenseTiming();
            observedChargeSeconds = 0f;
            frame = new SamuraiInputFrame();
            intent = !activeTraining ? "Training dummy" : target == null || target.IsDead ? "Waiting for opponent" :
                waitForPlayerCapture && playerInput != null && !playerInput.HasCursorCapture ? "Waiting for mouse capture" : "No visible opponent";
            return NodeStatus.Success;
        }
        private NodeStatus KeepCommitment()
        {
            intent = actor.IsDodging ? "Evading" : practiceSequenceActive ? PatternLabel + " - committed" : "Committed strike";
            if (actor.IsAttacking && lineOfSight && UsesDirectedEliteAttack(actor.Actions.ActiveAction))
                attackAim.Tick(actor, target, dt);
            if (practiceHeavyHolding)
            {
                if (!actor.IsCharging || !lineOfSight || targetDistance > StrikeDistance + 0.5f)
                {
                    actor.CancelInput(); ClearPracticeIntent(); postAttackRetreat = true; CancelTelegraph(attackInterval);
                }
                else
                {
                    practiceHeavyRemaining = Mathf.Max(0f, practiceHeavyRemaining - dt);
                    frame.heavyHeld = true;
                    intent = "Delayed heavy - holding wind-up";
                    if (practiceHeavyRemaining <= 0f)
                    {
                        frame.heavyHeld = false; frame.heavyReleased = true; practiceHeavyHolding = false;
                    }
                }
            }
            if (heavyReleaseNextFrame)
            {
                heavyReleaseNextFrame = false;
                if (!lineOfSight || targetDistance > StrikeDistance + 0.5f)
                {
                    actor.CancelInput(); postAttackRetreat = true; CancelTelegraph(attackInterval);
                }
                else if (actor.IsCharging) frame.heavyReleased = true;
            }
            QueuePracticeContinuation();
            return NodeStatus.Running;
        }
        private NodeStatus WaitForToughness()
        {
            ClearPracticeIntent(); heavyReleaseNextFrame = false; guardRemaining = 0f;
            CancelTelegraph(attackInterval);
            intent = actor.IsExecutionVulnerable ? "Toughness broken - execution available" : "Execution committed";
            return NodeStatus.Running;
        }
        private NodeStatus RespondAtCloseRange()
        {
            safetyRetreat = postAttackRetreat = false;
            if (telegraphing) return TelegraphAttack();
            if (cooldown <= 0f && CanAffordNextStrike())
                return targetDistance <= ThreatenDistance ? TelegraphAttack() : ApproachTarget();
            // 低精力时靠持防争取恢复，而不是在玩家持续跟进时永远倒退。
            frame.guardHeld = NormalGuardApproved && (cautious || !defensiveRetreat);
            intent = frame.guardHeld ? "Close pressure - defending before counter" : "Close pressure - preparing counter";
            return NodeStatus.Running;
        }
        private void QueuePracticeContinuation()
        {
            if (!practiceSequenceActive || !actor.IsAttacking || heavyPending || actor.Actions == null) return;
            int index = actor.ComboIndex;
            var action = actor.Actions.ActiveAction;
            var combo = actor.Profile.combo;
            if (index < 0 || index + 1 >= practiceStrikeLimit || action == null || combo == null || index + 1 >= combo.Length) return;
            if (queuedPracticeSerial == actor.Actions.AttackSerial || !lineOfSight) return;
            var next = combo[index + 1];
            float normalized = actor.StateNormalizedTime;
            if (next == null || actor.Stamina < Mathf.Max(0f, next.staminaCost) ||
                normalized < action.comboInputStart || normalized > action.comboInputEnd) return;
            // 每一刀只发送一次普通轻击边沿；接段时机和花费完全由正式状态机判断。
            frame.lightPressed = true;
            queuedPracticeSerial = actor.Actions.AttackSerial;
        }
        private NodeStatus RecoverFromHit()
        {
            intent = actor.IsDeflected ? "Deflected - recovering" :
                actor.IsGuardHit ? "Absorbing blocked hit" : "Recovering from hit";
            frame.guardHeld = actor.IsGuardHit && !defensiveRetreat && guardRemaining > 0f && !cautious
                && (IsPracticeMode || NormalGuardApproved);
            return NodeStatus.Running;
        }
        private NodeStatus RestoreSpacing()
        {
            retreatElapsed += dt;
            if (retreatElapsed >= .5f)
            {
                safetyRetreat = postAttackRetreat = false; retreatRest = 1f; retreatElapsed = 0f;
                cooldown = Mathf.Min(cooldown, .25f);
                intent = "Spacing attempt ended - preparing defense or attack";
                return NodeStatus.Success;
            }
            CancelTelegraph(Mathf.Max(0.45f, cooldown));
            intent = postAttackRetreat ? "Recovering distance after strike" : "Too close - stepping back";
            float destination = postAttackRetreat ? ObservationDistance : NearSpacing;
            float amount = MovementAmount(targetDistance < minimumSafeDistance ? 0.85f : 0.55f, destination - targetDistance);
            if (!MoveSafely(-towardTarget, -1f, amount))
            {
                frame.guardHeld = !cautious && lineOfSight && (target.IsAttacking || target.IsCharging)
                    && (IsPracticeMode || NormalGuardApproved);
                intent = "Retreat blocked - holding safe position";
            }
            return NodeStatus.Running;
        }
        private NodeStatus RespondToThreat()
        {
            if (IsPracticeMode) CancelTelegraph(Mathf.Max(0.55f, cooldown));
            if (cautious || defensiveRetreat)
            {
                intent = cautious ? "Low stamina - recovering at distance" : "Reading attack - withdrawing";
                if (targetDistance < FarSpacing + 0.1f && !MoveSafely(-towardTarget, -1f,
                    MovementAmount(cautious ? 0.6f : 0.8f, FarSpacing + 0.1f - targetDistance)))
                {
                    frame.guardHeld = !cautious && target.IsAttacking && lineOfSight
                        && (IsPracticeMode || NormalGuardApproved);
                    intent = "Withdrawal blocked - watching opponent";
                }
            }
            else
            {
                frame.guardHeld = lineOfSight && targetDistance <= FarSpacing + 0.5f
                    && (IsPracticeMode || NormalGuardApproved);
                intent = frame.guardHeld ? "Guarding incoming attack" : "Watching opponent recover";
            }
            return NodeStatus.Running;
        }
        private NodeStatus PressureCharge()
        {
            // 不在安全圈外原地等蓄力结束；接近和出刀仍经原移动检查与预警。
            if (telegraphing || targetDistance <= ThreatenDistance && CanAffordNextStrike())
                return TelegraphAttack();
            return ApproachTarget();
        }
        private bool ReadyToThreaten()
        {
            return cooldown <= 0f && lineOfSight && targetDistance <= ThreatenDistance &&
                CanAffordNextStrike();
        }
        private bool CanAffordNextStrike()
        {
            var p = actor.Profile;
            var custom = telegraphing ? telegraphedAction : NextCustomAction;
            if (custom != null) return custom.IsUsable && actor.Stamina >= Mathf.Max(0f, custom.staminaCost) + actor.MaxStamina * .12f;
            if (NextStrikeIsHeavy)
                return p.heavy != null && actor.Stamina >= Mathf.Max(0f, p.heavy.staminaCost) + actor.MaxStamina * 0.12f;
            int strikes = NextStrikeCount;
            if (p.combo == null || p.combo.Length < strikes) return false;
            float cost = actor.MaxStamina * 0.12f;
            for (int i = 0; i < strikes; i++)
            {
                if (p.combo[i] == null) return false;
                cost += Mathf.Max(0f, p.combo[i].staminaCost);
            }
            return actor.Stamina >= cost;
        }
        private NodeStatus TelegraphAttack()
        {
            if (!telegraphing)
            {
                telegraphedAction = NextCustomAction;
                telegraphedRangeAction = NextPlanningAction;
                telegraphing = true; heavyPending = NextStrikeIsHeavy;
                practiceStrikeLimit = NextStrikeCount;
                telegraphElapsed = 0f; telegraphStartDistance = targetDistance; orbitRemaining = 0f;
            }
            if (!lineOfSight || targetDistance > Mathf.Max(ThreatenDistance + 0.4f, telegraphStartDistance + 0.65f) || !CanAffordNextStrike())
            {
                CancelTelegraph(attackInterval);
                intent = "Attack cancelled - target escaped or obstructed";
                return NodeStatus.Success;
            }
            Vector3 attackDirection = towardTarget;
            bool directed = UsesDirectedEliteAttack(telegraphedAction)
                && SamuraiEnemyAttackAim.TryGetDirection(actor, target, telegraphedAction, 0, 0f, out attackDirection, out _);
            if (!directed) attackDirection = towardTarget;
            else frame.facingWorld = attackDirection;
            telegraphElapsed += dt;
            intent = IsPracticeMode ? PatternLabel + " INCOMING" : heavyPending ? "HEAVY ATTACK INCOMING" : "ATTACK INCOMING";
            float rangeError = targetDistance - StrikeDistance;
            if (rangeError > 0.06f && !MoveSafely(towardTarget, 1f, MovementAmount(0.75f, rangeError)))
            {
                CancelTelegraph(attackInterval); intent = "Attack cancelled - approach blocked";
                return NodeStatus.Success;
            }
            bool facing = Vector3.Angle(actor.transform.forward, attackDirection) < (directed ? 3f : 25f);
            if (telegraphElapsed >= Mathf.Max(0.25f, telegraphSeconds) && rangeError <= 0.12f && facing)
            {
                frame.moveWorld = Vector3.zero; telegraphing = false;
                if (!IsPracticeMode && telegraphedAction != null)
                {
                    if (actor.Actions.TryStartAttack(telegraphedAction, -1, attackDirection)) attackSequence++;
                    telegraphedAction = null;
                    practiceSequenceActive = false;
                    cooldown = Mathf.Max(.2f, attackInterval);
                    return NodeStatus.Running;
                }
                attackSequence++;
                // 常规敌人也可承诺两刀；命中窗口、接段和消耗仍由正式战斗状态机执行。
                practiceSequenceActive = IsPracticeMode || practiceStrikeLimit > 1;
                queuedPracticeSerial = -1;
                if (heavyPending)
                {
                    frame.heavyPressed = frame.heavyHeld = true;
                    practiceHeavyHolding = IsPracticeMode;
                    // 首版练习是延迟普通重击；不因配置值过大变成满蓄力斩。
                    practiceHeavyRemaining = Mathf.Clamp(delayedHeavySeconds, 0f,
                        Mathf.Max(0f, actor.Profile.chargeSeconds - 0.15f));
                    heavyReleaseNextFrame = !practiceHeavyHolding;
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
            float rangeError = targetDistance - ObservationDistance;
            if (rangeError > ArrivalTolerance)
            {
                frame.sprint = targetDistance > ThreatenDistance + 2f && !cautious;
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
            float radial = keepRing ? Mathf.Clamp((targetDistance - ObservationDistance) / 0.25f, -0.6f, 0.6f) : 0f;
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
            telegraphing = false; telegraphElapsed = 0f; telegraphedAction = null; telegraphedRangeAction = null;
            cooldown = Mathf.Max(cooldown, Mathf.Max(0.2f, delay));
        }
        private void ClearPracticeIntent()
        {
            practiceSequenceActive = practiceHeavyHolding = false;
            practiceStrikeLimit = 0; queuedPracticeSerial = -1; practiceHeavyRemaining = 0f;
        }

        private void RefreshAttackDistance()
        {
            var action = PlannedAttack;
            if (IsPracticeMode || action == null || !(action.useAttackVolumes || action.useRootMotion
                && action.enemyRootMotionConfigurationVersion >= 1 && action.weaponRangeFrames != null && action.weaponRangeFrames.Length > 1)
                || actor.Motor == null || target.Motor == null)
            { actionDistanceAvailable = false; distanceAction = null; return; }
            float height = target.Motor.transform.TransformPoint(target.Motor.center).y - actor.transform.position.y;
            distanceRefreshRemaining -= dt;
            if (distanceAction == action && distanceTarget == target && distanceRefreshRemaining > 0f
                && Mathf.Abs(height - distanceTargetHeight) < .04f) return;
            distanceAction = action; distanceTarget = target; distanceTargetHeight = height;
            distanceRefreshRemaining = .25f;
            Vector3 scale = target.Motor.transform.lossyScale;
            float radius = target.Motor.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfSegment = Mathf.Max(0f, target.Motor.height * Mathf.Abs(scale.y) * .5f - radius);
            Vector3 actorScale = actor.Motor.transform.lossyScale;
            float minimum = actor.Motor.radius * Mathf.Max(Mathf.Abs(actorScale.x), Mathf.Abs(actorScale.z)) + radius + .06f;
            actionDistanceAvailable = SamuraiAttackSpacing.TryGetDistance(action, radius, halfSegment, height,
                Mathf.Max(.6f, minimum), attackDistanceMargin, out plannedStrikeDistance, out maximumStrikeDistance);
        }

        private bool UsesDirectedEliteAttack(SamuraiActionDefinition action)
        {
            if (IsPracticeMode || action == null || !action.useRootMotion || !action.useAttackVolumes
                || normalAttackActions == null || Array.IndexOf(normalAttackActions, action) < 0 || action.attackVolumes == null) return false;
            foreach (var region in action.attackVolumes) if (region != null && region.IsUsable) return true;
            return false;
        }

    }
}
