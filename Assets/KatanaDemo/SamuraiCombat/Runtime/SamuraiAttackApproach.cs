using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 单次攻击的接近限速服务。原StepFraction提供位移预算，削去的位移不会积累补发；
    /// 方向和目标只在出招时选取，动画时序、根运动和普通跨步不受影响。
    /// </summary>
    public sealed class SamuraiAttackApproach
    {
        const float ContactGap = .06f;
        const float Epsilon = .00001f;
        readonly Transform root;
        readonly SamuraiCharacterMotor movement;
        readonly SamuraiCombatProfile profile;
        readonly Collider[] candidates = new Collider[64];
        readonly RaycastHit[] visibilityHits = new RaycastHit[32];
        SamuraiActionDefinition action;
        SamuraiCombatant owner, target;
        SamuraiAttackPush push;
        Renderer[] targetRenderers;
        Vector3 direction;
        int serial;
        bool hadTarget;

        public bool IsActive { get; private set; }
        public bool IsSlowed { get; private set; }
        public string BrakeReason { get; private set; } = string.Empty;
        public SamuraiCombatant Target => target;
        public Vector3 Direction => direction;
        /// <summary>上一移动帧的实际水平平均速度；跨阈值帧包含快速段和慢速段。</summary>
        public float LastSpeed { get; private set; }
        public float LastRequestedDistance { get; private set; }
        public float LastAllowedDistance { get; private set; }
        public float LastPushedDistance => push != null ? push.LastPushedDistance : 0f;
        public SamuraiCombatant LastPushTarget => push != null ? push.LastPushTarget : null;

        public SamuraiAttackApproach(Transform root, SamuraiCharacterMotor movement, SamuraiCombatProfile profile)
        {
            this.root = root;
            this.movement = movement;
            this.profile = profile;
        }

        public void Reset()
        {
            action = null; target = null; targetRenderers = null; hadTarget = false;
            direction = Vector3.zero; serial = 0;
            IsActive = IsSlowed = false;
            BrakeReason = string.Empty;
            LastSpeed = LastRequestedDistance = LastAllowedDistance = 0f;
            if (push != null) push.Reset();
        }

        public void Begin(SamuraiActionDefinition next, Vector3 lockedDirection, int attackSerial)
        {
            Reset();
            if (next == null || !next.approachEnabled || root == null) return;
            action = next; serial = attackSerial; IsActive = true;
            lockedDirection.y = 0f;
            direction = lockedDirection.sqrMagnitude > Epsilon ? lockedDirection.normalized : root.forward;
            owner = root.GetComponent<SamuraiCombatant>();
            if (next.pushEnemies) push = new SamuraiAttackPush(owner, movement);
            float range = Mathf.Clamp(Mathf.Max(0f, next.stepDistance) + StopGap + 1.5f, .5f, 20f);
            var targetLock = root.GetComponent<SamuraiTargetLock>();
            if (targetLock != null && Eligible(targetLock.CurrentTarget, range))
                target = targetLock.CurrentTarget;
            else
                target = AcquireForwardTarget(range);
            hadTarget = target != null;
            if (hadTarget)
            {
                targetRenderers = target.GetComponentsInChildren<Renderer>(true);
                if (!next.pushEnemies && DistanceToTarget(StopGap) <= Epsilon) Brake("target-distance");
            }
        }

        public void NotifyContact(int attackSerial, SamuraiHitResult result)
        {
            if (!IsActive || attackSerial != serial || result == SamuraiHitResult.Ignored || result == SamuraiHitResult.Dodged) return;
            if (action != null && action.pushEnemies) return;
            Brake("contact");
        }

        public void MoveAction(SamuraiActionDefinition current, Vector3 originalDirection,
            float previousNormalized, float currentNormalized, float dt)
        {
            LastSpeed = LastRequestedDistance = LastAllowedDistance = 0f;
            if (push != null) push.Reset();
            if (current == null || !current.approachEnabled)
            {
                movement.MoveAction(current, originalDirection, previousNormalized, currentNormalized, dt);
                return;
            }
            // 失焦/禁用取消输入后仍可播完动作，但取消的接近位移不能落回普通全速跨步。
            if (!IsActive || action != current || dt <= 0f)
            {
                movement.Move(Vector3.zero, dt);
                return;
            }
            if (hadTarget && !TargetIsAvailable())
            {
                target = null; targetRenderers = null; hadTarget = false;
                if (!current.pushEnemies) Brake("target-lost");
            }

            float before = Mathf.Clamp01(previousNormalized), after = Mathf.Clamp01(currentNormalized);
            float requested = Mathf.Max(0f, current.stepDistance * (current.StepFraction(after) - current.StepFraction(before)));
            LastRequestedDistance = requested;
            if (current.pushEnemies && push != null)
            {
                MovePushing(before, after, requested, dt);
                return;
            }
            float allowed = requested;
            float activeTime = ActiveStepSeconds(before, after, dt);
            float slowSpeed = Mathf.Max(0f, current.approachSlowSpeed);
            float boundary = DistanceToTarget(StopGap);
            if (!IsSlowed && boundary <= requested + Epsilon)
            {
                float fastDistance = Mathf.Clamp(boundary, 0f, requested);
                float remainingTime = SecondsAfterDistance(before, after, fastDistance, dt);
                allowed = fastDistance + Mathf.Min(requested - fastDistance, slowSpeed * remainingTime);
                Brake("target-distance");
            }
            else if (IsSlowed)
                allowed = Mathf.Min(requested, slowSpeed * activeTime);

            // 快速转慢速阈值之外另保留硬间隙，慢速不能顶穿角色，也不会倒退修正位置。
            allowed = Mathf.Min(allowed, DistanceToTarget(ContactGap));
            allowed = Mathf.Max(0f, allowed);
            LastAllowedDistance = allowed;
            Vector3 positionBefore = root.position;
            movement.Move(direction * (allowed / dt), dt);
            Vector3 actual = root.position - positionBefore; actual.y = 0f;
            LastSpeed = actual.magnitude / dt;
            var controller = movement.Controller;
            if (allowed > Epsilon && controller != null &&
                (((controller.collisionFlags & CollisionFlags.Sides) != 0) ||
                 Vector3.Dot(actual, direction) + .001f < allowed))
                Brake("obstacle");
        }

        void MovePushing(float before, float after, float requested, float dt)
        {
            // 命中/敌人距离不会刹车；预判玩家前方和被推敌人背后的墙，才使用原减速参数。
            float boundary = Mathf.Max(0f, push.ObstacleDistance(direction, requested + StopGap) - StopGap);
            float allowed = requested;
            float slowSpeed = Mathf.Max(0f, action.approachSlowSpeed);
            if (!IsSlowed && boundary <= requested + Epsilon)
            {
                float fastDistance = Mathf.Clamp(boundary, 0f, requested);
                float remainingTime = SecondsAfterDistance(before, after, fastDistance, dt);
                allowed = fastDistance + Mathf.Min(requested - fastDistance, slowSpeed * remainingTime);
                Brake("obstacle");
            }
            else if (IsSlowed)
                allowed = Mathf.Min(requested, slowSpeed * ActiveStepSeconds(before, after, dt));
            Vector3 positionBefore = root.position;
            push.Move(direction, allowed, dt);
            LastAllowedDistance = push.LastAllowedDistance;
            Vector3 actual = root.position - positionBefore; actual.y = 0f;
            LastSpeed = actual.magnitude / dt;
        }

        float StopGap => Mathf.Max(ContactGap, action != null ? action.approachStopGap : ContactGap);

        void Brake(string reason)
        {
            if (IsSlowed) return;
            IsSlowed = true; BrakeReason = reason;
        }

        float ActiveStepSeconds(float before, float after, float dt)
        {
            float span = after - before;
            if (span <= Epsilon) return 0f;
            float start = Mathf.Clamp01(action.stepStart);
            float end = Mathf.Clamp(action.stepEnd, start + .001f, 1.001f);
            return dt * Mathf.Clamp01((Mathf.Min(after, end) - Mathf.Max(before, start)) / span);
        }

        float SecondsAfterDistance(float before, float after, float distance, float dt)
        {
            if (distance <= Epsilon) return ActiveStepSeconds(before, after, dt);
            if (action.stepDistance <= Epsilon || after - before <= Epsilon) return 0f;
            float desired = action.StepFraction(before) + distance / action.stepDistance;
            float low = before, high = after;
            // 反解SmoothStep的跨阈值相位，长帧中只给越界后的时间慢速预算。
            for (int i = 0; i < 24; i++)
            {
                float middle = (low + high) * .5f;
                if (action.StepFraction(middle) < desired) low = middle;
                else high = middle;
            }
            return ActiveStepSeconds(high, after, dt * (after - high) / (after - before));
        }

        float DistanceToTarget(float gap)
        {
            if (target == null || movement.Controller == null || target.Motor == null ||
                !VerticalOverlap(movement.Controller, target.Motor)) return float.PositiveInfinity;
            Vector3 delta = CapsuleCenter(target.Motor) - CapsuleCenter(movement.Controller);
            delta.y = 0f;
            float radius = CapsuleRadius(movement.Controller) + CapsuleRadius(target.Motor) + gap;
            float squareDistance = delta.sqrMagnitude;
            if (squareDistance <= radius * radius + Epsilon) return 0f;
            float along = Vector3.Dot(delta, direction);
            if (along <= 0f) return float.PositiveInfinity;
            float lateralSquare = Mathf.Max(0f, squareDistance - along * along);
            if (lateralSquare >= radius * radius) return float.PositiveInfinity;
            return Mathf.Max(0f, along - Mathf.Sqrt(radius * radius - lateralSquare));
        }

        SamuraiCombatant AcquireForwardTarget(float range)
        {
            if (owner == null || profile == null || movement.Controller == null) return null;
            Vector3 center = CapsuleCenter(movement.Controller);
            int count = Physics.OverlapSphereNonAlloc(center, range, candidates, profile.hitMask, QueryTriggerInteraction.Ignore);
            Collider[] found = candidates;
            if (count == candidates.Length)
            {
                found = Physics.OverlapSphere(center, range, profile.hitMask, QueryTriggerInteraction.Ignore);
                count = found.Length;
            }
            SamuraiCombatant nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (found[i] == null) continue;
                var candidate = found[i].GetComponentInParent<SamuraiCombatant>();
                if (!Eligible(candidate, range)) continue;
                Vector3 delta = CapsuleCenter(candidate.Motor) - center; delta.y = 0f;
                float distance = delta.magnitude;
                if (distance >= nearestDistance || (distance > Epsilon && Vector3.Dot(delta / distance, direction) < .70710678f)
                    || !HasLineOfSight(candidate)) continue;
                nearest = candidate; nearestDistance = distance;
            }
            return nearest;
        }

        bool Eligible(SamuraiCombatant candidate, float range)
        {
            return candidate != null && owner != null && candidate != owner && candidate.Team != owner.Team &&
                candidate.isActiveAndEnabled && candidate.gameObject.activeInHierarchy && candidate.CanReceiveDamage &&
                candidate.Motor != null && candidate.Motor.enabled && movement.Controller != null &&
                VerticalOverlap(movement.Controller, candidate.Motor) &&
                (CapsuleCenter(candidate.Motor) - CapsuleCenter(movement.Controller)).sqrMagnitude <= range * range && IsShown(candidate);
        }

        bool TargetIsAvailable()
        {
            if (target == null || !target.isActiveAndEnabled || !target.gameObject.activeInHierarchy ||
                !target.CanReceiveDamage || target.Motor == null || !target.Motor.enabled) return false;
            if (targetRenderers == null || targetRenderers.Length == 0) return true;
            foreach (var renderer in targetRenderers)
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy) return true;
            return false;
        }

        static bool IsShown(SamuraiCombatant candidate)
        {
            var renderers = candidate.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return true;
            foreach (var renderer in renderers)
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy) return true;
            return false;
        }

        bool HasLineOfSight(SamuraiCombatant candidate)
        {
            Vector3 start = CapsuleCenter(movement.Controller);
            Vector3 delta = CapsuleCenter(candidate.Motor) - start;
            float distance = delta.magnitude;
            if (distance <= Epsilon) return true;
            int count = Physics.RaycastNonAlloc(start, delta / distance, visibilityHits, distance,
                profile.obstructionMask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = visibilityHits;
            if (count == visibilityHits.Length)
            {
                hits = Physics.RaycastAll(start, delta / distance, distance, profile.obstructionMask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
                if (hits[i].collider != null && hits[i].distance < distance - .02f &&
                    hits[i].collider.GetComponentInParent<SamuraiCombatant>() == null) return false;
            return true;
        }

        static Vector3 CapsuleCenter(CharacterController controller) => controller.transform.TransformPoint(controller.center);
        static float CapsuleRadius(CharacterController controller)
        {
            Vector3 scale = controller.transform.lossyScale;
            return controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }
        static bool VerticalOverlap(CharacterController first, CharacterController second)
        {
            Bounds a = first.bounds, b = second.bounds;
            return a.min.y <= b.max.y + .1f && b.min.y <= a.max.y + .1f;
        }
    }
}
