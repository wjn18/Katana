using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 攻击位移的胶囊推动。先让敌方胶囊经自己的Motor移动，再按实际让出的空间推进攻击者。
    /// 不禁用碰撞、不改Transform；按有界推人链先让前方角色让位，墙仍由真实碰撞阻挡。
    /// </summary>
    public sealed class SamuraiAttackPush
    {
        const float Epsilon = .00001f;
        const float BodyGap = .012f;
        const float CastInset = .004f;
        const float MaxStep = .04f;
        const float QueryPadding = .2f;
        const int MaxChain = 8;
        readonly SamuraiCombatant owner;
        readonly SamuraiCharacterMotor movement;
        readonly RaycastHit[] hits = new RaycastHit[64];
        readonly Collider[] overlaps = new Collider[64];
        readonly SamuraiCombatant[] chain = new SamuraiCombatant[MaxChain];
        readonly List<SamuraiCombatant> knownEnemies = new List<SamuraiCombatant>(8);

        public float LastPushedDistance { get; private set; }
        public SamuraiCombatant LastPushTarget { get; private set; }
        public float LastAllowedDistance { get; private set; }

        public SamuraiAttackPush(SamuraiCombatant owner, SamuraiCharacterMotor movement)
        {
            this.owner = owner;
            this.movement = movement;
        }

        public void Reset()
        {
            LastPushedDistance = LastAllowedDistance = 0f;
            LastPushTarget = null;
        }

        /// <summary>考虑自己前方及推人链前端的固定障碍；可推动的敌人不计为刹车点。</summary>
        public float ObstacleDistance(Vector3 direction, float range)
        {
            var controller = movement.Controller;
            if (controller == null || !controller.enabled) return 0f;
            Physics.SyncTransforms();
            return ScanObstacle(owner, controller, direction, range, 0);
        }

        float ScanObstacle(SamuraiCombatant actor, CharacterController controller,
            Vector3 direction, float range, int depth)
        {
            chain[depth] = actor;
            float distance = BlockingDistance(controller, direction, range, depth);
            var enemy = FindEnemy(controller, direction, range, depth);
            if (enemy != null)
            {
                float contact = ContactDistance(controller, enemy.Motor, direction);
                if (depth + 1 < MaxChain)
                    distance = Mathf.Min(distance, contact + ScanObstacle(enemy, enemy.Motor,
                        direction, Mathf.Max(0f, range - contact), depth + 1));
            }
            return distance;
        }

        public void Move(Vector3 direction, float distance, float dt)
        {
            Reset();
            if (dt <= 0f) return;
            if (distance <= Epsilon)
            {
                movement.Move(Vector3.zero, dt);
                return;
            }
            // 小步让长帧也先推人再推进自己；被墙裁掉的预算不会在后续补发。
            int steps = Mathf.Clamp(Mathf.CeilToInt(distance / MaxStep), 1, 128);
            float stepDistance = distance / steps;
            float stepTime = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float allowed = ResolveAdvance(owner, movement.Controller, direction, stepDistance, 0);
                LastAllowedDistance += allowed;
                movement.Move(direction * (allowed / stepTime), stepTime);
                // 同一帧可能推进多个小步；查询代理必须跟上刚移动的胶囊，不能等下一次物理帧。
                Physics.SyncTransforms();
            }
        }

        float ResolveAdvance(SamuraiCombatant actor, CharacterController controller,
            Vector3 direction, float requested, int depth)
        {
            chain[depth] = actor;
            float allowed = Mathf.Min(requested,
                BlockingDistance(controller, direction, requested + BodyGap, depth));
            var enemy = FindEnemy(controller, direction, allowed + BodyGap, depth);
            if (enemy != null)
            {
                float contact = ContactDistance(controller, enemy.Motor, direction);
                float wanted = Mathf.Max(0f, allowed - contact);
                if (wanted > Epsilon)
                {
                    float permitted = depth + 1 < MaxChain
                        ? ResolveAdvance(enemy, enemy.Motor, direction, wanted, depth + 1) : 0f;
                    Vector3 before = enemy.transform.position;
                    enemy.Movement.DisplacePlanar(direction * permitted);
                    Physics.SyncTransforms();
                    float actual = Mathf.Max(0f, Vector3.Dot(enemy.transform.position - before, direction));
                    LastPushedDistance += actual;
                    if (actual > Epsilon) LastPushTarget = enemy;
                    allowed = Mathf.Min(allowed, contact + actual);
                }
            }
            return Mathf.Max(0f, allowed);
        }

        SamuraiCombatant FindEnemy(CharacterController controller, Vector3 direction, float range, int depth)
        {
            if (owner == null || controller == null || !controller.enabled) return null;
            Vector3 lower, upper; float radius;
            Capsule(controller, out lower, out upper, out radius);
            SamuraiCombatant nearest = null;
            float nearestDistance = float.PositiveInfinity;
            // CCT查询代理在同一帧多次Move后可能滞后。保存本招已发现的对象，用实时Transform
            // 重算接触距离；仍重新检查存活、队伍、层碰撞和位于前方，墙体查询也照常执行。
            for (int i = 0; i < knownEnemies.Count; i++)
            {
                var candidate = knownEnemies[i];
                if (candidate != null && candidate.Motor != null && Collides(controller, candidate.Motor))
                    ConsiderActor(candidate, controller, direction, range, depth, ref nearest, ref nearestDistance);
            }
            int count = Physics.CapsuleCastNonAlloc(lower, upper, radius, direction, hits,
                range + QueryPadding, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit[] found = hits;
            if (count == hits.Length)
            {
                found = Physics.CapsuleCastAll(lower, upper, radius, direction,
                    range + QueryPadding, ~0, QueryTriggerInteraction.Ignore);
                count = found.Length;
            }
            for (int i = 0; i < count; i++)
                ConsiderEnemy(found[i].collider, controller, direction, range, depth, ref nearest, ref nearestDistance);
            // CapsuleCast不保证报告起点重叠；角色皮肤容差内也必须能继续推动。
            count = Physics.OverlapCapsuleNonAlloc(lower, upper, radius + QueryPadding,
                overlaps, ~0, QueryTriggerInteraction.Ignore);
            Collider[] touching = overlaps;
            if (count == overlaps.Length)
            {
                touching = Physics.OverlapCapsule(lower, upper, radius + QueryPadding,
                    ~0, QueryTriggerInteraction.Ignore);
                count = touching.Length;
            }
            for (int i = 0; i < count; i++)
                ConsiderEnemy(touching[i], controller, direction, range, depth, ref nearest, ref nearestDistance);
            return nearest;
        }

        void ConsiderEnemy(Collider collider, CharacterController controller, Vector3 direction, float range, int depth,
            ref SamuraiCombatant nearest, ref float nearestDistance)
        {
            if (!Collides(controller, collider)) return;
            var candidate = collider.GetComponentInParent<SamuraiCombatant>();
            if (!Pushable(candidate)) return;
            if (!knownEnemies.Contains(candidate)) knownEnemies.Add(candidate);
            ConsiderActor(candidate, controller, direction, range, depth, ref nearest, ref nearestDistance);
        }

        void ConsiderActor(SamuraiCombatant candidate, CharacterController controller, Vector3 direction,
            float range, int depth, ref SamuraiCombatant nearest, ref float nearestDistance)
        {
            if (!Pushable(candidate) || InChain(candidate, depth)) return;
            float distance = ContactDistance(controller, candidate.Motor, direction);
            if (distance > range + Epsilon || distance >= nearestDistance) return;
            nearest = candidate;
            nearestDistance = distance;
        }

        float BlockingDistance(CharacterController controller, Vector3 direction, float range, int depth)
        {
            if (controller == null || !controller.enabled) return 0f;
            Vector3 lower, upper; float radius;
            Capsule(controller, out lower, out upper, out radius);
            int count = Physics.CapsuleCastNonAlloc(lower, upper, radius, direction, hits,
                range + BodyGap, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit[] found = hits;
            if (count == hits.Length)
            {
                found = Physics.CapsuleCastAll(lower, upper, radius, direction,
                    range + BodyGap, ~0, QueryTriggerInteraction.Ignore);
                count = found.Length;
            }
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = found[i];
                if (!Collides(controller, hit.collider) || hit.normal.y > .65f) continue;
                var combatant = hit.collider.GetComponentInParent<SamuraiCombatant>();
                if (combatant != null && (InChain(combatant, depth) || Pushable(combatant))) continue;
                // Cast略缩小以避开地面接缝，距离补回缩小量并保留很小的物理间隙。
                nearest = Mathf.Min(nearest, Mathf.Max(0f, hit.distance - CastInset - BodyGap));
            }
            return nearest;
        }

        bool Pushable(SamuraiCombatant candidate)
        {
            return candidate != null && owner != null && candidate != owner && candidate.Team != owner.Team &&
                candidate.isActiveAndEnabled && candidate.CanReceiveDamage && candidate.Movement != null &&
                candidate.Motor != null && candidate.Motor.enabled && candidate.gameObject.activeInHierarchy;
        }

        bool InChain(SamuraiCombatant candidate, int depth)
        {
            for (int i = 0; i <= depth; i++)
                if (chain[i] == candidate) return true;
            return false;
        }

        static bool Collides(CharacterController controller, Collider collider)
        {
            return collider != null && collider != controller && collider.enabled && !collider.isTrigger &&
                !collider.transform.IsChildOf(controller.transform) &&
                !Physics.GetIgnoreLayerCollision(controller.gameObject.layer, collider.gameObject.layer) &&
                !Physics.GetIgnoreCollision(controller, collider);
        }

        static float ContactDistance(CharacterController first, CharacterController second, Vector3 direction)
        {
            if (first.bounds.min.y > second.bounds.max.y || second.bounds.min.y > first.bounds.max.y)
                return float.PositiveInfinity;
            Vector3 delta = second.transform.TransformPoint(second.center) - first.transform.TransformPoint(first.center);
            delta.y = 0f;
            float along = Vector3.Dot(delta, direction);
            if (along <= 0f) return float.PositiveInfinity;
            // CC会在皮肤宽度内提前形成接触；只按radius计算会在高帧率小步时停在推动阈值外。
            float combined = Radius(first) + Radius(second) + Skin(first) + Skin(second) + BodyGap;
            float lateralSquare = Mathf.Max(0f, delta.sqrMagnitude - along * along);
            if (lateralSquare >= combined * combined) return float.PositiveInfinity;
            return Mathf.Max(0f, along - Mathf.Sqrt(combined * combined - lateralSquare));
        }

        static float Radius(CharacterController controller)
        {
            Vector3 scale = controller.transform.lossyScale;
            return controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        static float Skin(CharacterController controller)
        {
            Vector3 scale = controller.transform.lossyScale;
            return controller.skinWidth * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        static void Capsule(CharacterController controller, out Vector3 lower, out Vector3 upper, out float radius)
        {
            Vector3 center = controller.transform.TransformPoint(controller.center);
            float fullRadius = Radius(controller);
            float halfHeight = Mathf.Max(fullRadius, controller.height * Mathf.Abs(controller.transform.lossyScale.y) * .5f);
            float stem = Mathf.Max(0f, halfHeight - fullRadius);
            lower = center - Vector3.up * stem;
            upper = center + Vector3.up * stem;
            // Controller允许skinWidth地面渗入；抬高探针底端，避免平地被误认作前方墙。
            lower.y = Mathf.Min(upper.y, lower.y + controller.skinWidth * Mathf.Abs(controller.transform.lossyScale.y) + .01f);
            radius = Mathf.Max(.01f, fullRadius - CastInset);
        }
    }
}
