using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>动画保持原速；所有窗口均为该片段的归一化时间。</summary>
    [CreateAssetMenu(menuName = "OC Design/Samurai/Action")]
    public sealed class SamuraiActionDefinition : ScriptableObject
    {
        public AnimationClip clip;
        public string stateName = "Light01";
        [Min(0f)] public float damage = 20f;
        [Min(0f)] public float staminaDamage = 25f;
        [Min(0f)] public float staminaCost = 14f;
        public bool unblockable;

        [Header("命中后退，仅实际受伤或破防生效")]
        [Tooltip("目标沿远离攻击来源的水平方向后退；0关闭。位移经角色碰撞体，不穿墙。")]
        [Min(0f)] public float knockbackDistance;
        [Min(.01f)] public float knockbackSeconds = .18f;

        [Header("命中与连段")]
        [Range(0f, 1f)] public float damageStart = 0.22f;
        [Range(0f, 1f)] public float damageEnd = 0.48f;
        [Range(0f, 1f)] public float comboInputStart = 0.12f;
        [Range(0f, 1f)] public float comboInputEnd = 0.85f;
        [Range(0f, 1f)] public float comboLink = 0.65f;
        [Tooltip("防御或闪避只能在命中段结束后的此处取消后摇。")]
        [Range(0f, 1f)] public float cancelStart = 0.58f;

        [Header("近身补偿，距离为0时关闭")]
        [Tooltip("身体中心到目标碰撞体最近表面的距离；只补刀弧内侧。")]
        [Min(0f)] public float closeRangeReach;
        [Range(0f, 90f)] public float closeRangeHalfAngle = 55f;
        [Range(0f, 1f)] public float closeRangeStart = .10f;
        [Range(0f, 1f)] public float closeRangeEnd = .42f;

        [Header("胶囊位移，动画不驱动根运动")]
        [Min(0f)] public float stepDistance = 0.28f;
        [Range(0f, 1f)] public float stepStart = 0.08f;
        [Range(0f, 1f)] public float stepEnd = 0.40f;
        [Header("仅用于闪避")]
        [Range(0f, 1f)] public float invulnerableStart = 0.10f;
        [Range(0f, 1f)] public float invulnerableEnd = 0.58f;

        public float Duration => clip != null ? Mathf.Max(0.02f, clip.length) : 0.02f;
        public float CancelStart => Mathf.Clamp01(Mathf.Max(damageEnd, cancelStart));
        public bool IsUsable => clip != null && !string.IsNullOrWhiteSpace(stateName);
        public float StepFraction(float normalizedTime)
        {
            float start = Mathf.Clamp01(stepStart);
            float end = Mathf.Clamp(stepEnd, start + 0.001f, 1.001f);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, normalizedTime));
        }
    }
}
