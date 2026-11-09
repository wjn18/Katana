using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 仅生成巡逻输入；由 SamuraiTrainingEnemy 每帧仲裁并且只调用一次角色 Tick。
    /// 返程沿本次交战实际经过的位置逆序步行，不改变 Transform 或角色出生点。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("OC Design/Samurai/Patrol Agent")]
    public sealed class SamuraiPatrolAgent : MonoBehaviour
    {
        public bool patrolEnabled = true;
        public SamuraiPatrolRoute route;
        [Min(0)] public int startPoint;
        [Range(.05f, 1f)] public float speedMultiplier = .45f;
        [Min(.04f)] public float arrivalDistance = .18f;

        const int MaximumBreadcrumbs = 256;
        const float BreadcrumbSpacing = .4f;
        const float ReturnArrivalDistance = .08f;
        const float VerticalArrivalTolerance = .35f;
        readonly List<Vector3> breadcrumbs = new List<Vector3>(MaximumBreadcrumbs);
        readonly SamuraiEnemySensors sensors = new SamuraiEnemySensors();
        SamuraiCombatant subscribedActor;
        SamuraiPatrolRoute initializedRoute;
        int initializedPointCount = -1;
        int currentPointIndex = -1;
        int direction = 1;
        int returnIndex = -1;
        int initializedStartPoint;
        float waitRemaining;
        bool initialized;
        bool engaged;
        bool returning;
        bool waiting;
        bool traceOverflowed;
        bool hasPreviousCombatPosition;
        Vector3 previousCombatPosition;
        Vector3 previousCombatDirection;
        string status = "尚未开始";

        public string Status => status;
        public int CurrentPointIndex => currentPointIndex;
        public bool IsReturning => returning;
        public bool IsEngaged => engaged;
        public bool IsWaiting => waiting;
        public float WaitRemaining => waitRemaining;
        public int BreadcrumbCount => breadcrumbs.Count;
        public bool ReturnTraceOverflowed => traceOverflowed;

        void OnEnable()
        {
            BindActor(GetComponent<SamuraiCombatant>());
            ResetPatrol();
        }

        void OnDisable()
        {
            BindActor(null);
            ResetPatrol();
            status = "组件已停用";
        }

        void BindActor(SamuraiCombatant actor)
        {
            if (subscribedActor == actor) return;
            if (subscribedActor != null) subscribedActor.CombatReset -= ResetPatrol;
            subscribedActor = actor;
            if (subscribedActor != null) subscribedActor.CombatReset += ResetPatrol;
        }

        public void ResetPatrol()
        {
            initialized = engaged = returning = waiting = traceOverflowed = false;
            initializedRoute = null;
            initializedPointCount = -1;
            currentPointIndex = -1;
            direction = 1;
            returnIndex = -1;
            waitRemaining = 0f;
            breadcrumbs.Clear();
            hasPreviousCombatPosition = false;
            previousCombatDirection = Vector3.zero;
            status = "等待巡逻";
        }

        /// <summary>战斗期间也逐帧调用，以保存追击轨迹；不是第二个角色驱动器。</summary>
        public void SetEngaged(SamuraiCombatant actor, bool isEngaged)
        {
            BindActor(actor);
            if (!CanConfigure(actor))
            {
                if (engaged || returning) ResetPatrol();
                status = !patrolEnabled ? "巡逻已关闭" : "未配置有效路线";
                return;
            }
            EnsureInitialized();
            if (isEngaged)
            {
                if (!engaged)
                {
                    // 返程中再次发现玩家时保留原离线点和未走完的返程链。
                    if (returning && returnIndex >= 0 && returnIndex < breadcrumbs.Count)
                    {
                        int firstConsumed = returnIndex + 1;
                        if (firstConsumed < breadcrumbs.Count)
                            breadcrumbs.RemoveRange(firstConsumed, breadcrumbs.Count - firstConsumed);
                    }
                    else
                    {
                        breadcrumbs.Clear();
                        traceOverflowed = false;
                    }
                    engaged = true;
                    returning = false;
                    hasPreviousCombatPosition = false;
                    previousCombatDirection = Vector3.zero;
                    RecordCombatPosition(actor.transform.position, true);
                }
                else RecordCombatPosition(actor.transform.position, false);
                status = traceOverflowed ? "交战中（返程轨迹容量已满）" : "交战中";
                return;
            }
            if (!engaged) return;
            // 目标超距并不取消已经提交的攻击、闪避或受击位移。
            // 等正式动作结束再封存轨迹，避免遗漏脱战后的最后一段真实移动。
            if (!actor.IsDead && (!actor.CanAct || actor.IsBeingKnockedBack || actor.IsAttacking
                || actor.IsCharging || actor.IsDodging || actor.IsInHitReaction || actor.IsHitStopped))
            {
                RecordCombatPosition(actor.transform.position, false);
                status = "动作恢复中，记录返程轨迹";
                return;
            }
            RecordCombatPosition(actor.transform.position, true);
            engaged = false;
            returning = breadcrumbs.Count > 0;
            returnIndex = breadcrumbs.Count - 1;
            status = traceOverflowed ? "追击轨迹已满，返程暂停" : "沿追击轨迹返回";
        }

        public bool TryBuildFrame(SamuraiCombatant actor, float dt, out SamuraiInputFrame frame)
        {
            frame = new SamuraiInputFrame();
            BindActor(actor);
            if (!CanConfigure(actor))
            {
                status = !patrolEnabled || !isActiveAndEnabled ? "巡逻已关闭" : "未配置有效路线";
                return false;
            }
            EnsureInitialized();
            if (engaged) { status = "交战中"; return false; }
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt) || Time.timeScale <= 0f)
            { status = "巡逻暂停"; return true; }
            if (actor.IsDead) { status = "已死亡"; return true; }
            if (!actor.CanAct || actor.IsHitStopped || actor.IsBeingKnockedBack || actor.IsDeflected
                || actor.IsToughnessBroken || actor.IsExecuting || actor.IsBeingExecuted
                || actor.IsAttacking || actor.IsCharging || actor.IsDodging || actor.IsInHitReaction)
            { status = "动作恢复中"; return true; }
            if (!actor.Motor.isGrounded) { status = "等待接地"; return true; }
            dt = Mathf.Min(dt, .1f);
            if (traceOverflowed) { status = "追击轨迹已满，返程暂停"; return true; }
            if (returning) return BuildReturnFrame(actor, dt, ref frame);

            if (waiting)
            {
                SetPointFacing(ref frame);
                waitRemaining = Mathf.Max(0f, waitRemaining - dt);
                status = waitRemaining > 0f ? "路点停留" : "路点待命";
                if (waitRemaining <= 0f && route.points.Count > 1)
                {
                    AdvancePoint();
                    waiting = false;
                }
                return true;
            }

            Vector3 destination = route.GetWorldPosition(currentPointIndex);
            if (HasArrived(actor.transform.position, destination, Mathf.Max(.04f, arrivalDistance)))
            {
                waiting = true;
                waitRemaining = Mathf.Max(0f, route.points[currentPointIndex].waitSeconds);
                SetPointFacing(ref frame);
                status = waitRemaining > 0f ? "路点停留" : "到达路点";
                return true;
            }
            BuildMoveFrame(actor, destination, dt, ref frame, "巡逻中");
            return true;
        }

        bool CanConfigure(SamuraiCombatant actor)
        {
            return isActiveAndEnabled && patrolEnabled && actor != null && actor.isActiveAndEnabled
                && actor.Profile != null && actor.Motor != null && actor.Motor.enabled
                && route != null && route.gameObject.scene == gameObject.scene
                && !route.transform.IsChildOf(actor.transform)
                && route.points != null && route.points.Count > 0 && HasValidPoints();
        }

        bool HasValidPoints()
        {
            for (int i = 0; i < route.points.Count; i++)
            {
                var point = route.points[i];
                if (point == null || !IsFinite(point.localPosition) || float.IsNaN(point.waitSeconds)
                    || float.IsInfinity(point.waitSeconds) || float.IsNaN(point.localYaw)
                    || float.IsInfinity(point.localYaw)) return false;
            }
            return true;
        }

        void EnsureInitialized()
        {
            if (initialized && initializedRoute == route && initializedPointCount == route.points.Count
                && initializedStartPoint == startPoint) return;
            ResetPatrol();
            initialized = true;
            initializedRoute = route;
            initializedPointCount = route.points.Count;
            initializedStartPoint = startPoint;
            currentPointIndex = Mathf.Clamp(startPoint, 0, route.points.Count - 1);
            // 从最后一点开始往返时，先沿路径反向走。
            direction = currentPointIndex == route.points.Count - 1 ? -1 : 1;
        }

        bool BuildReturnFrame(SamuraiCombatant actor, float dt, ref SamuraiInputFrame frame)
        {
            while (returnIndex >= 0 && HasArrived(actor.transform.position, breadcrumbs[returnIndex], ReturnArrivalDistance))
                returnIndex--;
            if (returnIndex < 0)
            {
                returning = false;
                breadcrumbs.Clear();
                hasPreviousCombatPosition = false;
                previousCombatDirection = Vector3.zero;
                status = "已返回巡逻路线";
                return true;
            }
            BuildMoveFrame(actor, breadcrumbs[returnIndex], dt, ref frame, "沿追击轨迹返回");
            return true;
        }

        void BuildMoveFrame(SamuraiCombatant actor, Vector3 destination, float dt, ref SamuraiInputFrame frame, string movingStatus)
        {
            Vector3 delta = destination - actor.transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < .001f)
            {
                status = "目标路点高度不匹配";
                return;
            }
            Vector3 forward = delta / distance;
            frame.facingWorld = forward;
            float speed = Mathf.Max(.01f, actor.Profile.walkSpeed);
            float amount = Mathf.Min(Mathf.Clamp(speedMultiplier, .05f, 1f), distance / (speed * dt));
            if (!sensors.HasClearPath(actor, forward, amount, Mathf.Max(.35f, speed * dt + .2f), actor.Profile.obstructionMask))
            {
                status = "路径受阻，等待通行";
                return;
            }
            frame.moveWorld = forward * amount;
            status = movingStatus;
        }

        void SetPointFacing(ref SamuraiInputFrame frame)
        {
            var point = route.points[currentPointIndex];
            if (point.useFacing) frame.facingWorld = route.GetWorldFacing(currentPointIndex);
        }

        void AdvancePoint()
        {
            int count = route.points.Count;
            if (count < 2) return;
            if (route.mode == SamuraiPatrolMode.Loop) currentPointIndex = (currentPointIndex + 1) % count;
            else
            {
                if (currentPointIndex + direction < 0 || currentPointIndex + direction >= count) direction = -direction;
                currentPointIndex += direction;
            }
        }

        void RecordCombatPosition(Vector3 position, bool force)
        {
            if (!IsFinite(position) || traceOverflowed) return;
            if (hasPreviousCombatPosition)
            {
                Vector3 change = position - previousCombatPosition;
                Vector3 flatChange = change; flatChange.y = 0f;
                // 方向改变时保留转角前的实际位置，避免返程抄近道跨过墙角。
                if (flatChange.sqrMagnitude > .000001f && previousCombatDirection.sqrMagnitude > .000001f
                    && Vector3.Angle(flatChange, previousCombatDirection) > 20f)
                    AppendBreadcrumb(previousCombatPosition);
                if (flatChange.sqrMagnitude > .000001f) previousCombatDirection = flatChange.normalized;
            }
            if (breadcrumbs.Count == 0 || force || (position - breadcrumbs[breadcrumbs.Count - 1]).sqrMagnitude >= BreadcrumbSpacing * BreadcrumbSpacing
                || Mathf.Abs(position.y - breadcrumbs[breadcrumbs.Count - 1].y) >= .15f)
                AppendBreadcrumb(position);
            previousCombatPosition = position;
            hasPreviousCombatPosition = true;
        }

        void AppendBreadcrumb(Vector3 position)
        {
            if (breadcrumbs.Count > 0 && (breadcrumbs[breadcrumbs.Count - 1] - position).sqrMagnitude < .0001f) return;
            if (breadcrumbs.Count >= MaximumBreadcrumbs)
            {
                // 不裁断离线路点，也不生成跨过未记录路段的直线返程。
                traceOverflowed = true;
                return;
            }
            breadcrumbs.Add(position);
        }

        static bool HasArrived(Vector3 position, Vector3 target, float radius)
        {
            Vector3 delta = position - target;
            return Mathf.Abs(delta.y) <= VerticalArrivalTolerance
                && delta.x * delta.x + delta.z * delta.z <= radius * radius;
        }

        static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
                && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }
    }
}
