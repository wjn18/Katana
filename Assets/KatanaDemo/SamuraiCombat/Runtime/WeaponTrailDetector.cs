using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>一次查询内刀根和刀尖的世界坐标轨迹；Time以这段轨迹的0–1表示。</summary>
    public struct WeaponTrailSweep
    {
        public Vector3 FromBase, FromTip, ToBase, ToTip;
        public float Radius;
    }

    /// <summary>纯几何接触；Time为刀轨内时间，扇区查询时为归一化表面距离，仅用于排序。</summary>
    public struct WeaponTrailContact
    {
        public Collider Collider;
        public Vector3 Point, BladeBase;
        public float Time;
    }

    /// <summary>由使用方决定哪些Collider能接触、哪些能遮挡，不要求特定角色组件。</summary>
    public interface IWeaponTrailFilter
    {
        bool IsTarget(Collider collider);
        bool IsEnvironment(Collider collider);
    }

    /// <summary>
    /// 不挂载物理组件的武器轨迹查询器。刀身上的虚拟点连续SphereCast，
    /// 起点OverlapCapsule补足静止及初始重叠。每个使用者持有自己的实例。
    /// </summary>
    public sealed class WeaponTrailDetector
    {
        readonly Collider[] overlapBuffer = new Collider[64];
        readonly RaycastHit[] trailBuffer = new RaycastHit[64];
        readonly RaycastHit[] obstructionBuffer = new RaycastHit[32];
        readonly List<WeaponTrailContact> contacts = new List<WeaponTrailContact>(16);

        /// <summary>
        /// 按接触时间排序，保留同一Collider的不同轨迹接触，以便调用方在视线
        /// 遮挡检查后去重。结果复用内部缓冲，仅在本实例下一次Query之前有效。
        /// </summary>
        public IReadOnlyList<WeaponTrailContact> Query(WeaponTrailSweep sweep, int hitMask,
            int obstructionMask, IWeaponTrailFilter filter)
        {
            contacts.Clear();
            if (filter == null) return contacts;
            float radius = Mathf.Max(.01f, sweep.Radius);
            // Cast不报告起点已重叠的目标；此查询同时覆盖静止刀刃。
            int count = Physics.OverlapCapsuleNonAlloc(sweep.FromBase, sweep.FromTip, radius,
                overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            Collider[] overlaps = overlapBuffer;
            if (count == overlapBuffer.Length)
            {
                overlaps = Physics.OverlapCapsule(sweep.FromBase, sweep.FromTip, radius,
                    hitMask, QueryTriggerInteraction.Ignore);
                count = overlaps.Length;
            }
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i];
                if (collider == null) continue;
                AddContact(collider, collider.ClosestPoint((sweep.FromBase + sweep.FromTip) * .5f),
                    sweep.FromBase, 0f, hitMask, filter);
            }

            // 密度由刀长而非帧间位移决定，覆盖Base/Tip之间的刀身。
            float length = Mathf.Max(Vector3.Distance(sweep.FromBase, sweep.FromTip),
                Vector3.Distance(sweep.ToBase, sweep.ToTip));
            int lanes = Mathf.Max(1, Mathf.CeilToInt(length / radius));
            for (int i = 0; i <= lanes; i++)
            {
                float alongBlade = (float)i / lanes;
                CastBladePoint(Vector3.Lerp(sweep.FromBase, sweep.FromTip, alongBlade),
                    Vector3.Lerp(sweep.ToBase, sweep.ToTip, alongBlade), sweep.FromBase, sweep.ToBase,
                    radius, hitMask, obstructionMask, filter);
            }
            contacts.Sort(CompareContacts);
            return contacts;
        }

        /// <summary>
        /// 近身内圈的有限扇区，按最近表面距离和水平方向筛选。遮挡及伤害仍由调用方处理。
        /// 与Query共用结果缓冲；起点在环境内部时拒绝查询，防止射线从墙内漏检。
        /// </summary>
        public IReadOnlyList<WeaponTrailContact> QuerySector(Vector3 origin, Vector3 forward,
            float reach, float halfAngle, int hitMask, int obstructionMask, IWeaponTrailFilter filter)
        {
            contacts.Clear();
            forward.y = 0f;
            if (filter == null || reach <= 0f || float.IsNaN(reach) || float.IsInfinity(reach)
                || float.IsNaN(halfAngle) || forward.sqrMagnitude < .000001f
                || StartsInEnvironment(origin, .01f, obstructionMask, filter)) return contacts;
            forward.Normalize();
            float minimumDot = Mathf.Cos(Mathf.Clamp(halfAngle, 0f, 90f) * Mathf.Deg2Rad);
            int count = Physics.OverlapSphereNonAlloc(origin, reach, overlapBuffer,
                hitMask, QueryTriggerInteraction.Ignore);
            Collider[] overlaps = overlapBuffer;
            if (count == overlapBuffer.Length)
            {
                overlaps = Physics.OverlapSphere(origin, reach, hitMask, QueryTriggerInteraction.Ignore);
                count = overlaps.Length;
            }
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i];
                if (!InMask(collider, hitMask) || !filter.IsTarget(collider)) continue;
                Vector3 point = collider.ClosestPoint(origin);
                Vector3 delta = point - origin;
                if (delta.sqrMagnitude > reach * reach) continue;
                delta.y = 0f;
                // 身体中心已在目标内部时，不能将零向量当作任意方向命中。
                if (delta.sqrMagnitude < .000001f)
                {
                    delta = collider.bounds.center - origin;
                    delta.y = 0f;
                }
                if (delta.sqrMagnitude < .000001f || Vector3.Dot(forward, delta.normalized) < minimumDot) continue;
                AddContact(collider, point, origin, Vector3.Distance(origin, point) / reach, hitMask, filter);
            }
            contacts.Sort(CompareContacts);
            return contacts;
        }

        /// <summary>通用线段遮挡查询；仅由环境过滤规则决定阻挡，不读取战斗状态。</summary>
        public bool Obstructed(Vector3 start, Vector3 end, int mask, IWeaponTrailFilter filter)
        {
            if (filter == null) return false;
            Vector3 ray = end - start;
            float distance = ray.magnitude;
            if (distance <= .01f) return false;
            int count = Physics.RaycastNonAlloc(start, ray / distance, obstructionBuffer, distance,
                mask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = obstructionBuffer;
            if (count == obstructionBuffer.Length)
            {
                hits = Physics.RaycastAll(start, ray / distance, distance, mask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
                if (IsEnvironment(hits[i].collider, mask, filter) && hits[i].distance < distance - .01f)
                    return true;
            return false;
        }

        void CastBladePoint(Vector3 start, Vector3 finish, Vector3 fromBase, Vector3 toBase,
            float radius, int hitMask, int obstructionMask, IWeaponTrailFilter filter)
        {
            Vector3 delta = finish - start;
            float distance = delta.magnitude;
            if (distance < .00001f || StartsInEnvironment(start, radius, obstructionMask, filter)) return;
            int mask = hitMask | obstructionMask;
            int count = Physics.SphereCastNonAlloc(start, radius, delta / distance, trailBuffer,
                distance, mask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = trailBuffer;
            if (count == trailBuffer.Length)
            {
                hits = Physics.SphereCastAll(start, radius, delta / distance, distance, mask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            float blockedAt = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (IsEnvironment(hits[i].collider, obstructionMask, filter))
                    blockedAt = Mathf.Min(blockedAt, hits[i].distance);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                // 零距离接触交给起点Overlap，避免使用Cast未定义的接触点。
                if (hit.distance <= 0f || hit.distance >= blockedAt) continue;
                float time = Mathf.Clamp01(hit.distance / distance);
                AddContact(hit.collider, hit.point, Vector3.Lerp(fromBase, toBase, time),
                    time, hitMask, filter);
            }
        }

        bool StartsInEnvironment(Vector3 point, float radius, int obstructionMask, IWeaponTrailFilter filter)
        {
            int count = Physics.OverlapSphereNonAlloc(point, radius, overlapBuffer,
                obstructionMask, QueryTriggerInteraction.Ignore);
            Collider[] hits = overlapBuffer;
            if (count == overlapBuffer.Length)
            {
                hits = Physics.OverlapSphere(point, radius, obstructionMask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
                if (IsEnvironment(hits[i], obstructionMask, filter)) return true;
            return false;
        }

        void AddContact(Collider collider, Vector3 point, Vector3 bladeBase, float time,
            int hitMask, IWeaponTrailFilter filter)
        {
            if (!InMask(collider, hitMask) || !filter.IsTarget(collider)) return;
            contacts.Add(new WeaponTrailContact
            {
                Collider = collider, Point = point, BladeBase = bladeBase, Time = time
            });
        }

        static bool InMask(Collider collider, int mask) =>
            collider != null && (mask & (1 << collider.gameObject.layer)) != 0;

        static bool IsEnvironment(Collider collider, int mask, IWeaponTrailFilter filter) =>
            InMask(collider, mask) && filter.IsEnvironment(collider);

        static int CompareContacts(WeaponTrailContact a, WeaponTrailContact b) => a.Time.CompareTo(b.Time);
    }
}
