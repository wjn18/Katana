using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>角色局部的命中停顿；不修改全局时间，恢复进入停顿前的动画速度。</summary>
    public sealed class SamuraiHitStop
    {
        readonly Animator animator;
        float savedSpeed;
        public float Remaining { get; private set; }
        public bool IsActive => Remaining > 0f;

        public SamuraiHitStop(Animator target) { animator = target; }

        public void Request(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return;
            if (!IsActive && animator != null) savedSpeed = animator.speed;
            Remaining = Mathf.Max(Remaining, Mathf.Clamp(seconds, 0f, .4f));
            if (animator != null) animator.speed = 0f;
        }

        public void Advance(float unscaledDelta)
        {
            if (!IsActive || unscaledDelta <= 0f || float.IsNaN(unscaledDelta) || float.IsInfinity(unscaledDelta)) return;
            Remaining = Mathf.Max(0f, Remaining - unscaledDelta);
            if (!IsActive) RestoreSpeed();
        }

        public void Clear()
        {
            if (IsActive) RestoreSpeed();
            Remaining = 0f;
        }

        void RestoreSpeed()
        {
            // 不覆盖停顿期间其他系统有意设置的非零播放速度。
            if (animator != null && Mathf.Approximately(animator.speed, 0f)) animator.speed = savedSpeed;
        }
    }
}
