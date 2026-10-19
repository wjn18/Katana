using UnityEngine;

namespace OCDesign.Combat
{
    /// <summary>共享移动参数；运行状态保存在各角色组件上，配置资产不承载运行状态。</summary>
    [CreateAssetMenu(menuName = "OC Design/Combat/Movement Settings")]
    public sealed class CombatMovementSettings : ScriptableObject
    {
        [SerializeField, Min(0.1f)] private float walkSpeed = 1.6f;
        [SerializeField, Min(0.1f)] private float runSpeed = 3.2f;
        [SerializeField, Min(0.1f)] private float acceleration = 12f;
        [SerializeField, Min(0.1f)] private float deceleration = 18f;
        [Tooltip("每秒最大转向角度。角色朝实际输入方向转身。")]
        [SerializeField, Min(1f)] private float turnSpeed = 720f;
        [SerializeField, Min(0.1f)] private float gravity = 20f;
        [SerializeField, Min(1f)] private float terminalFallSpeed = 30f;

        public float WalkSpeed => Mathf.Max(0.1f, walkSpeed);
        public float RunSpeed => Mathf.Max(WalkSpeed, runSpeed);
        public float Acceleration => Mathf.Max(0.1f, acceleration);
        public float Deceleration => Mathf.Max(0.1f, deceleration);
        public float TurnSpeed => Mathf.Max(1f, turnSpeed);
        public float Gravity => Mathf.Max(0.1f, gravity);
        public float TerminalFallSpeed => Mathf.Max(1f, terminalFallSpeed);
    }
}
