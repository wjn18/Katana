using System;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>固定精英HUD的数据选择与生命残影；只读已有锁定/追踪，不取得或唤醒战斗目标。</summary>
    public sealed class SamuraiEliteHUDPresenter
    {
        SamuraiCombatant observedPlayer, observedTarget;
        SamuraiTrainingEnemy[] candidates = Array.Empty<SamuraiTrainingEnemy>();
        float nextScan = float.NegativeInfinity;
        readonly SamuraiHUDHealthTrail healthTrail = new SamuraiHUDHealthTrail();

        public SamuraiCombatant CurrentTarget => observedTarget;

        // 当前唯一精英使用其既有移动适配组件辨识，不依赖实例名或可见网格/屏幕坐标。
        public static bool IsElite(SamuraiCombatant actor) => actor != null && actor.GetComponent<Elite1LocomotionPlayback>() != null;

        public SamuraiCombatant Resolve(SamuraiCombatant player, SamuraiTargetLock targetLock,
            SamuraiTrainingEnemy preferred, float displayDistance)
        {
            if (observedPlayer != player)
            {
                Reset(); observedPlayer = player;
            }
            if (player == null || !player.isActiveAndEnabled || player.IsDead)
            { Bind(null); return null; }
            // 主动锁定优先，锁到普通敌人即退出精英展示，不保留上一名精英的血量。
            var locked = targetLock != null ? targetLock.CurrentTarget : null;
            if (locked != null)
            {
                Bind(IsElite(locked) && Valid(locked, player, displayDistance) ? locked : null);
                return observedTarget;
            }
            if (observedTarget != null)
            {
                var brain = observedTarget.GetComponent<SamuraiTrainingEnemy>();
                if (Engaged(brain, player, displayDistance)) return observedTarget;
            }
            if (Engaged(preferred, player, displayDistance))
            { Bind(preferred.actor); return observedTarget; }
            if (Time.unscaledTime >= nextScan)
            {
                candidates = UnityEngine.Object.FindObjectsByType<SamuraiTrainingEnemy>();
                nextScan = Time.unscaledTime + .25f;
            }
            SamuraiCombatant best = null;
            float nearest = float.PositiveInfinity;
            foreach (var candidate in candidates)
            {
                if (!Engaged(candidate, player, displayDistance)) continue;
                float distance = (candidate.actor.transform.position - player.transform.position).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance; best = candidate.actor;
            }
            Bind(best);
            return observedTarget;
        }

        static bool Valid(SamuraiCombatant target, SamuraiCombatant player, float range) => target != null
            && target != player && target.isActiveAndEnabled && target.gameObject.activeInHierarchy && !target.IsDead
            && target.Team != player.Team && target.gameObject.scene == player.gameObject.scene
            && (range <= 0f || (target.transform.position - player.transform.position).sqrMagnitude <= range * range);

        static bool Engaged(SamuraiTrainingEnemy brain, SamuraiCombatant player, float displayDistance)
        {
            if (brain == null || !brain.isActiveAndEnabled || !brain.activeTraining || !brain.HasTrackedTarget
                || brain.target != player || !IsElite(brain.actor) || !Valid(brain.actor, player, displayDistance)) return false;
            Vector3 delta = brain.actor.transform.position - player.transform.position; delta.y = 0f;
            float distance = Mathf.Max(0f, brain.detectionDistance);
            return delta.sqrMagnitude <= distance * distance;
        }

        void Bind(SamuraiCombatant target)
        {
            if (ReferenceEquals(observedTarget, target)) return;
            if (observedTarget != null) observedTarget.CombatReset -= ResetHealthTrail;
            observedTarget = target;
            ResetHealthTrail();
            if (observedTarget != null) observedTarget.CombatReset += ResetHealthTrail;
        }

        public float HealthTrail(float now, float holdSeconds, float shrinkSeconds) => observedTarget != null
            ? healthTrail.Step(observedTarget.Health, observedTarget.MaxHealth, now, holdSeconds, shrinkSeconds) : 0f;

        public void ResetHealthTrail() => healthTrail.Clear();

        public void Reset()
        {
            Bind(null); observedPlayer = null;
            candidates = Array.Empty<SamuraiTrainingEnemy>(); nextScan = float.NegativeInfinity;
        }
    }
}
