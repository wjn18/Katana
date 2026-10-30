using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>原身体动画的武器端点，供AI规划及根运动挥击的帧间精细采样使用。</summary>
    [Serializable]
    public struct SamuraiWeaponRangeFrame
    {
        public float phase;
        public Vector3 from, to;
    }

    /// <summary>没有招式区域的敌人，按原刀轨和实际根位移估算首击起手距离。</summary>
    public static class SamuraiWeaponAttackSpacing
    {
        struct Pose { public Vector3 from, to; }
        public static bool TryGetDistance(SamuraiActionDefinition action, float radius, float halfSegment,
            float centerY, float minimum, float margin, out float preferred, out float maximum)
        {
            preferred = maximum = 0f;
            if (action == null || !action.useRootMotion || action.enemyRootMotionConfigurationVersion < 1
                || action.weaponRangeFrames == null || action.weaponRangeFrames.Length < 2
                || !Finite(radius) || radius < 0f || !Finite(halfSegment) || halfSegment < 0f
                || !Finite(centerY) || !Finite(minimum) || minimum < 0f || minimum > 16f
                || !Finite(margin) || margin < 0f || !Finite(action.weaponRangeRadius) || action.weaponRangeRadius <= 0f) return false;
            Vector2 window = action.GetDamageWindow(0);
            if (!Finite(window.x) || !Finite(window.y) || window.y <= window.x) return false;
            var poses = new List<Pose>(action.weaponRangeFrames.Length * 2);
            float bound = 0f, previousPhase = -1f;
            Vector3 start = action.RootMotionPosition(0f);
            for (int i = 0; i < action.weaponRangeFrames.Length; i++)
            {
                var f = action.weaponRangeFrames[i];
                if (!Finite(f.phase) || f.phase <= previousPhase || !Finite(f.from) || !Finite(f.to)) return false;
                previousPhase = f.phase;
                if (f.phase < window.x || f.phase > window.y) continue;
                Vector3 root = action.RootMotionPosition(f.phase) - start;
                if (!Finite(root)) return false;
                var pose = new Pose { from = f.from + root, to = f.to + root };
                poses.Add(pose);
                bound = Mathf.Max(bound, Mathf.Max(pose.from.z, pose.to.z) + radius + action.weaponRangeRadius);
            }
            if (poses.Count == 0 || !Finite(bound)) return false;
            bound = Mathf.Min(16f, bound);
            if (bound < minimum) return false;
            float combined = radius + action.weaponRangeRadius;
            float upper = bound;
            bool found = false;
            int steps = Mathf.CeilToInt((bound - minimum) / .04f);
            for (int i = 0; i <= steps; i++)
            {
                float d = Mathf.Max(minimum, bound - i * .04f);
                if (!Contains(poses, d, centerY, halfSegment, combined)) { upper = d; continue; }
                maximum = d;
                for (int k = 0; k < 10 && upper - maximum > .0005f; k++)
                {
                    float mid = (maximum + upper) * .5f;
                    if (Contains(poses, mid, centerY, halfSegment, combined)) maximum = mid;
                    else upper = mid;
                }
                found = true; break;
            }
            if (!found) return false;
            preferred = Mathf.Max(minimum, maximum - margin);
            for (int i = 0; i <= steps; i++)
            {
                if (Contains(poses, preferred, centerY, halfSegment, combined)) return true;
                if (preferred <= minimum) break;
                preferred = Mathf.Max(minimum, preferred - .04f);
            }
            preferred = maximum = 0f;
            return false;
        }

        static bool Contains(List<Pose> poses, float distance, float center, float halfSegment, float radius)
        {
            Vector3 a = new Vector3(0f, center - halfSegment, distance);
            Vector3 b = new Vector3(0f, center + halfSegment, distance);
            foreach (var p in poses) if (SegmentDistanceSquared(a, b, p.from, p.to) <= radius * radius) return true;
            return false;
        }

        static float SegmentDistanceSquared(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
            if (a <= .0000001f && e <= .0000001f) return r.sqrMagnitude;
            if (a <= .0000001f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= .0000001f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denominator = a * e - b * b;
                    s = denominator > .0000001f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return (p1 + d1 * s - p2 - d2 * t).sqrMagnitude;
        }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }
}
