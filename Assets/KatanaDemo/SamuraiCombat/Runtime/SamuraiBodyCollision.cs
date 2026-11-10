using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 活跃角色身体只在水平面阻挡/滑动，不作为可踩的坡面。原CC继续解算全部环境。
    /// 每次原生Move内临时排除身体对，finally恢复；伤害查询和碰撞层保持原样。
    /// </summary>
    public sealed class SamuraiBodyCollision
    {
        const float Clearance = .001f, Epsilon = .000001f;
        static readonly List<SamuraiCombatant> Actors = new List<SamuraiCombatant>();
        static bool discover = true;
        readonly CharacterController controller;
        readonly List<Body> bodies = new List<Body>(8);
        readonly List<CharacterController> restorePairs = new List<CharacterController>(8);
        int ownIndex;

        struct Body
        {
            public CharacterController controller;
            public SamuraiCombatant actor;
            public Vector2 center;
            public Vector2 forward;
            public float radius, bottom, top, height, forwardClearance, maxTargetHeight;
            public int index;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry() { Actors.Clear(); discover = true; }

        public static void Register(SamuraiCombatant actor)
        {
            if (actor != null && !Actors.Contains(actor)) Actors.Add(actor);
        }

        public static void Unregister(SamuraiCombatant actor) { Actors.Remove(actor); }

        public SamuraiBodyCollision(CharacterController source)
        {
            controller = source;
            if (source != null) Register(source.GetComponent<SamuraiCombatant>());
        }

        public CollisionFlags Move(Vector3 displacement)
        {
            if (controller == null || !controller.enabled || !controller.gameObject.activeInHierarchy
                || !Finite(displacement)) return CollisionFlags.None;
            if (!controller.detectCollisions) return controller.Move(displacement);
            Collect();
            if (bodies.Count == 0) return controller.Move(displacement);
            restorePairs.Clear();
            try
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    var other = bodies[i].controller;
                    if (other == null || !other.enabled) continue;
                    // Collect already excluded user-ignored pairs. Restore only pairs changed here.
                    if (!Physics.GetIgnoreCollision(controller, other))
                    { Physics.IgnoreCollision(controller, other, true); restorePairs.Add(other); }
                }
                CollisionFlags flags = CollisionFlags.None;
                Vector2 correction = Separate(displacement.y);
                if (correction.sqrMagnitude > Epsilon * Epsilon)
                    flags |= Slide(correction, 0f);
                // Each contact/tangent is moved as its own segment. A straight chord to the
                // endpoint of several safe segments could cross a different character.
                Vector2 requested = new Vector2(displacement.x, displacement.z);
                int parts = 1;
                Body own = Snapshot(controller, ownIndex);
                float safeStep = Mathf.Min(.15f, own.radius * .25f);
                bool near = false;
                for (int i = 0; i < bodies.Count; i++)
                {
                    safeStep = Mathf.Min(safeStep, bodies[i].radius * .25f);
                    float reach = own.radius + bodies[i].radius + requested.magnitude + .01f
                        + Extension(own, bodies[i]).magnitude + Extension(bodies[i], own).magnitude;
                    if ((own.center - bodies[i].center).sqrMagnitude <= reach * reach) near = true;
                }
                if (near) parts = Mathf.Max(1, Mathf.CeilToInt(requested.magnitude / Mathf.Max(.005f, safeStep)));
                for (int part = 0; part < parts; part++)
                {
                    flags |= Slide(requested / parts, displacement.y / parts);
                    correction = Separate(0f);
                    if (correction.sqrMagnitude > Epsilon * Epsilon) flags |= Slide(correction, 0f);
                }
                // Environment sliding can change the actual XZ endpoint. Recheck that endpoint,
                // and constrain each separation through the same environment-aware CC.
                for (int iteration = 0; iteration < 4; iteration++)
                {
                    correction = Separate(0f);
                    if (correction.sqrMagnitude <= Epsilon * Epsilon) break;
                    Vector3 before = controller.transform.position;
                    flags |= Slide(correction, 0f);
                    if ((controller.transform.position - before).sqrMagnitude < Epsilon * Epsilon) break;
                }
                return flags;
            }
            finally
            {
                for (int i = 0; i < restorePairs.Count; i++)
                    if (controller != null && restorePairs[i] != null)
                        Physics.IgnoreCollision(controller, restorePairs[i], false);
                restorePairs.Clear();
            }
        }

        void Collect()
        {
            bodies.Clear();
            if (discover)
            {
                discover = false;
                foreach (var actor in Object.FindObjectsByType<SamuraiCombatant>()) Register(actor);
            }
            for (int i = Actors.Count - 1; i >= 0; i--) if (Actors[i] == null) Actors.RemoveAt(i);
            var owner = controller.GetComponent<SamuraiCombatant>();
            Register(owner); ownIndex = Actors.IndexOf(owner);
            var physicsScene = controller.gameObject.scene.GetPhysicsScene();
            for (int i = 0; i < Actors.Count; i++)
            {
                var actor = Actors[i];
                var other = actor != null ? actor.Motor : null;
                if (other == null || other == controller || !other.enabled || !other.detectCollisions
                    || !other.gameObject.activeInHierarchy || other.gameObject.scene.GetPhysicsScene() != physicsScene
                    || Physics.GetIgnoreLayerCollision(controller.gameObject.layer, other.gameObject.layer)
                    || Physics.GetIgnoreCollision(controller, other)) continue;
                bodies.Add(Snapshot(other, i));
            }
        }

        static Body Snapshot(CharacterController source, int index)
        {
            Vector3 scale = source.transform.lossyScale;
            float horizontal = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float radius = source.radius * horizontal;
            float height = Mathf.Max(source.height * Mathf.Abs(scale.y), radius * 2f);
            float skin = source.skinWidth * horizontal;
            Vector3 center = source.transform.TransformPoint(source.center);
            var actor = source.GetComponent<SamuraiCombatant>();
            var profile = actor != null ? actor.Profile : null;
            Vector3 facing = source.transform.forward;
            float padding = profile != null ? profile.bodyForwardClearance : 0f;
            float maxHeight = profile != null ? profile.bodyForwardClearanceMaxTargetHeight : 0f;
            return new Body { controller = source, actor = actor, center = new Vector2(center.x, center.z),
                forward = new Vector2(facing.x, facing.z).normalized, height = height,
                forwardClearance = Finite(padding) ? Mathf.Max(0f, padding) : 0f,
                maxTargetHeight = Finite(maxHeight) ? Mathf.Max(0f, maxHeight) : 0f,
                radius = radius + skin + Clearance * .5f, bottom = center.y - height * .5f - skin,
                top = center.y + height * .5f + skin, index = index };
        }

        static Vector2 Extension(Body body, Body other)
        {
            if (body.forwardClearance <= 0f || other.height > body.maxTargetHeight
                || body.actor == null || other.actor == null || body.actor.Team == other.actor.Team
                || !body.actor.isActiveAndEnabled || !other.actor.isActiveAndEnabled
                || body.actor.IsDead || other.actor.IsDead
                || body.actor.IsExecuting || body.actor.IsBeingExecuted
                || other.actor.IsExecuting || other.actor.IsBeingExecuted) return Vector2.zero;
            return body.forward * body.forwardClearance;
        }

        // Closest points of the two forward centre segments. Their radius sums give the
        // exact XZ capsule separation, including when both profiles opt into clearance.
        static Vector2 SegmentDelta(Vector2 first, Vector2 firstExtension, Vector2 second, Vector2 secondExtension)
        {
            Vector2 offset = first - second;
            float a = firstExtension.sqrMagnitude, e = secondExtension.sqrMagnitude;
            if (a <= Epsilon * Epsilon && e <= Epsilon * Epsilon) return offset;
            float s, t;
            float f = Vector2.Dot(secondExtension, offset);
            if (a <= Epsilon * Epsilon) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector2.Dot(firstExtension, offset);
                if (e <= Epsilon * Epsilon) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector2.Dot(firstExtension, secondExtension);
                    float denominator = a * e - b * b;
                    s = denominator > Epsilon * Epsilon ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return offset + firstExtension * s - secondExtension * t;
        }

        static Vector2 ContactNormal(Vector2 delta, Vector2 centerDelta, int firstIndex, int secondIndex)
        {
            if (delta.sqrMagnitude > Epsilon * Epsilon) return delta.normalized;
            if (centerDelta.sqrMagnitude > Epsilon * Epsilon) return centerDelta.normalized;
            return new Vector2(firstIndex < secondIndex ? -1f : 1f, 0f);
        }

        // Convex capsule distance supplies a supporting plane at every step. Advancing
        // only to that plane cannot skip a contact; the fixed budget stops safely at
        // the last bound for near-tangent convergence instead of allowing tunnelling.
        static bool SweepExtended(Body own, Vector2 position, Body other, Vector2 ownExtension,
            Vector2 otherExtension, Vector2 displacement, float radius, float limit,
            out float fraction, out Vector2 normal)
        {
            fraction = 0f; normal = Vector2.zero;
            for (int iteration = 0; iteration < 24; iteration++)
            {
                Vector2 current = position + displacement * fraction;
                Vector2 delta = SegmentDelta(current, ownExtension, other.center, otherExtension);
                float distance = delta.magnitude;
                normal = ContactNormal(delta, current - other.center, own.index, other.index);
                float closing = -Vector2.Dot(displacement, normal);
                if (closing <= Epsilon) return false;
                if (distance <= radius + .00001f) return true;
                float next = fraction + (distance - radius) / closing;
                if (next > limit || next < fraction) return false;
                fraction = next;
            }
            return true;
        }

        /// <summary>攻击推人的接触距离与前向身体留距共用；无额外留距时返回false，保留旧圆形求解。</summary>
        public static bool TryGetExtendedContactDistance(CharacterController first, CharacterController second,
            Vector3 direction, float gap, out float distance)
        {
            distance = float.PositiveInfinity;
            if (first == null || second == null || !Finite(direction)) return false;
            Body own = Snapshot(first, -1), other = Snapshot(second, 0);
            Vector2 ownExtension = Extension(own, other), otherExtension = Extension(other, own);
            if (ownExtension.sqrMagnitude <= Epsilon * Epsilon && otherExtension.sqrMagnitude <= Epsilon * Epsilon) return false;
            Vector2 planar = new Vector2(direction.x, direction.z);
            if (planar.sqrMagnitude <= Epsilon * Epsilon || !VerticalOverlap(own, other, 0f)) return true;
            planar.Normalize();
            float radius = own.radius + other.radius - Clearance + Mathf.Max(0f, gap);
            float range = (own.center - other.center).magnitude + ownExtension.magnitude + otherExtension.magnitude + radius + 1f;
            float fraction; Vector2 normal;
            if (SweepExtended(own, own.center, other, ownExtension, otherExtension,
                planar * range, radius, 1f, out fraction, out normal)) distance = fraction * range;
            return true;
        }

        /// <summary>物理胶囊查询的保守扩展量；仅放宽候选搜索，精确接触仍由身体胶囊求解。</summary>
        public static float GetBodyQueryExpansion(CharacterController source)
        {
            if (source == null) return 0f;
            Body own = Snapshot(source, -1);
            float expansion = own.forwardClearance;
            var physicsScene = source.gameObject.scene.GetPhysicsScene();
            for (int i = 0; i < Actors.Count; i++)
            {
                var actor = Actors[i]; var other = actor != null ? actor.Motor : null;
                if (other == null || other == source || !other.enabled || !other.detectCollisions
                    || !other.gameObject.activeInHierarchy || other.gameObject.scene.GetPhysicsScene() != physicsScene) continue;
                Body candidate = Snapshot(other, i);
                expansion = Mathf.Max(expansion, Extension(own, candidate).magnitude + Extension(candidate, own).magnitude);
            }
            return expansion;
        }

        static bool VerticalOverlap(Body own, Body other, float vertical)
        {
            return Mathf.Min(own.bottom, own.bottom + vertical) < other.top - Epsilon
                && Mathf.Max(own.top, own.top + vertical) > other.bottom + Epsilon;
        }

        Vector2 Separate(float vertical)
        {
            Body own = Snapshot(controller, ownIndex);
            Vector2 origin = own.center, position = origin;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                bool separated = false;
                for (int i = 0; i < bodies.Count; i++)
                {
                    Body other = bodies[i];
                    if (other.controller == null || !other.controller.enabled || !VerticalOverlap(own, other, vertical)) continue;
                    float radius = own.radius + other.radius;
                    Vector2 ownExtension = Extension(own, other), otherExtension = Extension(other, own);
                    bool extended = ownExtension.sqrMagnitude > Epsilon * Epsilon || otherExtension.sqrMagnitude > Epsilon * Epsilon;
                    Vector2 delta = extended ? SegmentDelta(position, ownExtension, other.center, otherExtension) : position - other.center;
                    float distance = delta.magnitude;
                    if (distance >= radius - Epsilon) continue;
                    Vector2 normal = extended ? ContactNormal(delta, position - other.center, ownIndex, other.index)
                        : distance > Epsilon ? delta / distance
                        : new Vector2(ownIndex < other.index ? -1f : 1f, 0f);
                    position += normal * (radius - distance + Epsilon);
                    separated = true;
                }
                if (!separated) break;
            }
            return position - origin;
        }

        CollisionFlags Slide(Vector2 requested, float vertical)
        {
            Vector2 remaining = requested;
            CollisionFlags flags = CollisionFlags.None;
            for (int iteration = 0; iteration < 5 && remaining.sqrMagnitude > Epsilon * Epsilon; iteration++)
            {
                Body own = Snapshot(controller, ownIndex);
                Vector2 position = own.center;
                float first = 1f; Vector2 normal = Vector2.zero; bool hit = false;
                for (int i = 0; i < bodies.Count; i++)
                {
                    Body other = bodies[i];
                    if (other.controller == null || !other.controller.enabled || !VerticalOverlap(own, other, vertical)) continue;
                    float radius = own.radius + other.radius;
                    Vector2 ownExtension = Extension(own, other), otherExtension = Extension(other, own);
                    if (ownExtension.sqrMagnitude > Epsilon * Epsilon || otherExtension.sqrMagnitude > Epsilon * Epsilon)
                    {
                        float extendedTime; Vector2 extendedNormal;
                        if (SweepExtended(own, position, other, ownExtension, otherExtension, remaining,
                            radius, first, out extendedTime, out extendedNormal))
                        { first = Mathf.Max(0f, extendedTime); normal = extendedNormal; hit = true; }
                        continue;
                    }
                    Vector2 delta = position - other.center;
                    float distance = delta.magnitude;
                    Vector2 away = distance > Epsilon ? delta / distance
                        : new Vector2(ownIndex < other.index ? -1f : 1f, 0f);
                    float inward = Vector2.Dot(remaining, away);
                    if (inward >= -Epsilon) continue;
                    float t;
                    if (distance <= radius + Epsilon) t = 0f;
                    else
                    {
                        float a = remaining.sqrMagnitude, b = Vector2.Dot(delta, remaining);
                        float discriminant = b * b - a * (delta.sqrMagnitude - radius * radius);
                        if (discriminant < 0f) continue;
                        t = (-b - Mathf.Sqrt(discriminant)) / a;
                        if (t < 0f || t > first) continue;
                    }
                    if (t <= first)
                    {
                        first = Mathf.Max(0f, t); hit = true;
                        Vector2 contact = delta + remaining * first;
                        normal = contact.sqrMagnitude > Epsilon * Epsilon ? contact.normalized : away;
                    }
                }
                if (!hit)
                { flags |= controller.Move(new Vector3(remaining.x, vertical, remaining.y)); vertical = 0f; remaining = Vector2.zero; break; }
                Vector2 segment = remaining * first;
                flags |= controller.Move(new Vector3(segment.x, vertical, segment.y)); vertical = 0f;
                remaining *= 1f - first;
                float into = Vector2.Dot(remaining, normal);
                if (into < 0f) remaining -= normal * into;
            }
            if (vertical != 0f) flags |= controller.Move(Vector3.up * vertical);
            return flags;
        }

        static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
