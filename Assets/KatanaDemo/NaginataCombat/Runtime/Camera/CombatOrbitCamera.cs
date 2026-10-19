using UnityEngine;

namespace OCDesign.Combat
{
    /// <summary>
    /// 独立的第三人称环绕相机。控制器传入鼠标位移，相机自身不读取任何输入设备。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class CombatOrbitCamera : MonoBehaviour
    {
        [SerializeField, Tooltip("场景中的玩家容器；不将相机作为角色子对象，以免角色转向强制旋转镜头。")]
        private Transform target;

        [SerializeField, Tooltip("相对玩家位置的世界空间观察点偏移，Y 方向通常放在胸口高度。")]
        private Vector3 targetOffset = new Vector3(0f, 1.35f, 0f);

        [SerializeField, Min(0.1f), Tooltip("镜头到观察点的距离，单位为米。")]
        private float distance = 4.8f;

        [SerializeField, Range(-180f, 180f), Tooltip("复位时相对角色朝向的水平角度；0 表示从角色背后观察。")]
        private float initialYaw;

        [SerializeField, Range(1f, 89f), Tooltip("复位时的俯视角度。")]
        private float initialPitch = 22f;

        [SerializeField, Tooltip("最小/最大俯视角度，限制镜头穿过地面或翻转。")]
        private Vector2 pitchLimits = new Vector2(15f, 65f);

        [SerializeField, Min(0.001f), Tooltip("每像素鼠标位移对应的水平旋转角度。")]
        private float horizontalSensitivity = 0.12f;

        [SerializeField, Min(0.001f), Tooltip("每像素鼠标位移对应的垂直旋转角度。")]
        private float verticalSensitivity = 0.1f;

        [SerializeField, Min(0f), Tooltip("观察点跟随平滑时间；0 为立即跟随。")]
        private float followSmoothTime = 0.08f;

        private Vector3 smoothedPivot;
        private Vector3 pivotVelocity;
        private float yaw;
        private float pitch;
        private bool initialized;

        public Transform Target => target;

        /// <summary>注入场景目标，并立即放置镜头，避免首次运行从原点追赶。</summary>
        public void Configure(Transform followTarget)
        {
            if (followTarget != null && (followTarget == transform || followTarget.IsChildOf(transform)))
            {
                Debug.LogError("CombatOrbitCamera 的目标不能是相机自身或相机的子对象。", this);
                return;
            }

            target = followTarget;
            ResetView();
        }

        private void OnEnable()
        {
            ResetView();
        }

        private void Start()
        {
            if (target == null)
                Debug.LogError("CombatOrbitCamera 缺少跟随目标，请由场景控制器调用 Configure。", this);
        }

        /// <summary>输入已经是每帧累计的像素位移，因此不能再次乘以 deltaTime。</summary>
        public void ApplyLook(Vector2 pixelDelta)
        {
            if (!isActiveAndEnabled || target == null)
                return;

            if (!initialized)
                ResetView();

            yaw = Mathf.Repeat(yaw + pixelDelta.x * horizontalSensitivity + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch - pixelDelta.y * verticalSensitivity, pitchLimits.x, pitchLimits.y);
        }

        /// <summary>传送或复位后调用，同时清理跟随惯性并恢复角色背后的观察方向。</summary>
        public void ResetView()
        {
            initialized = target != null;
            pivotVelocity = Vector3.zero;
            if (!initialized)
                return;

            smoothedPivot = target.position + targetOffset;
            yaw = target.eulerAngles.y + initialYaw;
            pitch = Mathf.Clamp(initialPitch, pitchLimits.x, pitchLimits.y);
            PlaceCamera();
        }

        private void LateUpdate()
        {
            if (target == null)
                return;
            if (!initialized)
                ResetView();

            Vector3 desiredPivot = target.position + targetOffset;
            smoothedPivot = followSmoothTime <= 0f
                ? desiredPivot
                : Vector3.SmoothDamp(smoothedPivot, desiredPivot, ref pivotVelocity,
                    followSmoothTime, Mathf.Infinity, Time.deltaTime);
            PlaceCamera();
        }

        private void PlaceCamera()
        {
            Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(smoothedPivot + orbitRotation * Vector3.back * distance,
                orbitRotation);
        }

        private void OnValidate()
        {
            distance = Mathf.Max(0.1f, distance);
            pitchLimits.x = Mathf.Clamp(pitchLimits.x, 1f, 89f);
            pitchLimits.y = Mathf.Clamp(pitchLimits.y, pitchLimits.x, 89f);
            initialPitch = Mathf.Clamp(initialPitch, pitchLimits.x, pitchLimits.y);
            horizontalSensitivity = Mathf.Max(0.001f, horizontalSensitivity);
            verticalSensitivity = Mathf.Max(0.001f, verticalSensitivity);
            followSmoothTime = Mathf.Max(0f, followSmoothTime);
        }
    }
}
