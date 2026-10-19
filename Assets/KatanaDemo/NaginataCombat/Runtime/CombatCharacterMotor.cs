using UnityEngine;

namespace OCDesign.Combat
{
    /// <summary>负责胶囊体位移、重力、朝向，以及由动画进度同步的攻击跨步和离地高度。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class CombatCharacterMotor : MonoBehaviour
    {
        [SerializeField] private CombatMovementSettings settings;
        private CharacterController capsule;
        private Vector3 horizontalVelocity;
        private float verticalVelocity;
        private CombatAttackDefinition verticalAttack;
        private uint verticalAttackSerial;
        private float attackProgress;
        private float takeoffHeight;
        private bool attackLiftStarted;
        private bool attackLiftFinished;
        private Vector3 attackStepDirection;
        private float attackStepSpeed;
        private bool attackStepDirectionLocked;
        private bool attackStepBraking;
        private readonly RaycastHit[] landingPredictionHits = new RaycastHit[24];

        public float PlanarSpeed { get; private set; }
        public CombatMovementSettings Settings => settings;
        public bool IsAttackAirborne => attackLiftStarted && !attackLiftFinished;
        public bool IsAttackStepping => attackStepSpeed > 0.0001f;
        public Vector3 AttackStepDirection => attackStepDirection;
        /// <summary>最近一次胶囊Move的实际碰撞结果，供落地表现只读观察。</summary>
        public CollisionFlags LastCollisionFlags { get; private set; }

        private void Awake() => capsule = GetComponent<CharacterController>();
        public void Configure(CombatMovementSettings value) => settings = value;

        // 攻击位移窗口和高度共用 Animator 实际进度；每次起手清除上一招或行走的水平惯性。
        public void SetAttackMotion(CombatAttackDefinition attack, float normalizedTime, uint attackSerial = 0)
        {
            if (!isActiveAndEnabled) return;
            if (verticalAttack != attack || verticalAttackSerial != attackSerial)
            {
                if (IsAttackAirborne) verticalVelocity = Mathf.Min(0f, verticalVelocity);
                verticalAttack = attack;
                verticalAttackSerial = attackSerial;
                attackLiftStarted = false;
                attackLiftFinished = false;
                ResetAttackStep();
            }
            attackProgress = Mathf.Clamp01(normalizedTime);
        }

        /// <summary>在水平面重建正交相机基向量，避免俯视导致前后移动变慢或斜走加速。</summary>
        public static Vector3 CameraRelativeDirection(Vector2 input, Vector3 cameraForward)
        {
            input = Vector2.ClampMagnitude(input, 1f);
            cameraForward.y = 0f;
            if (cameraForward.sqrMagnitude < 0.0001f) cameraForward = Vector3.forward;
            cameraForward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, cameraForward);
            return cameraForward * input.y + right * input.x;
        }

        public void Tick(Vector2 move, bool sprint, Vector3 viewForward,
            bool lockMovement, bool lockRotation, float deltaTime)
        {
            if (!isActiveAndEnabled || !settings || !capsule || !capsule.enabled || deltaTime <= 0f) return;
            Vector3 direction = CameraRelativeDirection(move, viewForward);
            if (verticalAttack)
                UpdateAttackStep(direction, deltaTime);
            else
            {
                float speed = sprint ? settings.RunSpeed : settings.WalkSpeed;
                Vector3 desiredVelocity = direction * speed;
                horizontalVelocity = lockMovement ? Vector3.zero : Vector3.MoveTowards(
                    horizontalVelocity, desiredVelocity,
                    (desiredVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude
                        ? settings.Acceleration : settings.Deceleration) * deltaTime);
            }

            if (!lockRotation && !attackStepDirectionLocked && direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction, Vector3.up), settings.TurnSpeed * deltaTime);

            bool driveHeight = verticalAttack && verticalAttack.HasAirborneMotion && !attackLiftFinished;
            float lift = driveHeight ? verticalAttack.EvaluateAirborneHeight(attackProgress) : 0f;
            if (driveHeight && !attackLiftStarted)
            {
                if (attackProgress >= verticalAttack.AirborneEndNormalized)
                { attackLiftFinished = true; driveHeight = false; }
                else if (lift > 0.0001f)
                {
                    // 只从地面起跳；跌落中触发动作不能凭空再跳一次。
                    if (capsule.isGrounded)
                    { takeoffHeight = transform.position.y; attackLiftStarted = true; }
                    else { attackLiftFinished = true; driveHeight = false; }
                }
                else driveHeight = false;
            }

            if (driveHeight && attackLiftStarted)
                verticalVelocity = (takeoffHeight + lift - transform.position.y) / deltaTime;
            else if (capsule.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            else verticalVelocity = Mathf.Max(verticalVelocity - settings.Gravity * deltaTime,
                -settings.TerminalFallSpeed);

            Vector3 before = transform.position;
            CollisionFlags collisions = capsule.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);
            LastCollisionFlags = collisions;
            bool hitCeiling = (collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f;
            bool landed = (collisions & CollisionFlags.Below) != 0 && verticalVelocity < 0f;
            if (hitCeiling || (driveHeight && landed))
            {
                verticalVelocity = hitCeiling ? 0f : -2f;
                attackLiftFinished = true;
            }
            if (driveHeight && attackProgress >= verticalAttack.AirborneEndNormalized)
            {
                attackLiftFinished = true;
                verticalVelocity = capsule.isGrounded ? -2f : Mathf.Min(0f, verticalVelocity);
            }
            // 使用碰撞后的实际速度驱动动画，顶墙时不会原地全速奔跑。
            Vector3 displacement = transform.position - before;
            displacement.y = 0f;
            PlanarSpeed = displacement.magnitude / deltaTime;
        }

        /// <summary>只读预测当前攻击下降过程中的接地；沿现有曲线、重力和已锁跨步分步查询真实地面。</summary>
        public bool TryPredictAttackLanding(float lookAheadSeconds, float normalizedPerSecond,
            LayerMask groundMask, out RaycastHit ground, out float eta)
        {
            ground = default; eta = 0f;
            if (!isActiveAndEnabled || !settings || !capsule || !capsule.enabled || capsule.isGrounded
                || !IsAttackAirborne || !verticalAttack || !verticalAttack.HasAirborneMotion
                || capsule.velocity.y >= -0.0001f || (LastCollisionFlags & CollisionFlags.Above) != 0
                || Time.deltaTime <= 0f || lookAheadSeconds <= 0f || normalizedPerSecond <= 0f
                || float.IsNaN(lookAheadSeconds) || float.IsInfinity(lookAheadSeconds)
                || float.IsNaN(normalizedPerSecond) || float.IsInfinity(normalizedPerSecond)) return false;

            const float contactEpsilon = 0.00005f;
            float step = Mathf.Min(Time.deltaTime, 1f / 60f);
            Vector3 position = transform.position;
            float bottomOffset = capsule.bounds.min.y - position.y;
            float predictedVertical = verticalVelocity, predictedStepSpeed = attackStepSpeed;
            float minimumGroundNormal = Mathf.Max(0.45f, Mathf.Cos(capsule.slopeLimit * Mathf.Deg2Rad));
            bool driveHeight = true;
            float elapsed = 0f;
            while (elapsed < lookAheadSeconds - 0.000001f)
            {
                float delta = Mathf.Min(step, lookAheadSeconds - elapsed);
                elapsed += delta;
                // 使用Motor已应用的进度；LateUpdate的Animator可能已比它前进一帧。
                float progress = Mathf.Clamp01(attackProgress + normalizedPerSecond * elapsed);
                if (attackStepDirectionLocked && verticalAttack.HasStepMotion
                    && progress >= verticalAttack.StepStartNormalized && progress < verticalAttack.StepEndNormalized)
                {
                    float desiredSpeed = attackStepBraking ? 0f : verticalAttack.EvaluateStepSpeed(progress);
                    float acceleration = desiredSpeed > predictedStepSpeed
                        ? verticalAttack.EvaluateStepAcceleration(progress) : verticalAttack.StepBraking;
                    predictedStepSpeed = Mathf.MoveTowards(predictedStepSpeed, desiredSpeed, acceleration * delta);
                }
                else predictedStepSpeed = 0f;
                position += attackStepDirection * (predictedStepSpeed * delta);

                float previousY = position.y;
                if (driveHeight)
                {
                    position.y = takeoffHeight + verticalAttack.EvaluateAirborneHeight(progress);
                    predictedVertical = (position.y - previousY) / delta;
                }
                else
                {
                    predictedVertical = Mathf.Max(predictedVertical - settings.Gravity * delta, -settings.TerminalFallSpeed);
                    position.y += predictedVertical * delta;
                }

                float previousBottom = previousY + bottomOffset, bottom = position.y + bottomOffset;
                if (predictedVertical < 0f)
                {
                    // 每个未来位置单独检查承接地面；没有地面时不按动画时刻发射。
                    Vector3 origin = new Vector3(position.x, previousBottom + 0.4f, position.z);
                    float distance = previousBottom - bottom + 0.4f + capsule.skinWidth + contactEpsilon;
                    int count = Physics.RaycastNonAlloc(origin, Vector3.down, landingPredictionHits,
                        distance, groundMask, QueryTriggerInteraction.Ignore);
                    int nearest = -1; float nearestDistance = float.PositiveInfinity;
                    for (int i = 0; i < count; i++)
                    {
                        RaycastHit hit = landingPredictionHits[i];
                        if (!hit.collider || hit.collider.transform.IsChildOf(transform)
                            || Vector3.Dot(hit.normal, Vector3.up) < minimumGroundNormal
                            || hit.point.y > previousBottom + capsule.skinWidth + contactEpsilon
                            || hit.distance >= nearestDistance) continue;
                        nearest = i; nearestDistance = hit.distance;
                    }
                    if (nearest >= 0 && bottom <= landingPredictionHits[nearest].point.y + capsule.skinWidth + contactEpsilon)
                    { ground = landingPredictionHits[nearest]; eta = elapsed; return true; }
                }
                if (driveHeight && progress >= verticalAttack.AirborneEndNormalized)
                { driveHeight = false; predictedVertical = Mathf.Min(0f, predictedVertical); }
            }
            return false;
        }

        private void UpdateAttackStep(Vector3 inputDirection, float deltaTime)
        {
            // LockMovement 禁止普通行走；攻击配置授权的跨步只在本招窗口内生效。
            // 窗口结束硬停，不把急刹尾速带进收招或下一招。
            if (!verticalAttack.HasStepMotion || attackProgress < verticalAttack.StepStartNormalized
                || attackProgress >= verticalAttack.StepEndNormalized)
            {
                attackStepSpeed = 0f;
                horizontalVelocity = Vector3.zero;
                return;
            }

            bool hasInput = inputDirection.sqrMagnitude > 0.0001f;
            if (!hasInput && attackStepDirectionLocked) attackStepBraking = true;
            float desiredSpeed = hasInput && !attackStepBraking
                ? verticalAttack.EvaluateStepSpeed(attackProgress) : 0f;
            if (!attackStepDirectionLocked && desiredSpeed > 0f)
            {
                // 第一帧真正跨步时统一确定位移与朝向；窗口开启前不提前转身。
                attackStepDirection = inputDirection.normalized;
                attackStepDirectionLocked = true;
                transform.rotation = Quaternion.LookRotation(attackStepDirection, Vector3.up);
                // 锁定持续到本招结束/打断。06第二挥不重新取向，也不再跨步。
            }
            float acceleration = desiredSpeed > attackStepSpeed
                ? verticalAttack.EvaluateStepAcceleration(attackProgress) : verticalAttack.StepBraking;
            attackStepSpeed = Mathf.MoveTowards(attackStepSpeed, desiredSpeed, acceleration * deltaTime);
            horizontalVelocity = attackStepDirection * attackStepSpeed;
        }

        private void ResetAttackStep()
        {
            horizontalVelocity = Vector3.zero;
            attackStepDirection = Vector3.zero;
            attackStepSpeed = 0f;
            attackStepDirectionLocked = false;
            attackStepBraking = false;
        }

        public void ResetMotion()
        {
            LastCollisionFlags = CollisionFlags.None;
            ResetAttackStep();
            verticalVelocity = 0f;
            PlanarSpeed = 0f;
            verticalAttack = null;
            verticalAttackSerial = 0;
            attackLiftStarted = false;
            attackLiftFinished = false;
        }

        private void OnDisable() => ResetMotion();
    }
}
