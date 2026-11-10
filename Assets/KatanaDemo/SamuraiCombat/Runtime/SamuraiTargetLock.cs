using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>主动居中锁定、丢失后就近补锁与鼠标方向切换；仅在获取/切换时搜索候选。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-30)]
    public sealed class SamuraiTargetLock : MonoBehaviour
    {
        public SamuraiCombatant actor;
        public Transform viewTransform;
        [Min(1f)] public float acquireDistance = 12f;
        [Min(1f)] public float releaseDistance = 14f;
        [Tooltip("可被锁定角色的碰撞层；角色必须有启用的碰撞体。")]
        public LayerMask targetMask = ~0;
        [Tooltip("获取或切换目标时使用的环境遮挡层；已锁定目标不会因遮挡而解除。角色自身和其他战斗角色不作为环境遮挡。")]
        public LayerMask obstructionMask = ~0;

        [Header("鼠标快速切换目标")]
        [Tooltip("快速移动在短窗口内达到此净位移才切换，单位为鼠标输入像素。")]
        [Min(1f)] public float switchMousePixels = 65f;
        [Min(1f)] public float switchMouseSpeed = 700f;
        [Min(.01f)] public float switchGestureSeconds = .12f;
        [Min(0f)] public float switchCooldownSeconds = .25f;
        [Tooltip("慢移或停止达到此时长后，允许下一次同方向甩动。冷却结束后反向甩动也可重新触发。")]
        [Min(.01f)] public float switchRearmSeconds = .06f;
        [Range(1f, 89f)] public float switchDirectionHalfAngle = 70f;

        private readonly Collider[] candidates = new Collider[64];
        private readonly RaycastHit[] obstructionHits = new RaycastHit[32];
        private readonly HashSet<SamuraiCombatant> visited = new HashSet<SamuraiCombatant>();
        private SamuraiCombatant currentTarget;
        private Renderer[] targetRenderers = Array.Empty<Renderer>();
        private Camera viewCamera;
        private Vector2 switchMotion, lastSwitchDirection;
        private float switchMotionTime, switchCooldown, switchQuietTime;
        private bool switchArmed = true;
        private enum SearchMode { Centered, Nearest, Directional }

        public SamuraiCombatant CurrentTarget => currentTarget;
        public bool IsLocked => currentTarget != null;
        public Vector3 TargetPoint => currentTarget != null ? AimPoint(currentTarget) : Vector3.zero;
        public event Action<SamuraiCombatant> TargetChanged;

        public void Configure(SamuraiCombatant owner, Transform view)
        {
            ClearLock();
            actor = owner;
            viewTransform = view;
            viewCamera = view != null ? view.GetComponent<Camera>() : null;
        }

        private void Awake()
        {
            if (actor == null) actor = GetComponent<SamuraiCombatant>();
            if (viewTransform != null) viewCamera = viewTransform.GetComponent<Camera>();
        }

        private void Start()
        {
            if (viewTransform == null && Camera.main != null)
            {
                viewCamera = Camera.main;
                viewTransform = viewCamera.transform;
            }
        }

        private void Update() => TickLock(Time.deltaTime);
        private void OnDisable() => ClearLock();
        private void OnApplicationFocus(bool focused) { if (!focused) ClearLock(); }

        /// <summary>有锁时解除；没有锁时从可见敌方活目标中选择屏幕中心最接近的一位。</summary>
        public void ToggleLock()
        {
            if (!ReferenceEquals(currentTarget, null)) { ClearLock(); return; }
            if (!isActiveAndEnabled || actor == null || !actor.isActiveAndEnabled || actor.IsDead || actor.IsBackstabbing) return;
            SetTarget(FindTarget(SearchMode.Centered, Vector2.zero));
        }

        private SamuraiCombatant FindTarget(SearchMode mode, Vector2 direction)
        {
            Vector2 origin = Vector2.zero;
            if (mode == SearchMode.Directional && (currentTarget == null ||
                !TryScreenPosition(currentTarget, false, out origin))) return null;
            visited.Clear();
            int count = Physics.OverlapSphereNonAlloc(actor.transform.position, acquireDistance, candidates,
                targetMask, QueryTriggerInteraction.Ignore);
            Collider[] found = candidates;
            if (count == candidates.Length)
            {
                found = Physics.OverlapSphere(actor.transform.position, acquireDistance, targetMask, QueryTriggerInteraction.Ignore);
                count = found.Length;
            }
            SamuraiCombatant best = null;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                SamuraiCombatant candidate = found[i].GetComponentInParent<SamuraiCombatant>();
                if (candidate == null || !visited.Add(candidate) || !IsEligible(candidate, acquireDistance)) continue;
                if (mode != SearchMode.Centered && candidate == currentTarget) continue;
                Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(true);
                if (!IsShown(candidate, renderers) || !HasLineOfSight(candidate)) continue;
                float score;
                if (mode == SearchMode.Nearest)
                    score = Vector3.SqrMagnitude(candidate.transform.position - actor.transform.position);
                else if (mode == SearchMode.Centered)
                {
                    if (!TryScreenScore(candidate, out score)) continue;
                }
                else
                {
                    if (!TryScreenPosition(candidate, true, out Vector2 screen)) continue;
                    Vector2 offset = screen - origin;
                    if (offset.sqrMagnitude < .000001f) continue;
                    float alignment = Vector2.Dot(offset.normalized, direction);
                    if (alignment < Mathf.Cos(switchDirectionHalfAngle * Mathf.Deg2Rad)) continue;
                    // 优先屏幕上相邻的目标；偏离甩动方向越大，评分惩罚越高。
                    score = offset.magnitude * (1f + 2f * (1f - alignment));
                }
                if (score >= bestScore) continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        public void ClearLock()
        {
            SetTarget(null);
        }

        private void SetTarget(SamuraiCombatant target)
        {
            // ReferenceEquals 能区分主动清空与已被 Destroy 的 Unity 假 null。
            bool changed = !ReferenceEquals(currentTarget, target);
            currentTarget = target;
            targetRenderers = target != null ? target.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            ResetMouseSwitch();
            if (changed) TargetChanged?.Invoke(currentTarget);
        }

        /// <summary>持续检查死亡、主动隐藏与距离；环境遮挡或离开画面不会解除已经建立的锁定。</summary>
        public void TickLock(float deltaTime)
        {
            if (ReferenceEquals(currentTarget, null)) return;
            if (!isActiveAndEnabled || actor == null || !actor.isActiveAndEnabled || actor.IsDead)
            { ClearLock(); return; }
            // Contact can kill the receiver before its paired fall finishes. Retain only that
            // existing lock; manual ClearLock still wins and no new target is acquired here.
            if (actor.IsBackstabbing && currentTarget != null && currentTarget == actor.ActiveBackstabTarget
                && currentTarget.isActiveAndEnabled && currentTarget.gameObject.activeInHierarchy
                && currentTarget.gameObject.scene == actor.gameObject.scene) return;
            if (currentTarget == null || !IsEligible(currentTarget, Mathf.Max(acquireDistance, releaseDistance)) ||
                !IsShown(currentTarget, targetRenderers))
                SetTarget(FindTarget(SearchMode.Nearest, Vector2.zero));
        }

        /// <summary>相对当前目标的屏幕方向，选择同方向的可见邻近敌人；无候选时保留原锁定。</summary>
        public bool TrySwitchTarget(Vector2 screenDirection)
        {
            if (!isActiveAndEnabled || actor == null || !actor.isActiveAndEnabled || actor.IsDead || actor.IsBackstabbing || currentTarget == null ||
                !IsFinite(screenDirection) || screenDirection.sqrMagnitude < .0001f) return false;
            var next = FindTarget(SearchMode.Directional, screenDirection.normalized);
            if (next == null) return false;
            SetTarget(next);
            return true;
        }

        /// <summary>输入层仅在捕获鼠标时提交像素增量和真实帧时长；一次连续快速移动只触发一次。</summary>
        public bool ReadMouseSwitch(Vector2 pixels, float deltaTime)
        {
            if (!isActiveAndEnabled || currentTarget == null || actor == null || !actor.isActiveAndEnabled || actor.IsDead || actor.IsBackstabbing)
            { ResetMouseSwitch(); return false; }
            if (!IsFinite(pixels) || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f)
            {
                switchMotion = Vector2.zero; switchMotionTime = switchQuietTime = 0f;
                return false;
            }
            switchCooldown = Mathf.Max(0f, switchCooldown - deltaTime);
            // 卡顿帧丢弃累积位移，但不能清掉冷却/重新武装，避免同一次甩动连跳。
            if (deltaTime > switchGestureSeconds)
            {
                switchMotion = Vector2.zero; switchMotionTime = switchQuietTime = 0f;
                return false;
            }
            if (pixels.magnitude / deltaTime < switchMouseSpeed)
            {
                switchMotion = Vector2.zero; switchMotionTime = 0f;
                switchQuietTime += deltaTime;
                if (switchQuietTime >= switchRearmSeconds) switchArmed = true;
                return false;
            }
            switchQuietTime = 0f;
            if (!switchArmed)
            {
                if (switchCooldown > 0f || Vector2.Dot(pixels.normalized, lastSwitchDirection) >= -.25f) return false;
                switchArmed = true;
            }
            if (switchCooldown > 0f) { switchMotion = Vector2.zero; switchMotionTime = 0f; return false; }
            if (switchMotionTime + deltaTime > switchGestureSeconds ||
                (switchMotion.sqrMagnitude > 0f && Vector2.Dot(switchMotion.normalized, pixels.normalized) < .5f))
            { switchMotion = Vector2.zero; switchMotionTime = 0f; }
            switchMotion += pixels;
            switchMotionTime += deltaTime;
            if (switchMotion.magnitude < switchMousePixels) return false;
            Vector2 direction = switchMotion.normalized;
            bool changed = TrySwitchTarget(direction);
            switchMotion = Vector2.zero; switchMotionTime = 0f;
            switchArmed = false; lastSwitchDirection = direction;
            switchCooldown = switchCooldownSeconds;
            return changed;
        }

        private void ResetMouseSwitch()
        {
            switchMotion = lastSwitchDirection = Vector2.zero;
            switchMotionTime = switchCooldown = switchQuietTime = 0f;
            switchArmed = true;
        }

        private static bool IsFinite(Vector2 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y);

        private bool TryScreenPosition(SamuraiCombatant candidate, bool requireOnScreen, out Vector2 screen)
        {
            Vector3 point = AimPoint(candidate);
            if (viewCamera != null)
            {
                Vector3 viewport = viewCamera.WorldToViewportPoint(point);
                // x乘宽高比，让横向和纵向使用同一屏幕尺度。
                screen = new Vector2(viewport.x * viewCamera.aspect, viewport.y);
                return viewport.z > 0f && (!requireOnScreen ||
                    (viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f));
            }
            Transform view = viewTransform != null ? viewTransform : actor.transform;
            Vector3 local = view.InverseTransformPoint(point);
            screen = local.z > 0f ? new Vector2(local.x / local.z, local.y / local.z) : Vector2.zero;
            return local.z > 0f && (!requireOnScreen || Vector3.Angle(Vector3.forward, local) <= 70f);
        }

        private bool IsEligible(SamuraiCombatant candidate, float range)
        {
            return candidate != null && candidate != actor && candidate.isActiveAndEnabled &&
                candidate.gameObject.activeInHierarchy && !candidate.IsDead && candidate.Team != actor.Team &&
                Vector3.SqrMagnitude(candidate.transform.position - actor.transform.position) <= range * range;
        }

        private static bool IsShown(SamuraiCombatant candidate, Renderer[] renderers)
        {
            // 只检查明确的隐藏/停用，不使用 Renderer.isVisible，避免遮挡剔除或离开画面影响锁定。
            // 无可视网格的测试/逻辑角色仍可作为目标；真正隐藏所有网格的角色会解除锁定。
            if (renderers.Length == 0) return candidate.gameObject.activeInHierarchy;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].enabled && renderers[i].gameObject.activeInHierarchy)
                    return true;
            return false;
        }

        private bool TryScreenScore(SamuraiCombatant candidate, out float score)
        {
            Vector3 point = AimPoint(candidate);
            // 距离只用于近似同屏位置的细小偏好，不能压过明显更居中的目标。
            float distancePenalty = Vector3.Distance(actor.transform.position, candidate.transform.position) / Mathf.Max(1f, acquireDistance) * 0.0025f;
            if (viewCamera != null)
            {
                Vector3 viewport = viewCamera.WorldToViewportPoint(point);
                score = (new Vector2(viewport.x - 0.5f, viewport.y - 0.5f)).sqrMagnitude + distancePenalty;
                return viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
            }
            Transform view = viewTransform != null ? viewTransform : actor.transform;
            float angle = Vector3.Angle(view.forward, point - view.position);
            score = angle / 70f + distancePenalty;
            return angle <= 70f;
        }

        private bool HasLineOfSight(SamuraiCombatant candidate)
        {
            Vector3 start = AimPoint(actor), end = AimPoint(candidate);
            Vector3 ray = end - start;
            float distance = ray.magnitude;
            if (distance < 0.01f) return true;
            int count = Physics.RaycastNonAlloc(start, ray / distance, obstructionHits, distance,
                obstructionMask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = obstructionHits;
            if (count == obstructionHits.Length)
            {
                hits = Physics.RaycastAll(start, ray / distance, distance, obstructionMask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null || hits[i].distance >= distance - 0.02f) continue;
                if (hits[i].collider.GetComponentInParent<SamuraiCombatant>() != null) continue;
                return false;
            }
            return true;
        }

        public static Vector3 AimPoint(SamuraiCombatant combatant)
        {
            return combatant.Motor != null
                ? combatant.transform.TransformPoint(combatant.Motor.center)
                : combatant.transform.position + Vector3.up;
        }

        private void OnValidate()
        {
            acquireDistance = Mathf.Max(1f, acquireDistance);
            releaseDistance = Mathf.Max(acquireDistance, releaseDistance);
            switchMousePixels = Mathf.Max(1f, switchMousePixels);
            switchMouseSpeed = Mathf.Max(1f, switchMouseSpeed);
            switchGestureSeconds = Mathf.Max(.01f, switchGestureSeconds);
            switchCooldownSeconds = Mathf.Max(0f, switchCooldownSeconds);
            switchRearmSeconds = Mathf.Max(.01f, switchRearmSeconds);
            switchDirectionHalfAngle = Mathf.Clamp(switchDirectionHalfAngle, 1f, 89f);
        }
    }
}
