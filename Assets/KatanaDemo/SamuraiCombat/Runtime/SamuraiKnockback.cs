using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 独立受力位移服务。角色入口在受伤或成功防御后提交，并仅在战斗时间推进时Tick；
    /// 不决定伤害、动作状态或命中停顿。墙体截停的剩余距离不会延后补移。
    /// </summary>
    public sealed class SamuraiKnockback
    {
        readonly Transform root;
        readonly SamuraiCharacterMotor movement;
        Vector3 direction;
        float distance, duration, elapsed;
        bool rapidDecay;

        public bool IsActive => elapsed < duration;
        public float RemainingSeconds => Mathf.Max(0f, duration - elapsed);

        public SamuraiKnockback(Transform root, SamuraiCharacterMotor movement)
        {
            this.root = root; this.movement = movement;
        }

        public void Apply(Vector3 sourcePosition, float meters, float seconds, bool rapidDecay = false)
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
            this.rapidDecay = rapidDecay;
        }

        public void Tick(float dt)
        {
            if (!IsActive || !Finite(dt) || dt <= 0f) return;
            float before = Fraction(elapsed / duration);
            elapsed = Mathf.Min(duration, elapsed + dt);
            float after = Fraction(elapsed / duration);
            movement.DisplacePlanar(direction * (distance * (after - before)));
        }

        public void Clear() { direction = Vector3.zero; distance = duration = elapsed = 0f; rapidDecay = false; }

        // 原受伤保留二次缓出；防御用三次缓出，初速3D/T、半程降至初速1/4。
        // 用累计路程之差积分，帧率不改变总距离，结束时速度为0。
        float Fraction(float normalized)
        {
            float remaining = 1f - normalized;
            return 1f - remaining * remaining * (rapidDecay ? remaining : 1f);
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
