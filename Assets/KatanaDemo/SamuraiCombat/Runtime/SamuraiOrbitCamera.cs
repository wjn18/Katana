using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>第三人称环绕相机；输入由玩家输入组件提供，碰撞查询忽略自己的角色。</summary>
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
        [Min(0.01f)] public float collisionRadius = 0.22f;
        [Min(0.01f)] public float collisionPadding = 0.07f;
        [Tooltip("环境碰撞层；触发器和跟随角色自身自动忽略。")]
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

        /// <summary>碰撞与锁定求值后的无震屏姿态；表现组件只能在此结果上添加旋转。</summary>
        public Quaternion UnshakenRotation { get; private set; } = Quaternion.identity;
        public Vector3 UnshakenPosition { get; private set; }
        public bool HasSolvedPose { get; private set; }
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
            initialized = target != null;
            pivotVelocity = Vector3.zero;
            distanceVelocity = 0f;
            yawVelocity = pitchVelocity = 0f;
            if (!initialized) return;
            pivot = target.position + targetOffset;
            yaw = target.eulerAngles.y + initialYaw;
            pitch = Mathf.Clamp(initialPitch, pitchLimits.x, pitchLimits.y);
            currentDistance = distance;
            framingDistance = distance;
            PlaceCamera(0f);
        }

        private void LateUpdate()
        {
            if (target == null) return;
            if (!initialized) ResetView();
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
                        ref yawVelocity, lockRotationSmoothTime, Mathf.Infinity, Time.deltaTime);
                }
                float desiredPitch = Mathf.Clamp(initialPitch, pitchLimits.x, pitchLimits.y);
                pitch = lockRotationSmoothTime <= 0f ? desiredPitch : Mathf.SmoothDampAngle(pitch, desiredPitch,
                    ref pitchVelocity, lockRotationSmoothTime, Mathf.Infinity, Time.deltaTime);
                // 以双方中点取景，目标越远相机适度后退，玩家与目标均保留在同一视野。
                framingDistance += planar.magnitude * 0.5f;
            }
            else yawVelocity = pitchVelocity = 0f;
            // 大幅传送时直接对齐，避免平滑轨迹从墙或地板内穿过。
            if (Vector3.SqrMagnitude(destination - pivot) > 25f || followSmoothTime <= 0f)
            {
                pivot = destination;
                pivotVelocity = Vector3.zero;
            }
            else pivot = Vector3.SmoothDamp(pivot, destination, ref pivotVelocity,
                followSmoothTime, Mathf.Infinity, Time.deltaTime);
            PlaceCamera(Time.deltaTime);
        }

        private void PlaceCamera(float deltaTime)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 backward = rotation * Vector3.back;
            float safeDistance = framingDistance;
            int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, backward, collisionHits,
                framingDistance + collisionPadding, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = collisionHits[i];
                if (hit.collider == null || IsOwnCollider(hit.collider.transform)) continue;
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

        private bool IsOwnCollider(Transform other)
        {
            if (other == target || other.IsChildOf(target) || other == transform || other.IsChildOf(transform)) return true;
            Transform locked = targetLock != null && targetLock.CurrentTarget != null ? targetLock.CurrentTarget.transform : null;
            return locked != null && (other == locked || other.IsChildOf(locked));
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
        }
    }
}
