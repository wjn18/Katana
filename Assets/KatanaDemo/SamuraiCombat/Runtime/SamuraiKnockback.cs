using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 独立受击位移服务。角色入口在实际受伤后提交，并仅在战斗时间推进时Tick；
    /// 不决定伤害、动作状态或命中停顿。墙体截停的剩余距离不会延后补移。
    /// </summary>
    public sealed class SamuraiKnockback
    {
        readonly Transform root;
        readonly SamuraiCharacterMotor movement;
        Vector3 direction;
        float distance, duration, elapsed;

        public bool IsActive => elapsed < duration;
        public float RemainingSeconds => Mathf.Max(0f, duration - elapsed);

        public SamuraiKnockback(Transform root, SamuraiCharacterMotor movement)
        {
            this.root = root; this.movement = movement;
        }

        public void Apply(Vector3 sourcePosition, float meters, float seconds)
        {
            // 新命中替换上一段后退，避免连击叠加成无法控制的无限滑行。
            Clear();
            if (root == null || movement == null || !Finite(meters) || meters <= 0f || !Finite(seconds)
                || !Finite(sourcePosition.x) || !Finite(sourcePosition.y) || !Finite(sourcePosition.z)) return;
            direction = root.position - sourcePosition;
            direction.y = 0f;
            if (direction.sqrMagnitude < .0001f) direction = -root.forward;
            direction.y = 0f;
            direction.Normalize();
            distance = meters;
            duration = Mathf.Max(.01f, seconds);
        }

        public void Tick(float dt)
        {
            if (!IsActive || !Finite(dt) || dt <= 0f) return;
            float before = Fraction(elapsed / duration);
            elapsed = Mathf.Min(duration, elapsed + dt);
            float after = Fraction(elapsed / duration);
            movement.DisplacePlanar(direction * (distance * (after - before)));
        }

        public void Clear() { direction = Vector3.zero; distance = duration = elapsed = 0f; }

        // 起始冲量较明显，接近结束时自然减速；积分为配置的总距离。
        static float Fraction(float normalized) => 1f - (1f - normalized) * (1f - normalized);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
