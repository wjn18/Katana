using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>主动锁定屏幕中心附近的敌人；只在切换时搜索候选，持续锁定只检查当前目标。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-30)]
    public sealed class SamuraiTargetLock : MonoBehaviour
    {
        public SamuraiCombatant actor;
        public Transform viewTransform;
        [Min(1f)] public float acquireDistance = 12f;
        [Min(1f)] public float releaseDistance = 14f;
        [Min(0f)] public float occlusionGraceSeconds = 0.35f;
        [Tooltip("可被锁定角色的碰撞层；角色必须有启用的碰撞体。")]
        public LayerMask targetMask = ~0;
        [Tooltip("环境遮挡层；角色自身和其他战斗角色不作为环境遮挡。")]
        public LayerMask obstructionMask = ~0;

        private readonly Collider[] candidates = new Collider[64];
        private readonly RaycastHit[] obstructionHits = new RaycastHit[32];
        private readonly HashSet<SamuraiCombatant> visited = new HashSet<SamuraiCombatant>();
        private SamuraiCombatant currentTarget;
        private Renderer[] targetRenderers = Array.Empty<Renderer>();
        private Camera viewCamera;
        private float occludedSeconds;

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
            if (currentTarget != null) { ClearLock(); return; }
            if (!isActiveAndEnabled || actor == null || actor.IsDead) return;
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
                Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(true);
                if (!IsShown(candidate, renderers) || !HasLineOfSight(candidate) || !TryScreenScore(candidate, out float score)) continue;
                if (score >= bestScore) continue;
                best = candidate;
                bestScore = score;
            }
            if (best == null) return;
            currentTarget = best;
            targetRenderers = best.GetComponentsInChildren<Renderer>(true);
            occludedSeconds = 0f;
            TargetChanged?.Invoke(currentTarget);
        }

        public void ClearLock()
        {
            bool hadTarget = currentTarget != null;
            currentTarget = null;
            targetRenderers = Array.Empty<Renderer>();
            occludedSeconds = 0f;
            if (hadTarget) TargetChanged?.Invoke(null);
        }

        /// <summary>公开的持续检查入口，便于确定性验证死亡、隐藏、距离和遮挡宽限。</summary>
        public void TickLock(float deltaTime)
        {
            if (currentTarget == null) return;
            if (actor == null || actor.IsDead || !IsEligible(currentTarget, Mathf.Max(acquireDistance, releaseDistance)) ||
                !IsShown(currentTarget, targetRenderers))
            { ClearLock(); return; }
            if (HasLineOfSight(currentTarget)) occludedSeconds = 0f;
            else
            {
                occludedSeconds += Mathf.Max(0f, deltaTime);
                if (occludedSeconds >= occlusionGraceSeconds) ClearLock();
            }
        }

        private bool IsEligible(SamuraiCombatant candidate, float range)
        {
            return candidate != null && candidate != actor && candidate.isActiveAndEnabled &&
                candidate.gameObject.activeInHierarchy && !candidate.IsDead && candidate.Team != actor.Team &&
                Vector3.SqrMagnitude(candidate.transform.position - actor.transform.position) <= range * range;
        }

        private static bool IsShown(SamuraiCombatant candidate, Renderer[] renderers)
        {
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
            occlusionGraceSeconds = Mathf.Max(0f, occlusionGraceSeconds);
        }
    }
}
