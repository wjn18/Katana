using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 敌人的物理环境查询。只读取身体和碰撞信息，不决定攻击、移动或状态切换；
    /// 新行为策略可复用同一个实例及其无分配查询缓冲区。
    /// </summary>
    public sealed class SamuraiEnemySensors
    {
        private readonly RaycastHit[] hits = new RaycastHit[24];

        public bool IsInsideView(SamuraiCombatant actor, SamuraiCombatant target, float range,
            float horizontalAngle, float verticalAngle)
        {
            if (actor == null || target == null || !target.isActiveAndEnabled) return false;
            Vector3 delta = BodyCentre(target) - BodyCentre(actor);
            Vector3 planar = delta; planar.y = 0f;
            if (planar.sqrMagnitude > Mathf.Max(0f, range) * Mathf.Max(0f, range)) return false;
            Vector3 forward = actor.transform.forward; forward.y = 0f;
            if (planar.sqrMagnitude > .000001f && Vector3.Angle(forward, planar)
                > Mathf.Clamp(horizontalAngle, 0f, 360f) * .5f + .0001f) return false;
            float elevation = Mathf.Abs(Mathf.Atan2(delta.y, planar.magnitude) * Mathf.Rad2Deg);
            return elevation <= Mathf.Clamp(verticalAngle, 0f, 180f) * .5f + .0001f;
        }

        public bool CanSee(SamuraiCombatant actor, SamuraiCombatant target, float range,
            float horizontalAngle, float verticalAngle, LayerMask obstructionMask) =>
            IsInsideView(actor, target, range, horizontalAngle, verticalAngle)
            && HasLineOfSight(actor, target, obstructionMask);

        public bool HasLineOfSight(SamuraiCombatant actor, SamuraiCombatant target, LayerMask obstructionMask)
        {
            if (actor == null || target == null) return false;
            Vector3 start = BodyCentre(actor), end = BodyCentre(target), ray = end - start;
            float distance = ray.magnitude;
            if (distance < 0.01f) return true;
            int count = Physics.RaycastNonAlloc(start, ray / distance, hits, distance,
                obstructionMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var owner = hits[i].collider.GetComponentInParent<SamuraiCombatant>();
                if (owner != actor && owner != target && hits[i].distance < distance - 0.02f) return false;
            }
            return true;
        }

        public bool HasClearPath(SamuraiCombatant actor, Vector3 direction, float amount,
            float lookAhead, LayerMask obstructionMask)
        {
            if (actor == null || direction.sqrMagnitude < 0.001f) return false;
            direction.Normalize();
            var motor = actor.Motor;
            Vector3 centre = BodyCentre(actor), scale = actor.transform.lossyScale;
            float radius = motor != null ? motor.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) : 0.3f;
            float height = motor != null ? motor.height * Mathf.Abs(scale.y) : 1.5f;
            radius = Mathf.Max(0.1f, radius * 0.92f);
            float halfSegment = Mathf.Max(0f, height * 0.5f - radius - 0.04f);
            float probe = Mathf.Max(0.18f, lookAhead * Mathf.Clamp(amount, 0.4f, 1f));
            int count = Physics.CapsuleCastNonAlloc(centre + Vector3.up * halfSegment, centre - Vector3.up * halfSegment,
                radius, direction, hits, probe, obstructionMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.GetComponentInParent<SamuraiCombatant>() == actor) continue;
                if (hits[i].normal.y > 0.65f) continue;
                return false;
            }
            // 落地时检查下一步的落脚面，避免主动后退到台沿外。
            if (motor != null && motor.isGrounded)
            {
                Vector3 foot = centre - Vector3.up * (height * 0.5f);
                count = Physics.RaycastNonAlloc(foot + direction * probe + Vector3.up * 0.5f,
                    Vector3.down, hits, 1.05f, obstructionMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    if (hits[i].collider.GetComponentInParent<SamuraiCombatant>() == null && hits[i].normal.y > 0.55f)
                        return true;
                return false;
            }
            return true;
        }

        private static Vector3 BodyCentre(SamuraiCombatant combatant)
        {
            return combatant.Motor != null ? combatant.transform.TransformPoint(combatant.Motor.center)
                : combatant.transform.position + Vector3.up * 0.8f;
        }
    }
}
