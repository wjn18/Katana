using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>第三人称环绕相机；输入由玩家输入组件提供，环境避障忽略所有战斗角色及其子碰撞体。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class SamuraiOrbitCamera : MonoBehaviour
    {
        public Transform target;
        public SamuraiPlayerInput input;
        public SamuraiTargetLock targetLock;
        public Vector3 targetOffset = new Vector3(0f, 1.15f, 0f);
        [Min(0.2f)] public float distance = 4.5f;
        public Vector2 distanceLimits = new Vector2(1.8f, 7f);
        public Vector2 pitchLimits = new Vector2(8f, 68f);
        public float initialYaw;
        public float initialPitch = 24f;
        [Min(0.001f)] public float sensitivity = 0.12f;
        [Min(0f)] public float scrollSensitivity = 0.0035f;
        [Min(0f)] public float followSmoothTime = 0.055f;
        [Min(0f)] public float distanceSmoothTime = 0.12f;
        [Min(0f)] public float lockRotationSmoothTime = 0.14f;
        [Header("中架势冬月位移镜头")]
        public bool middleFrostMoonLagEnabled = true;
        [Tooltip("仅中冬月快速位移时，镜头暂留在移动前的位置；不会改动角色运动。")]
        [Range(0f, 1f)] public float middleFrostMoonLagStrength = .9f;
        [Min(0f)] public float middleFrostMoonMaxLag = 1.35f;
        [Min(.01f)] public float middleFrostMoonLagSeconds = .18f;
        [Min(.01f)] public float middleFrostMoonCatchUpSeconds = .10f;
        [Min(.01f)] public float middleFrostMoonMinSpeed = 1.3f;
        [Min(0.01f)] public float collisionRadius = 0.22f;
        [Min(0.01f)] public float collisionPadding = 0.07f;
        [Tooltip("环境碰撞层；触发器、跟随角色及所有战斗角色（含子碰撞体）自动忽略，不受锁定或生死状态影响。")]
        public LayerMask obstacleMask = ~0;

        private readonly RaycastHit[] collisionHits = new RaycastHit[48];
        private Vector3 pivot;
        private Vector3 pivotVelocity;
        private float yaw;
        private float pitch;
        private float currentDistance;
        private float distanceVelocity;
        private float yawVelocity;
        private float pitchVelocity;
        private float framingDistance;
        private bool initialized;
        private Transform followedTarget;
        private SamuraiCombatant followedActor;
        private Vector3 previousTargetPosition;
        private Vector3 frostMoonLag;

        /// <summary>碰撞与锁定求值后的无震屏姿态；表现组件只能在此结果上添加旋转。</summary>
        public Quaternion UnshakenRotation { get; private set; } = Quaternion.identity;
        public Vector3 UnshakenPosition { get; private set; }
        public bool HasSolvedPose { get; private set; }
        /// <summary>额外水平取景滞后；不含原有普通跟随平滑，不移动相机之外的对象。</summary>
        public Vector3 FrostMoonLagOffset => frostMoonLag;
        public bool IsMiddleFrostMoonLagActive { get; private set; }
        public event System.Action ViewReset;

        public void Configure(Transform followTarget, SamuraiPlayerInput playerInput = null, SamuraiTargetLock lockComponent = null)
        {
            target = followTarget;
            input = playerInput;
            targetLock = lockComponent != null ? lockComponent : playerInput != null ? playerInput.targetLock :
                target != null ? target.GetComponent<SamuraiTargetLock>() : null;
            ResetView();
        }

        private void OnEnable() => ResetView();
        private void OnDisable()
        {
            ViewReset?.Invoke();
            HasSolvedPose = false;
            initialized = false;
            BindFollowTarget(null);
            ClearFrostMoonLag();
        }

        /// <summary>鼠标位移是本帧像素，不再乘 deltaTime；未捕获鼠标时不转镜头。</summary>
        public void ApplyLook(Vector2 pixels, float scroll)
        {
            if (!isActiveAndEnabled || target == null || (input != null && !input.HasCursorCapture)) return;
            if (!initialized) ResetView();
            if (targetLock == null || !targetLock.IsLocked)
            {
                yaw = Mathf.Repeat(yaw + pixels.x * sensitivity + 180f, 360f) - 180f;
                pitch = Mathf.Clamp(pitch - pixels.y * sensitivity, pitchLimits.x, pitchLimits.y);
                yawVelocity = pitchVelocity = 0f;
            }
            distance = Mathf.Clamp(distance - scroll * scrollSensitivity, distanceLimits.x, distanceLimits.y);
        }

        /// <summary>角色复位或传送后清理相机跟随惯性。</summary>
        public void ResetView()
        {
            ViewReset?.Invoke();
            HasSolvedPose = false;
            BindFollowTarget(target);
            ClearFrostMoonLag();
            initialized = target != null;
            pivotVelocity = Vector3.zero;
            distanceVelocity = 0f;
            yawVelocity = pitchVelocity = 0f;
            if (!initialized) return;
            previousTargetPosition = target.position;
            pivot = target.position + targetOffset;
            yaw = target.eulerAngles.y + initialYaw;
            pitch = Mathf.Clamp(initialPitch, pitchLimits.x, pitchLimits.y);
            currentDistance = distance;
            framingDistance = distance;
            PlaceCamera(0f);
        }

        private void LateUpdate() => TickCamera(Time.deltaTime);

        /// <summary>实际LateUpdate与暂停Play检查共用同一求解；deltaTime为游戏时间。</summary>
        public void TickCamera(float deltaTime)
        {
            if (target == null)
            {
                BindFollowTarget(null);
                ClearFrostMoonLag();
                HasSolvedPose = initialized = false;
                return;
            }
            if (!initialized || followedTarget != target) ResetView();
            if (deltaTime <= 0f || Time.timeScale <= 0f) return;
            bool middleFrostMoon = IsMiddleFrostMoon();
            // 只冻结冬月特殊镜头；其他动作仍沿用原有跟随行为。
            if (followedActor != null && followedActor.IsHitStopped
                && (middleFrostMoon || frostMoonLag.sqrMagnitude > .000001f)) return;
            Vector3 targetDelta = target.position - previousTargetPosition;
            previousTargetPosition = target.position;
            if (targetDelta.sqrMagnitude > 25f) { ResetView(); return; }
            Vector3 destination = target.position + targetOffset;
            framingDistance = distance;
            if (targetLock != null && targetLock.CurrentTarget != null)
            {
                Vector3 targetPoint = targetLock.TargetPoint;
                Vector3 direction = targetPoint - destination;
                Vector3 planar = new Vector3(direction.x, 0f, direction.z);
                destination = Vector3.Lerp(destination, targetPoint, 0.5f);
                if (planar.sqrMagnitude > 0.0025f)
                {
                    float desiredYaw = Mathf.Atan2(planar.x, planar.z) * Mathf.Rad2Deg;
                    yaw = lockRotationSmoothTime <= 0f ? desiredYaw : Mathf.SmoothDampAngle(yaw, desiredYaw,
                        ref yawVelocity, lockRotationSmoothTime, Mathf.Infinity, deltaTime);
                }
                float desiredPitch = Mathf.Clamp(initialPitch, pitchLimits.x, pitchLimits.y);
                pitch = lockRotationSmoothTime <= 0f ? desiredPitch : Mathf.SmoothDampAngle(pitch, desiredPitch,
                    ref pitchVelocity, lockRotationSmoothTime, Mathf.Infinity, deltaTime);
                // 以双方中点取景，目标越远相机适度后退，玩家与目标均保留在同一视野。
                framingDistance += planar.magnitude * 0.5f;
            }
            else yawVelocity = pitchVelocity = 0f;
            UpdateFrostMoonLag(targetDelta, middleFrostMoon, deltaTime);
            destination += frostMoonLag;
            // 大幅传送时直接对齐，避免平滑轨迹从墙或地板内穿过。
            if (Vector3.SqrMagnitude(destination - pivot) > 25f || followSmoothTime <= 0f)
            {
                pivot = destination;
                pivotVelocity = Vector3.zero;
            }
            else pivot = Vector3.SmoothDamp(pivot, destination, ref pivotVelocity,
                followSmoothTime, Mathf.Infinity, deltaTime);
            PlaceCamera(deltaTime);
        }

        private bool IsMiddleFrostMoon()
        {
            if (!middleFrostMoonLagEnabled || followedActor == null || !followedActor.IsFrostMoonAttack) return false;
            var stances = followedActor.Profile != null ? followedActor.Profile.stances : null;
            return stances != null && stances.Length > 1 && stances[1] != null
                && stances[1].frostMoon != null && followedActor.Actions.ActiveAction == stances[1].frostMoon;
        }

        private void UpdateFrostMoonLag(Vector3 delta, bool active, float deltaTime)
        {
            delta.y = 0f;
            IsMiddleFrostMoonLagActive = active;
            float seconds = active ? middleFrostMoonLagSeconds : middleFrostMoonCatchUpSeconds;
            frostMoonLag *= Mathf.Exp(-deltaTime / Mathf.Max(.01f, seconds));
            if (active)
            {
                float speed = delta.magnitude / deltaTime;
                float threshold = Mathf.Max(.01f, middleFrostMoonMinSpeed);
                float weight = Mathf.Clamp01((speed - threshold) / threshold);
                frostMoonLag -= delta * (Mathf.Clamp01(middleFrostMoonLagStrength) * weight);
                frostMoonLag = Vector3.ClampMagnitude(frostMoonLag, Mathf.Max(0f, middleFrostMoonMaxLag));
            }
            if (frostMoonLag.sqrMagnitude < .00000001f) frostMoonLag = Vector3.zero;
        }

        private void ClearFrostMoonLag()
        {
            frostMoonLag = Vector3.zero;
            IsMiddleFrostMoonLagActive = false;
        }

        private void BindFollowTarget(Transform next)
        {
            if (followedActor != null) followedActor.CombatReset -= ResetView;
            followedTarget = next;
            followedActor = next != null ? next.GetComponentInParent<SamuraiCombatant>(true) : null;
            if (followedActor != null && isActiveAndEnabled) followedActor.CombatReset += ResetView;
        }

        private void PlaceCamera(float deltaTime)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 backward = rotation * Vector3.back;
            float safeDistance = framingDistance;
            int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, backward, collisionHits,
                framingDistance + collisionPadding, obstacleMask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = collisionHits;
            // 大量角色/子碰撞体填满缓冲时，仍需完整查询才能找到后方的环境障碍。
            if (count == collisionHits.Length)
            {
                hits = Physics.SphereCastAll(pivot, collisionRadius, backward,
                    framingDistance + collisionPadding, obstacleMask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null || IsIgnoredCollider(hit.collider.transform)) continue;
                safeDistance = Mathf.Min(safeDistance, Mathf.Max(0.05f, hit.distance - collisionPadding));
            }
            // 拉近立即执行；仅向外恢复平滑，避免平滑延迟把镜头留在墙内。
            if (safeDistance < currentDistance || deltaTime <= 0f || distanceSmoothTime <= 0f)
            {
                currentDistance = safeDistance;
                distanceVelocity = 0f;
            }
            else currentDistance = Mathf.SmoothDamp(currentDistance, safeDistance, ref distanceVelocity,
                distanceSmoothTime, Mathf.Infinity, deltaTime);
            UnshakenPosition = pivot + backward * currentDistance;
            UnshakenRotation = rotation;
            HasSolvedPose = true;
            transform.SetPositionAndRotation(UnshakenPosition, UnshakenRotation);
        }

        private bool IsIgnoredCollider(Transform other)
        {
            if (other == target || other.IsChildOf(target) || other == transform || other.IsChildOf(transform)) return true;
            Transform locked = targetLock != null && targetLock.CurrentTarget != null ? targetLock.CurrentTarget.transform : null;
            if (locked != null && (other == locked || other.IsChildOf(locked))) return true;
            // 角色碰撞只服务于战斗与移动，任何敌人都不应成为相机的环境障碍。
            // 沿父链识别，覆盖身体、武器和附属碰撞体，禁用的角色组件也仍然适用。
            return other.GetComponentInParent<SamuraiCombatant>(true) != null;
        }

        private void OnValidate()
        {
            distanceLimits.x = Mathf.Max(0.2f, distanceLimits.x);
            distanceLimits.y = Mathf.Max(distanceLimits.x, distanceLimits.y);
            distance = Mathf.Clamp(distance, distanceLimits.x, distanceLimits.y);
            pitchLimits.x = Mathf.Clamp(pitchLimits.x, -10f, 80f);
            pitchLimits.y = Mathf.Clamp(pitchLimits.y, pitchLimits.x, 85f);
            initialPitch = Mathf.Clamp(initialPitch, pitchLimits.x, pitchLimits.y);
            collisionRadius = Mathf.Max(0.01f, collisionRadius);
            collisionPadding = Mathf.Max(0.01f, collisionPadding);
            middleFrostMoonMaxLag = Mathf.Max(0f, middleFrostMoonMaxLag);
            middleFrostMoonLagSeconds = Mathf.Max(.01f, middleFrostMoonLagSeconds);
            middleFrostMoonCatchUpSeconds = Mathf.Max(.01f, middleFrostMoonCatchUpSeconds);
            middleFrostMoonMinSpeed = Mathf.Max(.01f, middleFrostMoonMinSpeed);
        }
    }
}
