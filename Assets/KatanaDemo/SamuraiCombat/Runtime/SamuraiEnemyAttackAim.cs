using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>精英逐击准备期的有限转向。只读原招式区域/根曲线，不改动画、窗口或伤害。</summary>
    public sealed class SamuraiEnemyAttackAim
    {
        public const float MaximumTurnPerStrike = 75f;
        public const float LockBeforeSeconds = .12f;
        int serial = -1;
        SamuraiActionDefinition action;
        float lastPhase, lastClock;
        public int WindowIndex { get; private set; } = -1;
        public float TurnedDegrees { get; private set; }
        public float LastSolutionPhase { get; private set; }
        public Vector3 LastDesiredDirection { get; private set; }

        /// <summary>长准备期最多提前0.12秒锁向；短连段留出65%的实际间隙用于有限对准。</summary>
        public static float LockPhase(SamuraiActionDefinition action, int windowIndex)
        {
            if (action == null || windowIndex < 0 || windowIndex >= action.DamageWindowCount) return 0f;
            float opening = windowIndex > 0 ? action.GetDamageWindow(windowIndex - 1).y : 0f;
            float start = action.GetDamageWindow(windowIndex).x;
            if (!Finite(opening) || !Finite(start)) return 0f;
            float preparationSeconds = Mathf.Max(0f, (start - opening) * action.Duration);
            float lockedSeconds = Mathf.Min(LockBeforeSeconds, preparationSeconds * .35f);
            return Mathf.Max(opening, start - lockedSeconds / action.Duration);
        }

        public void Reset()
        {
            serial = -1; action = null; WindowIndex = -1;
            TurnedDegrees = lastPhase = lastClock = LastSolutionPhase = 0f;
            LastDesiredDirection = Vector3.zero;
        }

        public void Tick(SamuraiCombatant actor, SamuraiCombatant target, float dt)
        {
            if (actor == null || target == null || !actor.IsAttacking || actor.IsHitStopped || actor.IsDeflected
                || actor.IsToughnessBroken || actor.IsDead || !Finite(dt) || dt <= 0f || Time.timeScale <= 0f) return;
            var current = actor.Actions.ActiveAction;
            float phase = actor.StateNormalizedTime, clock = actor.Actions.Clock;
            if (serial != actor.Actions.AttackSerial || action != current || phase < lastPhase - .00001f || clock < lastClock)
            {
                Reset(); serial = actor.Actions.AttackSerial; action = current;
            }
            lastPhase = phase; lastClock = clock;
            if (current == null || !current.useRootMotion) return;
            for (int index = 0; index < current.DamageWindowCount; index++)
            {
                Vector2 window = current.GetDamageWindow(index);
                if (phase > window.y) continue;
                // 生效窗完全锁向。短连段按实际间隙缩短预锁时长，避免低帧率饿死对准阶段。
                float opening = index > 0 ? current.GetDamageWindow(index - 1).y : 0f;
                float lockPhase = LockPhase(current, index);
                if (phase >= window.x || phase < opening || phase >= lockPhase) return;
                if (WindowIndex != index) { WindowIndex = index; TurnedDegrees = 0f; }
                if (TurnedDegrees >= MaximumTurnPerStrike - .0001f) return;
                if (!TryGetDirection(actor, target, current, index, phase, out Vector3 direction, out float solutionPhase)) return;
                LastDesiredDirection = direction; LastSolutionPhase = solutionPhase;
                // 低帧率跨越锁向点时只消费准备期剩余时间，不把整帧转向挪进挥击。
                float seconds = Mathf.Min(dt, Mathf.Max(0f, (lockPhase - phase) * current.Duration));
                float budget = Mathf.Min(MaximumTurnPerStrike - TurnedDegrees, Mathf.Max(0f, actor.Profile.turnSpeed) * seconds);
                TurnedDegrees += actor.ReorientCommittedAttack(direction, budget);
                return;
            }
        }

        /// <summary>
        /// 挑选高度有效、径向距离匹配的真实区域中心轴；整圈扫击优先原局部前向的有效轴，
        /// 不平均展开yaw，也不通过扩大命中范围或预知玩家未来位置来瞄准。
        /// </summary>
        public static bool TryGetDirection(SamuraiCombatant actor, SamuraiCombatant target,
            SamuraiActionDefinition action, int windowIndex, float currentPhase,
            out Vector3 direction, out float solutionPhase)
        {
            direction = Vector3.zero; solutionPhase = 0f;
            if (actor == null || target == null || actor.Motor == null || target.Motor == null || action == null
                || !action.useAttackVolumes || action.attackVolumes == null || windowIndex < 0
                || windowIndex >= action.DamageWindowCount || windowIndex >= action.attackVolumes.Length
                || target.IsDead || !target.isActiveAndEnabled || target.Team == actor.Team || !Finite(currentPhase)) return false;
            var region = action.attackVolumes[windowIndex];
            if (region == null || !region.IsUsable) return false;
            Vector2 window = action.GetDamageWindow(windowIndex);
            Vector3 targetCenter = target.Motor.transform.TransformPoint(target.Motor.center);
            Vector3 toTarget = targetCenter - actor.transform.position;
            float height = toTarget.y; toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (!Finite(distance) || distance < .001f || !Finite(height)) return false;
            Vector3 scale = target.Motor.transform.lossyScale;
            float radius = target.Motor.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfSegment = Mathf.Max(0f, target.Motor.height * Mathf.Abs(scale.y) * .5f - radius);
            if (!Finite(radius) || !Finite(halfSegment) || radius < 0f) return false;
            float bearing = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            Vector3 startRoot = action.useRootMotion ? action.RootMotionPosition(currentPhase) : Vector3.forward * (action.stepDistance * action.StepFraction(currentPhase));
            if (!Finite(startRoot)) return false;
            bool found = false; float bestScore = float.PositiveInfinity, bestYaw = 0f;
            for (int index = 0; index < region.frames.Length; index++)
            {
                var frame = region.frames[index];
                if (frame.phase < window.x || frame.phase > window.y || frame.phase < currentPhase) continue;
                float gapY = Mathf.Max(0f, Mathf.Max(region.minHeight + frame.lift - (height + halfSegment),
                    height - halfSegment - region.maxHeight - frame.lift));
                if (gapY > radius) continue;
                float horizontalRadius = Mathf.Sqrt(Mathf.Max(0f, radius * radius - gapY * gapY));
                float allowed = horizontalRadius + (region.shape == SamuraiAttackVolumeShape.Capsule ? region.radius : 0f);
                Vector3 root = (action.useRootMotion ? action.RootMotionPosition(frame.phase)
                    : Vector3.forward * (action.stepDistance * action.StepFraction(frame.phase))) - startRoot;
                if (!Finite(root)) continue;
                float radians = frame.yaw * Mathf.Deg2Rad;
                var axis = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
                var offset = new Vector2(root.x, root.z);
                float projection = Vector2.Dot(offset, axis);
                float discriminant = projection * projection - (offset.sqrMagnitude - distance * distance);
                // 线段端点、离根最近点及与目标径向圆相交的两个点，均按原中心轴范围裁剪。
                for (int candidate = 0; candidate < 5; candidate++)
                {
                    float reach;
                    if (candidate == 0) reach = region.innerReach;
                    else if (candidate == 1) reach = frame.reach;
                    else if (candidate == 2) reach = Mathf.Clamp(-projection, region.innerReach, frame.reach);
                    else
                    {
                        if (discriminant < 0f) continue;
                        reach = Mathf.Clamp(-projection + (candidate == 3 ? 1f : -1f) * Mathf.Sqrt(discriminant), region.innerReach, frame.reach);
                    }
                    Vector2 point = offset + axis * reach;
                    float length = point.magnitude;
                    if (length < .001f) continue;
                    float error = Mathf.Abs(length - distance);
                    if (error > allowed + .0001f) continue;
                    float localYaw = Mathf.Atan2(point.x, point.y) * Mathf.Rad2Deg;
                    // 径向误差优先；同样匹配时选原局部前向的有效接触轴，回旋不会追着每一帧刀尖转圈。
                    float score = error * 4f + Mathf.Abs(localYaw) * .002f;
                    if (score >= bestScore) continue;
                    bestScore = score; bestYaw = bearing - localYaw; solutionPhase = frame.phase; found = true;
                }
            }
            if (found) direction = Quaternion.Euler(0f, bestYaw, 0f) * Vector3.forward;
            return found && Finite(direction);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
