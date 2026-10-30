using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    public enum SamuraiAttackVolumeShape { Sector, Capsule }

    /// <summary>源片段归一化时间的区域姿态；yaw使用展开角度，lift为相对根部的高度偏移。</summary>
    [Serializable]
    public struct SamuraiAttackVolumeFrame
    {
        [Tooltip("整段动画的归一化相位0～1，不是本命中窗内的百分比；必须递增。")]
        public float phase;
        [Tooltip("相对角色正前方的水平角度，单位度；回旋使用连续展开角度。")]
        public float yaw;
        [Tooltip("距角色根部的米制长度；扇形为外半径，胶囊为中心线终点（外沿还加Radius）。")]
        public float reach;
        [Tooltip("米制竖直偏移，叠加到Min Height/Max Height。高举阶段避免区域投到地面。")]
        public float lift;
    }

    /// <summary>一段命中窗的局部米制区域，不引用Animator、目标或伤害数值。</summary>
    [Serializable]
    public sealed class SamuraiAttackVolume
    {
        public bool enabled = true;
        [Tooltip("Sector为水平扇形；Capsule为纵劈的水平胶囊，均受高度范围和命中窗约束。")]
        public SamuraiAttackVolumeShape shape;
        [Tooltip("米制起始长度：扇形的内半径，或胶囊中心线的起点。")]
        [Min(0f)] public float innerReach = .55f;
        [Tooltip("只对Sector生效，左右各此角度；完整角宽为两倍。")]
        [Range(0f, 180f)] public float halfAngle = 14f;
        [Tooltip("只对Capsule生效，单位米，决定宽度及中心线两端圆头外扩。")]
        [Min(0f)] public float radius = .5f;
        [Tooltip("相对角色根部的米制高度范围，再叠每帧Lift。")]
        public float minHeight = .1f, maxHeight = 2.1f;
        public SamuraiAttackVolumeFrame[] frames;

        public bool IsUsable
        {
            get
            {
                if (!enabled || (shape != SamuraiAttackVolumeShape.Sector && shape != SamuraiAttackVolumeShape.Capsule)
                    || !Finite(innerReach) || innerReach < 0f || !Finite(halfAngle) || halfAngle < 0f || halfAngle > 180f
                    || !Finite(radius) || radius < 0f || !Finite(minHeight) || !Finite(maxHeight) || maxHeight < minHeight
                    || frames == null || frames.Length == 0) return false;
                for (int i = 0; i < frames.Length; i++)
                {
                    var f = frames[i];
                    if (!Finite(f.phase) || f.phase < 0f || f.phase > 1f || !Finite(f.yaw)
                        || !Finite(f.reach) || f.reach < innerReach || !Finite(f.lift)
                        || !Finite(minHeight + f.lift) || !Finite(maxHeight + f.lift)
                        || (i > 0 && (f.phase <= frames[i - 1].phase || !Finite(f.yaw - frames[i - 1].yaw)
                            || !Finite(f.lift - frames[i - 1].lift)))) return false;
                }
                return true;
            }
        }

        public SamuraiAttackVolumeFrame Evaluate(float phase)
        {
            if (frames == null || frames.Length == 0 || !Finite(phase)) return default;
            if (phase <= frames[0].phase) return frames[0];
            int last = frames.Length - 1;
            if (phase >= frames[last].phase) return frames[last];
            for (int i = 1; i <= last; i++)
            {
                if (phase > frames[i].phase) continue;
                var a = frames[i - 1]; var b = frames[i];
                float span = b.phase - a.phase;
                float t = span > 0f ? Mathf.Clamp01((phase - a.phase) / span) : 0f;
                return new SamuraiAttackVolumeFrame
                {
                    phase = phase,
                    // 不用LerpAngle：回旋的展开角度可能跨过180或360度。
                    yaw = Mathf.Lerp(a.yaw, b.yaw, t),
                    reach = Mathf.Lerp(a.reach, b.reach, t),
                    lift = Mathf.Lerp(a.lift, b.lift, t)
                };
            }
            return frames[last];
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// 区域的纯几何查询。按关键帧切段并细分平移/转角/伸展/抬升；主角色使用真实竖直胶囊表面相交，
    /// 不是用中心点或包围盒判命中。伤害、遮挡射线和每窗去重留给SamuraiWeaponAttack。
    /// </summary>
    public sealed class SamuraiAttackVolumeDetector
    {
        const float LinearStep = .08f, AngularStep = 3f, Epsilon = .0001f;
        const int MaximumSubsteps = 8192;
        readonly Collider[] candidates = new Collider[64];
        readonly Collider[] environment = new Collider[32];
        readonly List<WeaponTrailContact> contacts = new List<WeaponTrailContact>(32);
        public event Action<SamuraiAttackVolume, Vector3, Quaternion, float> PoseQueried;

        struct Pose
        {
            public Vector3 root;
            public Quaternion rootRotation, rotation;
            public SamuraiAttackVolumeFrame frame;
        }

        public IReadOnlyList<WeaponTrailContact> Query(SamuraiAttackVolume region,
            float fromPhase, float toPhase, Vector3 previousRoot, Quaternion previousRotation,
            Vector3 currentRoot, Quaternion currentRotation, float previousPhase, float currentPhase,
            int hitMask, int obstructionMask, IWeaponTrailFilter filter)
        {
            contacts.Clear();
            if (region == null || !region.IsUsable || filter == null
                || !Finite(fromPhase) || !Finite(toPhase) || toPhase < fromPhase
                || !Finite(previousPhase) || !Finite(currentPhase) || currentPhase < previousPhase
                || !Finite(previousRoot) || !Finite(currentRoot) || !Finite(previousRotation) || !Finite(currentRotation)) return contacts;
            float queryFrom = fromPhase, queryTo = toPhase;
            // 配置只在自身姿态键覆盖的阶段生效；不能把高位预备姿态向整个伤害窗外推。
            fromPhase = Mathf.Max(fromPhase, region.frames[0].phase);
            toPhase = Mathf.Min(toPhase, region.frames[region.frames.Length - 1].phase);
            if (toPhase < fromPhase) return contacts;
            float previousYaw = Yaw(previousRotation), currentYaw = Yaw(currentRotation);
            float a = fromPhase;
            Pose from = GetPose(region, a, previousRoot, currentRoot, previousYaw, currentYaw, previousPhase, currentPhase);
            QueryPose(region, from, a, queryFrom, queryTo, previousRoot, currentRoot,
                previousYaw, currentYaw, previousPhase, currentPhase, hitMask, obstructionMask, filter);
            for (int key = 0; key <= region.frames.Length; key++)
            {
                float b = key < region.frames.Length ? region.frames[key].phase : toPhase;
                if (b <= a || b > toPhase) continue;
                Pose to = GetPose(region, b, previousRoot, currentRoot, previousYaw, currentYaw, previousPhase, currentPhase);
                float rootAngle = Mathf.Abs(Mathf.DeltaAngle(Yaw(from.rootRotation), Yaw(to.rootRotation)));
                float angle = Mathf.Abs(to.frame.yaw - from.frame.yaw) + rootAngle;
                float reach = Mathf.Max(from.frame.reach, to.frame.reach) + (region.shape == SamuraiAttackVolumeShape.Capsule ? region.radius : 0f);
                float resolution = Mathf.Max(angle / AngularStep, Vector3.Distance(from.root, to.root) / LinearStep);
                resolution = Mathf.Max(resolution, Mathf.Abs(to.frame.reach - from.frame.reach) / LinearStep);
                resolution = Mathf.Max(resolution, Mathf.Abs(to.frame.lift - from.frame.lift) / LinearStep);
                resolution = Mathf.Max(resolution, reach * angle * Mathf.Deg2Rad / LinearStep);
                // 拒绝病态的无限大配置，不能以粗采样静默跨过大范围运动。
                if (!Finite(resolution) || resolution > MaximumSubsteps) { contacts.Clear(); return contacts; }
                int steps = Mathf.Max(1, Mathf.CeilToInt(resolution));
                for (int step = 1; step <= steps; step++)
                {
                    float phase = Mathf.Lerp(a, b, (float)step / steps);
                    Pose pose = GetPose(region, phase, previousRoot, currentRoot, previousYaw, currentYaw, previousPhase, currentPhase);
                    QueryPose(region, pose, phase, queryFrom, queryTo, previousRoot, currentRoot,
                        previousYaw, currentYaw, previousPhase, currentPhase, hitMask, obstructionMask, filter);
                }
                a = b; from = to;
                if (a >= toPhase) break;
            }
            return contacts;
        }

        void QueryPose(SamuraiAttackVolume region, Pose pose, float phase, float fromPhase, float toPhase,
            Vector3 previousRoot, Vector3 currentRoot, float previousYaw, float currentYaw,
            float previousPhase, float currentPhase, int hitMask, int obstructionMask, IWeaponTrailFilter filter)
        {
            PoseQueried?.Invoke(region, pose.root, pose.rootRotation, phase);
            float minimum = region.minHeight + pose.frame.lift;
            float maximum = region.maxHeight + pose.frame.lift;
            Vector3 origin = pose.root + Vector3.up * ((minimum + maximum) * .5f);
            float horizontal = pose.frame.reach + (region.shape == SamuraiAttackVolumeShape.Capsule ? region.radius : 0f);
            float halfHeight = (maximum - minimum) * .5f;
            float broadRadius = Mathf.Sqrt(horizontal * horizontal + halfHeight * halfHeight) + Epsilon;
            if (!Finite(origin) || !Finite(broadRadius) || StartsInEnvironment(origin, obstructionMask, filter)) return;
            int count = Physics.OverlapSphereNonAlloc(origin, broadRadius, candidates, hitMask, QueryTriggerInteraction.Ignore);
            Collider[] found = candidates;
            if (count == candidates.Length)
            {
                found = Physics.OverlapSphere(origin, broadRadius, hitMask, QueryTriggerInteraction.Ignore);
                count = found.Length;
            }
            for (int i = 0; i < count; i++)
            {
                Collider collider = found[i];
                if (collider == null || !filter.IsTarget(collider)
                    || !TryContact(collider, region, pose, out Vector3 point)) continue;
                contacts.Add(new WeaponTrailContact
                {
                    Collider = collider, Point = point, BladeBase = origin,
                    Time = toPhase > fromPhase ? Mathf.Clamp01((phase - fromPhase) / (toPhase - fromPhase)) : 0f,
                    Motion = ContactMotion(region, pose, phase, point, previousRoot, currentRoot,
                        previousYaw, currentYaw, previousPhase, currentPhase),
                    HasMotion = true
                });
            }
        }

        static Pose GetPose(SamuraiAttackVolume region, float phase, Vector3 previousRoot, Vector3 currentRoot,
            float previousYaw, float currentYaw, float previousPhase, float currentPhase)
        {
            float span = currentPhase - previousPhase;
            float t = span > .000001f ? Mathf.Clamp01((phase - previousPhase) / span) : 1f;
            Quaternion rootRotation = Quaternion.Euler(0f, Mathf.LerpAngle(previousYaw, currentYaw, t), 0f);
            var frame = region.Evaluate(phase);
            return new Pose
            {
                root = Vector3.Lerp(previousRoot, currentRoot, t), rootRotation = rootRotation,
                rotation = rootRotation * Quaternion.Euler(0f, frame.yaw, 0f), frame = frame
            };
        }

        static bool TryContact(Collider collider, SamuraiAttackVolume region, Pose pose, out Vector3 point)
        {
            var controller = collider as CharacterController;
            if (controller != null && Mathf.Abs(Vector3.Dot(controller.transform.up, Vector3.up)) > .9999f)
            {
                Vector3 scale = controller.transform.lossyScale;
                float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float height = Mathf.Max(controller.height * Mathf.Abs(scale.y), radius * 2f);
                return CapsuleContact(controller.transform.TransformPoint(controller.center), Mathf.Max(0f, height * .5f - radius),
                    radius, region, pose, out point);
            }
            var capsule = collider as CapsuleCollider;
            if (capsule != null && capsule.direction == 1 && Mathf.Abs(Vector3.Dot(capsule.transform.up, Vector3.up)) > .9999f)
            {
                Vector3 scale = capsule.transform.lossyScale;
                float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float height = Mathf.Max(capsule.height * Mathf.Abs(scale.y), radius * 2f);
                return CapsuleContact(capsule.transform.TransformPoint(capsule.center), Mathf.Max(0f, height * .5f - radius),
                    radius, region, pose, out point);
            }
            var sphere = collider as SphereCollider;
            if (sphere != null)
            {
                Vector3 scale = sphere.transform.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                return CapsuleContact(sphere.transform.TransformPoint(sphere.center), 0f, radius, region, pose, out point);
            }
            // 非胶囊受击物以Collider.ClosestPoint和区域投影寻找相交见证；不将AABB重叠直接当命中。
            Vector3 seed = Project(collider.bounds.center, region, pose);
            for (int iteration = 0; iteration < 12; iteration++)
            {
                Vector3 onCollider = collider.ClosestPoint(seed);
                Vector3 onRegion = Project(onCollider, region, pose);
                if ((onCollider - onRegion).sqrMagnitude <= Epsilon * Epsilon)
                {
                    point = onCollider; return true;
                }
                seed = onRegion;
            }
            point = default; return false;
        }

        static bool CapsuleContact(Vector3 center, float halfSegment, float radius,
            SamuraiAttackVolume region, Pose pose, out Vector3 point)
        {
            if (!Finite(center) || !Finite(halfSegment) || !Finite(radius) || halfSegment < 0f || radius < 0f)
            {
                point = default; return false;
            }
            Vector3 local = Quaternion.Inverse(pose.rotation) * (center - pose.root);
            Vector2 nearest = ClosestHorizontal(new Vector2(local.x, local.z), region, pose.frame.reach);
            float minimum = region.minHeight + pose.frame.lift, maximum = region.maxHeight + pose.frame.lift;
            float onAxis = Mathf.Clamp(Mathf.Clamp(local.y, minimum, maximum), local.y - halfSegment, local.y + halfSegment);
            float onBand = Mathf.Clamp(onAxis, minimum, maximum);
            float gapY = onBand - onAxis;
            Vector2 gapXZ = nearest - new Vector2(local.x, local.z);
            if (gapXZ.sqrMagnitude + gapY * gapY > radius * radius + Epsilon * Epsilon)
            {
                point = default; return false;
            }
            // 该区域点同时处于目标胶囊内，是精确重叠见证，可用于遮挡与接触特效。
            point = pose.root + pose.rotation * new Vector3(nearest.x, onBand, nearest.y);
            return true;
        }

        static Vector3 Project(Vector3 point, SamuraiAttackVolume region, Pose pose)
        {
            Vector3 local = Quaternion.Inverse(pose.rotation) * (point - pose.root);
            Vector2 horizontal = ClosestHorizontal(new Vector2(local.x, local.z), region, pose.frame.reach);
            float height = Mathf.Clamp(local.y, region.minHeight + pose.frame.lift, region.maxHeight + pose.frame.lift);
            return pose.root + pose.rotation * new Vector3(horizontal.x, height, horizontal.y);
        }

        static Vector2 ClosestHorizontal(Vector2 point, SamuraiAttackVolume region, float reach)
        {
            if (region.shape == SamuraiAttackVolumeShape.Capsule)
            {
                Vector2 axis = new Vector2(0f, Mathf.Clamp(point.y, region.innerReach, reach));
                Vector2 delta = point - axis;
                float length = delta.magnitude;
                return length <= region.radius || length <= Epsilon ? point : axis + delta * (region.radius / length);
            }
            float distance = point.magnitude;
            float halfAngle = region.halfAngle * Mathf.Deg2Rad;
            if (region.halfAngle >= 180f)
                return distance > Epsilon ? point * (Mathf.Clamp(distance, region.innerReach, reach) / distance) : new Vector2(0f, region.innerReach);
            float angle = Mathf.Atan2(point.x, point.y);
            if (Mathf.Abs(angle) <= halfAngle)
                return distance > Epsilon ? point * (Mathf.Clamp(distance, region.innerReach, reach) / distance) : new Vector2(0f, region.innerReach);
            Vector2 left = new Vector2(-Mathf.Sin(halfAngle), Mathf.Cos(halfAngle));
            Vector2 right = new Vector2(-left.x, left.y);
            Vector2 a = left * Mathf.Clamp(Vector2.Dot(point, left), region.innerReach, reach);
            Vector2 b = right * Mathf.Clamp(Vector2.Dot(point, right), region.innerReach, reach);
            return (a - point).sqrMagnitude <= (b - point).sqrMagnitude ? a : b;
        }

        static Vector3 ContactMotion(SamuraiAttackVolume region, Pose pose, float phase, Vector3 point,
            Vector3 previousRoot, Vector3 currentRoot, float previousYaw, float currentYaw, float previousPhase, float currentPhase)
        {
            float first = region.frames[0].phase, last = region.frames[region.frames.Length - 1].phase;
            float before = Mathf.Max(first, phase - .001f), after = Mathf.Min(last, phase + .001f);
            Pose a = GetPose(region, before, previousRoot, currentRoot, previousYaw, currentYaw, previousPhase, currentPhase);
            Pose b = GetPose(region, after, previousRoot, currentRoot, previousYaw, currentYaw, previousPhase, currentPhase);
            Vector3 local = Quaternion.Inverse(pose.rotation) * (point - pose.root);
            local.y -= pose.frame.lift;
            Vector3 from = MapContact(local, pose.frame.reach, a, region);
            Vector3 to = MapContact(local, pose.frame.reach, b, region);
            return to - from;
        }

        static Vector3 MapContact(Vector3 local, float contactReach, Pose pose, SamuraiAttackVolume region)
        {
            float denominator = contactReach - region.innerReach;
            if (denominator > Epsilon)
            {
                if (region.shape == SamuraiAttackVolumeShape.Capsule)
                    local.z = region.innerReach + Mathf.Clamp01((local.z - region.innerReach) / denominator) * (pose.frame.reach - region.innerReach);
                else
                {
                    float distance = new Vector2(local.x, local.z).magnitude;
                    if (distance > Epsilon)
                    {
                        float mapped = region.innerReach + Mathf.Clamp01((distance - region.innerReach) / denominator) * (pose.frame.reach - region.innerReach);
                        local.x *= mapped / distance; local.z *= mapped / distance;
                    }
                }
            }
            local.y += pose.frame.lift;
            return pose.root + pose.rotation * local;
        }

        bool StartsInEnvironment(Vector3 origin, int mask, IWeaponTrailFilter filter)
        {
            int count = Physics.OverlapSphereNonAlloc(origin, .01f, environment, mask, QueryTriggerInteraction.Ignore);
            Collider[] found = environment;
            if (count == environment.Length)
            {
                found = Physics.OverlapSphere(origin, .01f, mask, QueryTriggerInteraction.Ignore);
                count = found.Length;
            }
            for (int i = 0; i < count; i++) if (found[i] != null && filter.IsEnvironment(found[i])) return true;
            return false;
        }

        static float Yaw(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            return forward.x * forward.x + forward.z * forward.z > .000001f
                ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : rotation.eulerAngles.y;
        }
        static bool Finite(float value) => SamuraiAttackVolume.Finite(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(Quaternion value)
        {
            float length = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w) && Finite(length) && length > .000001f;
        }
    }
}
