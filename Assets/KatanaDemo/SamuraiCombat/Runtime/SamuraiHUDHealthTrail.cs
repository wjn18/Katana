using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>仅供 HUD 使用的扣血残影；不改写角色生命值。调用方提供可暂停的显示时间。</summary>
    public sealed class SamuraiHUDHealthTrail
    {
        bool initialized;
        float previous, maximum, shrinkFrom, shrinkAt;
        public float Fill { get; private set; }
        public bool IsVisible => initialized && Fill > previous + .0001f;

        public void Clear() => initialized = false;

        public float Step(float health, float maxHealth, float now, float holdSeconds, float shrinkSeconds)
        {
            float max = Mathf.Max(1f, maxHealth);
            float current = Mathf.Clamp01(health / max);
            if (!initialized || !Mathf.Approximately(maximum, max) || current > previous + .0001f)
            {
                initialized = true;
                maximum = max;
                Fill = previous = current;
                shrinkFrom = current;
                shrinkAt = now;
                return Fill;
            }
            if (current < previous - .0001f)
            {
                // 连续受击保留尚未收完的损失区间，并重新给予一次完整的阅读时间。
                Fill = Mathf.Max(Fill, previous);
                shrinkFrom = Fill;
                shrinkAt = now + Mathf.Max(0f, holdSeconds);
            }
            previous = current;
            if (now >= shrinkAt)
                Fill = Mathf.Lerp(shrinkFrom, current, shrinkSeconds <= 0f ? 1f : (now - shrinkAt) / shrinkSeconds);
            Fill = Mathf.Max(current, Fill);
            return Fill;
        }
    }
}
