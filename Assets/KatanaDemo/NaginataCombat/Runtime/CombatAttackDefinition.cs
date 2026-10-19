using UnityEngine;

namespace OCDesign.Combat
{
    /// <summary>一招攻击的配置。新增招式时添加资产和 Animator 状态，无需修改移动模块。</summary>
    [CreateAssetMenu(menuName = "OC Design/Combat/Attack Definition")]
    public sealed class CombatAttackDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Attack";
        [Tooltip("包括层名的完整状态路径，例如 Base Layer.NaginataAttack01。")]
        [SerializeField] private string statePath = "Base Layer.NaginataAttack01";
        [SerializeField, Min(0.01f)] private float playbackSpeed = 1f;
        [SerializeField, Min(0f)] private float blendInSeconds = 0.12f;
        [SerializeField] private bool lockMovement = true;
        [SerializeField] private bool lockRotation = true;
        [Tooltip("已缓存下一段普通攻击时，从本招此进度开始衔接，省去后续完整回收。")]
        [SerializeField, Range(0.05f, 0.99f)] private float comboLinkNormalized = 0.6f;
        [Tooltip("晚于此进度的续招点击不缓存；没有续招时当前动画完整收招。")]
        [SerializeField, Range(0.05f, 0.99f)] private float comboInputCloseNormalized = 0.9f;
        [Tooltip("重击的角色胶囊高度，横轴为动画归一化进度，纵轴为离地米数。空曲线表示不驱动起跳。")]
        [SerializeField] private AnimationCurve airborneHeight = new AnimationCurve();
        [SerializeField, HideInInspector] private int comboConfigurationVersion;
        [Header("攻击跨步")]
        [Tooltip("横轴为本招归一化时间，纵轴为跨步目标速度（米/秒）。首尾为0；只有按方向键才启动。")]
        [SerializeField] private AnimationCurve stepSpeed = new AnimationCurve();
        [Tooltip("起步基础加速度（米/秒²），随当前目标速度叠加增益，形成短促冲步。")]
        [SerializeField, Min(0f)] private float stepBaseAcceleration = 18f;
        [SerializeField, Min(0f)] private float stepSpeedAccelerationGain = 9f;
        [Tooltip("减速或松开方向键时的制动加速度（米/秒²）；窗口结束后水平速度归零。")]
        [SerializeField, Min(0.1f)] private float stepBraking = 65f;
        [Header("预留伤害窗口")]
        [Tooltip("每项X/Y为本招归一化开始/结束时间。多次挥击分别设置，间隔不开放判定；本组件不施加伤害。")]
        [SerializeField] private Vector2[] damageWindows = new Vector2[0];
        [SerializeField, HideInInspector] private int attackTimingConfigurationVersion;

        public string DisplayName => displayName;
        public int StateHash => Animator.StringToHash(statePath);
        public float PlaybackSpeed => Mathf.Max(0.01f, playbackSpeed);
        public float BlendInSeconds => Mathf.Max(0f, blendInSeconds);
        public bool LockMovement => lockMovement;
        public bool LockRotation => lockRotation;
        public float ComboLinkNormalized => Mathf.Clamp(comboLinkNormalized, 0.05f, 0.99f);
        public float ComboInputCloseNormalized => Mathf.Clamp(comboInputCloseNormalized, ComboLinkNormalized, 0.99f);
        public bool HasAirborneMotion => airborneHeight != null && airborneHeight.length > 1;
        public float AirborneEndNormalized => HasAirborneMotion ? airborneHeight[airborneHeight.length - 1].time : 0f;
        public float EvaluateAirborneHeight(float normalizedTime) => HasAirborneMotion
            ? Mathf.Max(0f, airborneHeight.Evaluate(Mathf.Clamp01(normalizedTime))) : 0f;
        public bool HasStepMotion => stepSpeed != null && stepSpeed.length > 1;
        public float StepStartNormalized => HasStepMotion ? Mathf.Clamp01(stepSpeed[0].time) : 0f;
        public float StepEndNormalized => HasStepMotion ? Mathf.Clamp01(stepSpeed[stepSpeed.length - 1].time) : 0f;
        public float StepBraking => Mathf.Max(0.1f, stepBraking);
        public float EvaluateStepSpeed(float normalizedTime) => HasStepMotion
            && normalizedTime > StepStartNormalized && normalizedTime < StepEndNormalized
            ? Mathf.Max(0f, stepSpeed.Evaluate(normalizedTime)) : 0f;
        public float EvaluateStepAcceleration(float normalizedTime) => Mathf.Max(0f, stepBaseAcceleration)
            + EvaluateStepSpeed(normalizedTime) * Mathf.Max(0f, stepSpeedAccelerationGain);
        public int DamageWindowCount => damageWindows == null ? 0 : damageWindows.Length;
        public bool HasDamageWindow => DamageWindowCount > 0;
        public Vector2 GetDamageWindow(int index)
        {
            Vector2 window = damageWindows[index];
            return new Vector2(Mathf.Clamp01(window.x), Mathf.Clamp(window.y, Mathf.Clamp01(window.x), 1f));
        }
        public float DamageWindowStartNormalized => HasDamageWindow ? GetDamageWindow(0).x : 0f;
        public float DamageWindowEndNormalized => HasDamageWindow ? GetDamageWindow(DamageWindowCount - 1).y : 0f;
        public bool IsDamageActive(float normalizedTime)
        {
            for (int i = 0; i < DamageWindowCount; i++)
            {
                Vector2 window = GetDamageWindow(i);
                if (normalizedTime >= window.x && normalizedTime < window.y) return true;
            }
            return false;
        }
    }
}
