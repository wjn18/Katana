using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>订阅正式命中结算，只处理重击/蓄力的顿帧与镜头反馈，不更改伤害。</summary>
    [DisallowMultipleComponent]
    public sealed class SamuraiImpactFeedback : MonoBehaviour
    {
        [SerializeField] SamuraiCombatant actor;
        [SerializeField, Range(0f, .3f)] float heavyHitStop = .075f;
        [SerializeField, Range(0f, .3f)] float chargedHitStop = .14f;
        [SerializeField, Range(0f, 1f)] float blockedMultiplier = .5f;
        SamuraiCombatant subscribedActor;
        int lastImpactSerial = -1;
        public int ImpactCount { get; private set; }
        public float LastHitStopSeconds { get; private set; }

        public void Configure(SamuraiCombatant combatant)
        {
            Unsubscribe();
            actor = combatant;
            lastImpactSerial = -1;
            if (Application.isPlaying && isActiveAndEnabled) Subscribe();
        }
        void OnEnable() { if (Application.isPlaying) Subscribe(); }
        void OnDisable() { Unsubscribe(); if (actor != null) actor.ClearHitStop(); }
        void OnDestroy() => Unsubscribe();
        void Subscribe()
        {
            if (subscribedActor == actor) return;
            Unsubscribe();
            subscribedActor = actor;
            if (subscribedActor != null) subscribedActor.DamageDealt += OnDamageDealt;
        }
        void Unsubscribe()
        {
            if (subscribedActor != null) subscribedActor.DamageDealt -= OnDamageDealt;
            subscribedActor = null;
        }
        void OnDamageDealt(ISamuraiDamageReceiver receiver, SamuraiHitResult result, Vector3 point)
        {
            if (actor == null || !actor.isActiveAndEnabled || !actor.IsAttacking
                || actor.Actions == null || actor.Profile == null) return;
            if (result != SamuraiHitResult.Hit && result != SamuraiHitResult.Killed
                && result != SamuraiHitResult.GuardBroken && result != SamuraiHitResult.Blocked) return;
            var action = actor.Actions.ActiveAction;
            bool charged = action != null && action == actor.Profile.charged;
            if (!charged && (action == null || action != actor.Profile.heavy)) return;
            float seconds = charged ? chargedHitStop : heavyHitStop;
            if (result == SamuraiHitResult.Blocked) seconds *= blockedMultiplier;
            var victim = receiver as SamuraiCombatant;
            // 同一帧扫中多个目标时，每个目标均停顿；攻击者和相机每招只触发一次。
            if (victim != null) victim.ApplyHitStop(seconds);
            if (actor.Actions.AttackSerial == lastImpactSerial) return;
            lastImpactSerial = actor.Actions.AttackSerial;
            LastHitStopSeconds = seconds;
            ImpactCount++;
            actor.ApplyHitStop(seconds);
            var view = PlayerCamera(actor);
            if (view == null && victim != null) view = PlayerCamera(victim);
            if (view != null) view.PlayImpact(charged);
        }
        static SamuraiCombatCameraFeedback PlayerCamera(SamuraiCombatant combatant)
        {
            var input = combatant.GetComponent<SamuraiPlayerInput>();
            return input != null && input.orbitCamera != null
                ? input.orbitCamera.GetComponent<SamuraiCombatCameraFeedback>() : null;
        }
    }
}
