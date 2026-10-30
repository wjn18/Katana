using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 根据首击区域估计出招前的中心间距。只做AI规划，不修改实际判定、动画、转向或伤害。
    /// 目标视为出招正前方的静止竖直胶囊，动作移动仍读取原根曲线或原固定跨步。
    /// </summary>
    public static class SamuraiAttackSpacing
    {
        const float SearchLimit = 16f, DistanceStep = .05f, LinearStep = .08f, AngularStep = 3f;
        const int MaximumPoses = 8192;

        struct Pose
        {
            public float sine, cosine, reach, rootX, rootZ, horizontalRadius;
        }

        public static bool TryGetDistance(SamuraiActionDefinition action, float targetRadius,
            float targetHalfSegment, float targetCenterY, float minimumDistance, float safetyMargin,
            out float preferred, out float maximum)
        {
            preferred = maximum = 0f;
            if (action != null && !action.useAttackVolumes)
                return SamuraiWeaponAttackSpacing.TryGetDistance(action, targetRadius, targetHalfSegment,
                    targetCenterY, minimumDistance, safetyMargin, out preferred, out maximum);
            if (action == null || !action.useAttackVolumes || action.attackVolumes == null
                || action.attackVolumes.Length == 0 || action.attackVolumes[0] == null
                || !action.attackVolumes[0].IsUsable
                || !Finite(targetRadius) || targetRadius < 0f || !Finite(targetHalfSegment) || targetHalfSegment < 0f
                || !Finite(targetCenterY) || !Finite(minimumDistance) || minimumDistance < 0f || minimumDistance > SearchLimit
                || !Finite(safetyMargin) || safetyMargin < 0f) return false;

            var window = action.GetDamageWindow(0);
            if (!Finite(window.x) || !Finite(window.y) || window.x < 0f || window.y > 1f || window.y <= window.x)
                return false;
            var region = action.attackVolumes[0];
            float first = Mathf.Max(window.x, region.frames[0].phase);
            float last = Mathf.Min(window.y, region.frames[region.frames.Length - 1].phase);
            if (last < first) return false;

            var poses = new List<Pose>(Mathf.Min(MaximumPoses, region.frames.Length * 2));
            if (!BuildPoses(action, region, first, last, targetRadius, targetHalfSegment, targetCenterY, poses)
                || poses.Count == 0) return false;

            // 不用区域最大reach充当正前方攻击距离：方向、侧向根位移和高度都可能限制实际交集。
            float searchMaximum = MaximumCandidateDistance(region, poses);
            if (!Finite(searchMaximum) || searchMaximum < minimumDistance) return false;
            searchMaximum = Mathf.Min(SearchLimit, searchMaximum);
            float previous = searchMaximum;
            bool found = false;
            int steps = Mathf.CeilToInt((searchMaximum - minimumDistance) / DistanceStep);
            for (int index = 0; index <= steps; index++)
            {
                float distance = Mathf.Max(minimumDistance, searchMaximum - index * DistanceStep);
                if (!CanContact(distance, region, poses)) { previous = distance; continue; }
                maximum = distance;
                // 细化最外侧粗网格，返回可命中的内侧界限，不把未命中上界作为出手距离。
                float upper = previous;
                for (int iteration = 0; iteration < 10 && upper - maximum > .0005f; iteration++)
                {
                    float middle = (maximum + upper) * .5f;
                    if (CanContact(middle, region, poses)) maximum = middle;
                    else upper = middle;
                }
                found = true;
                break;
            }
            if (!found) return false;

            preferred = Mathf.Max(minimumDistance, maximum - safetyMargin);
            // 不同相位可能形成离散可交区间，内缩后再核对，不能假定整个径向区间连续。
            for (int index = 0; index <= steps; index++)
            {
                if (CanContact(preferred, region, poses)) return true;
                if (preferred <= minimumDistance) break;
                preferred = Mathf.Max(minimumDistance, preferred - DistanceStep);
            }
            preferred = maximum = 0f;
            return false;
        }

        static bool BuildPoses(SamuraiActionDefinition action, SamuraiAttackVolume region,
            float first, float last, float targetRadius, float targetHalfSegment, float targetCenterY,
            List<Pose> poses)
        {
            var phases = new List<float>(region.frames.Length + 2) { first, last };
            for (int index = 0; index < region.frames.Length; index++)
                AddPhase(phases, region.frames[index].phase, first, last);
            if (action.useRootMotion)
            {
                AddCurvePhases(phases, action.rootMotionX, action.RootMotionCurveTimeScale, first, last);
                AddCurvePhases(phases, action.rootMotionZ, action.RootMotionCurveTimeScale, first, last);
            }
            phases.Sort();
            float a = phases[0];
            if (!AddPose(action, region, a, targetRadius, targetHalfSegment, targetCenterY, poses)) return false;
            for (int index = 1; index < phases.Count; index++)
            {
                float b = phases[index];
                if (b <= a) continue;
                var from = region.Evaluate(a);
                var to = region.Evaluate(b);
                Vector3 fromRoot = Motion(action, a), toRoot = Motion(action, b);
                Vector3 middleRoot = Motion(action, (a + b) * .5f);
                if (!Finite(fromRoot) || !Finite(toRoot) || !Finite(middleRoot)) return false;
                float resolution = Mathf.Abs(to.yaw - from.yaw) / AngularStep;
                resolution = Mathf.Max(resolution, Mathf.Abs(to.reach - from.reach) / LinearStep);
                resolution = Mathf.Max(resolution, Mathf.Abs(to.lift - from.lift) / LinearStep);
                resolution = Mathf.Max(resolution, (Vector3.Distance(fromRoot, middleRoot)
                    + Vector3.Distance(middleRoot, toRoot)) / LinearStep);
                // 动画关键帧之外的根曲线弯曲也按120Hz取样；关键帧边界在上面单独切段。
                resolution = Mathf.Max(resolution, (b - a) * action.Duration * 120f);
                if (!Finite(resolution) || resolution > MaximumPoses) return false;
                int steps = Mathf.Max(1, Mathf.CeilToInt(resolution));
                if (poses.Count + steps > MaximumPoses) return false;
                for (int step = 1; step <= steps; step++)
                    if (!AddPose(action, region, Mathf.Lerp(a, b, (float)step / steps),
                        targetRadius, targetHalfSegment, targetCenterY, poses)) return false;
                a = b;
            }
            return true;
        }

        static bool AddPose(SamuraiActionDefinition action, SamuraiAttackVolume region, float phase,
            float targetRadius, float targetHalfSegment, float targetCenterY, List<Pose> poses)
        {
            var frame = region.Evaluate(phase);
            Vector3 root = Motion(action, phase);
            if (!Finite(root)) return false;
            float minimum = region.minHeight + frame.lift;
            float maximum = region.maxHeight + frame.lift;
            float bottom = targetCenterY - targetHalfSegment, top = targetCenterY + targetHalfSegment;
            float verticalGap = Mathf.Max(0f, Mathf.Max(minimum - top, bottom - maximum));
            if (!Finite(verticalGap)) return false;
            // 该相位的区域高度完全错开目标胶囊时，不参与水平攻击距离规划。
            if (verticalGap > targetRadius) return true;
            float horizontalSquared = targetRadius * targetRadius - verticalGap * verticalGap;
            if (!Finite(horizontalSquared)) return false;
            float angle = frame.yaw * Mathf.Deg2Rad;
            poses.Add(new Pose
            {
                sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle), reach = frame.reach,
                rootX = root.x, rootZ = root.z,
                horizontalRadius = Mathf.Sqrt(Mathf.Max(0f, horizontalSquared))
            });
            return true;
        }

        static Vector3 Motion(SamuraiActionDefinition action, float phase) => action.useRootMotion
            ? action.RootMotionPosition(phase) - action.RootMotionPosition(0f)
            : Vector3.forward * (action.stepDistance * action.StepFraction(phase));

        static float MaximumCandidateDistance(SamuraiAttackVolume region, List<Pose> poses)
        {
            float maximum = 0f;
            for (int index = 0; index < poses.Count; index++)
            {
                Pose pose = poses[index];
                float radius = pose.reach + pose.horizontalRadius
                    + (region.shape == SamuraiAttackVolumeShape.Capsule ? region.radius : 0f);
                float squared = radius * radius - pose.rootX * pose.rootX;
                if (!Finite(squared)) return float.NaN;
                if (squared >= 0f) maximum = Mathf.Max(maximum, pose.rootZ + Mathf.Sqrt(squared));
            }
            return maximum;
        }

        static bool CanContact(float distance, SamuraiAttackVolume region, List<Pose> poses)
        {
            for (int index = 0; index < poses.Count; index++)
            {
                Pose pose = poses[index];
                float x = -pose.rootX, z = distance - pose.rootZ;
                var point = new Vector2(pose.cosine * x - pose.sine * z,
                    pose.sine * x + pose.cosine * z);
                if (region.shape == SamuraiAttackVolumeShape.Capsule)
                {
                    var axis = new Vector2(0f, Mathf.Clamp(point.y, region.innerReach, pose.reach));
                    float allowed = region.radius + pose.horizontalRadius;
                    if ((point - axis).sqrMagnitude <= allowed * allowed) return true;
                }
                else
                {
                    Vector2 nearest = ClosestSector(point, region.innerReach, pose.reach, region.halfAngle);
                    if ((nearest - point).sqrMagnitude <= pose.horizontalRadius * pose.horizontalRadius) return true;
                }
            }
            return false;
        }

        // 与真实区域检测使用同一有限环形扇区最近点定义，但不读Collider或提交接触。
        static Vector2 ClosestSector(Vector2 point, float inner, float outer, float halfAngleDegrees)
        {
            float distance = point.magnitude;
            if (halfAngleDegrees >= 180f)
                return distance > .0001f ? point * (Mathf.Clamp(distance, inner, outer) / distance) : new Vector2(0f, inner);
            float halfAngle = halfAngleDegrees * Mathf.Deg2Rad;
            float angle = Mathf.Atan2(point.x, point.y);
            if (Mathf.Abs(angle) <= halfAngle)
                return distance > .0001f ? point * (Mathf.Clamp(distance, inner, outer) / distance) : new Vector2(0f, inner);
            Vector2 left = new Vector2(-Mathf.Sin(halfAngle), Mathf.Cos(halfAngle));
            Vector2 right = new Vector2(-left.x, left.y);
            Vector2 a = left * Mathf.Clamp(Vector2.Dot(point, left), inner, outer);
            Vector2 b = right * Mathf.Clamp(Vector2.Dot(point, right), inner, outer);
            return (a - point).sqrMagnitude <= (b - point).sqrMagnitude ? a : b;
        }

        static void AddCurvePhases(List<float> phases, AnimationCurve curve, float timeScale, float first, float last)
        {
            if (curve == null) return;
            foreach (Keyframe key in curve.keys) AddPhase(phases, key.time / timeScale, first, last);
        }
        static void AddPhase(List<float> phases, float phase, float first, float last)
        {
            if (phase > first && phase < last) phases.Add(phase);
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
