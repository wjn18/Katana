using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 武器检测与受击接口之间的适配器：有效窗口、阵营、单招去重、遮挡与伤害提交。
    /// 几何算法由WeaponTrailDetector承担；不持有状态机、Animator或输入。
    /// </summary>
    public sealed class SamuraiWeaponAttack : IWeaponTrailFilter
    {
        readonly ISamuraiDamageReceiver owner;
        readonly Transform bladeBase, bladeTip;
        readonly Transform leftFoot, leftToes, rightFoot, rightToes;
        readonly SamuraiCombatProfile profile;
        readonly WeaponTrailDetector detector = new WeaponTrailDetector();
        readonly HashSet<ISamuraiDamageReceiver> targets = new HashSet<ISamuraiDamageReceiver>();
        Vector3 previousBase, previousTip, previousOwnerPosition;
        float previousNormalized;
        float swingDamageMultiplier = 1f;
        bool sampleValid, sampling;
        uint swingRevision;
        int hitWindowIndex = -1;
        public event Action<WeaponTrailSweep> SweepQueried;
        public event Action<ISamuraiDamageReceiver, SamuraiHitResult, Vector3> ContactResolved;
        public event Action HistoryCleared;

        public SamuraiWeaponAttack(ISamuraiDamageReceiver owner, Transform bladeBase, Transform bladeTip, SamuraiCombatProfile profile, Animator animator = null)
        {
            this.owner = owner; this.bladeBase = bladeBase; this.bladeTip = bladeTip; this.profile = profile;
            if (animator != null && animator.isHuman)
            {
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
            }
        }
        public void BeginSwing(float damageMultiplier = 1f, SamuraiActionDefinition strike = null)
        {
            swingRevision++;
            swingDamageMultiplier = Mathf.Max(1f, damageMultiplier);
            targets.Clear();
            hitWindowIndex = -1;
            sampleValid = TryEndpoints(strike, out previousBase, out previousTip);
            if (!sampleValid) return;
            previousNormalized = 0f;
            previousOwnerPosition = owner.DamageRoot.position;
        }
        public void CancelSwing()
        {
            swingRevision++;
            swingDamageMultiplier = 1f;
            sampleValid = false;
        }
        public void Reset()
        {
            CancelSwing(); targets.Clear(); HistoryCleared?.Invoke();
        }

        public void Sample(float normalized, SamuraiActionDefinition strike, Vector3 bodyPoint, Func<bool> canContinue)
        {
            if (sampling || strike == null
                || canContinue == null || !canContinue()) return;
            uint revision = swingRevision;
            sampling = true;
            try
            {
                SampleCurrentSwing(normalized, strike, bodyPoint, canContinue, revision);
            }
            finally { sampling = false; }
        }

        void SampleCurrentSwing(float normalized, SamuraiActionDefinition strike, Vector3 bodyPoint,
            Func<bool> canContinue, uint revision)
        {
            if (!TryEndpoints(strike, out Vector3 currentBase, out Vector3 currentTip)) return;
            if (!sampleValid)
            {
                previousBase = currentBase; previousTip = currentTip; previousNormalized = normalized;
                previousOwnerPosition = owner.DamageRoot.position;
            }
            for (int windowIndex = 0; windowIndex < strike.DamageWindowCount; windowIndex++)
            {
                if (windowIndex < hitWindowIndex) continue;
                Vector2 window = strike.GetDamageWindow(windowIndex);
                if (window.y <= window.x || normalized < window.x || previousNormalized > window.y) continue;
                if (hitWindowIndex != windowIndex) { targets.Clear(); hitWindowIndex = windowIndex; }
                float span = Mathf.Max(.00001f, normalized - previousNormalized);
                float first = Mathf.Clamp01((window.x - previousNormalized) / span);
                float last = Mathf.Clamp01((window.y - previousNormalized) / span);
                var sweep = new WeaponTrailSweep
                {
                    FromBase = Vector3.Lerp(previousBase, currentBase, first),
                    FromTip = Vector3.Lerp(previousTip, currentTip, first),
                    ToBase = Vector3.Lerp(previousBase, currentBase, last),
                    ToTip = Vector3.Lerp(previousTip, currentTip, last),
                    Radius = Mathf.Max(.01f, strike.hitRadius > 0f ? strike.hitRadius : profile.bladeRadius)
                };
                SweepQueried?.Invoke(sweep);
                if (!CanContinue(revision, canContinue)) return;
                var contacts = detector.Query(sweep, profile.hitMask, profile.obstructionMask, this);
                ResolveContacts(contacts, strike, windowIndex, bodyPoint, canContinue, revision, sweep, true);
                if (!CanContinue(revision, canContinue)) return;

                // 补刀弧内侧和冲刺贴身滑过的空隙；帧间路径裁到有效窗，不能在收刀位置补伤害。
                float assistStart = Mathf.Max(window.x, strike.closeRangeStart);
                float assistEnd = Mathf.Min(window.y, strike.closeRangeEnd);
                if (strike.closeRangeReach > 0f && assistEnd >= assistStart
                    && normalized >= assistStart && previousNormalized <= assistEnd)
                {
                    float assistFirst = Mathf.Clamp01((assistStart - previousNormalized) / span);
                    float assistLast = Mathf.Clamp01((assistEnd - previousNormalized) / span);
                    Vector3 previousBody = previousOwnerPosition + bodyPoint - owner.DamageRoot.position;
                    Vector3 from = Vector3.Lerp(previousBody, bodyPoint, assistFirst);
                    Vector3 to = Vector3.Lerp(previousBody, bodyPoint, assistLast);
                    float spacing = Mathf.Max(.05f, strike.closeRangeReach * .125f);
                    int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, to) / spacing), 1, 64);
                    for (int i = 0; i <= steps && CanContinue(revision, canContinue); i++)
                    {
                        Vector3 origin = Vector3.Lerp(from, to, (float)i / steps);
                        contacts = detector.QuerySector(origin, owner.DamageRoot.forward, strike.closeRangeReach,
                            strike.closeRangeHalfAngle, profile.hitMask, profile.obstructionMask, this);
                        ResolveContacts(contacts, strike, windowIndex, origin, canContinue, revision, sweep, false);
                    }
                }
            }
            // 回调可能弹反、切换招式或复位，不能将旧轨迹写回新一招。
            if (!CanContinue(revision, canContinue)) return;
            previousBase = currentBase; previousTip = currentTip; previousNormalized = normalized; sampleValid = true;
            previousOwnerPosition = owner.DamageRoot.position;
        }

        bool TryEndpoints(SamuraiActionDefinition strike, out Vector3 from, out Vector3 to)
        {
            from = to = Vector3.zero;
            SamuraiHitSource source = strike != null ? strike.hitSource : SamuraiHitSource.Weapon;
            if (source == SamuraiHitSource.Weapon)
            {
                if (bladeBase == null || bladeTip == null) return false;
                from = bladeBase.position; to = bladeTip.position;
                return true;
            }
            Transform foot = source == SamuraiHitSource.LeftFoot ? leftFoot : rightFoot;
            Transform toes = source == SamuraiHitSource.LeftFoot ? leftToes : rightToes;
            if (foot == null) return false;
            from = foot.position;
            to = toes != null ? toes.position : from + owner.DamageRoot.forward * .16f;
            return true;
        }

        void ResolveContacts(IReadOnlyList<WeaponTrailContact> contacts, SamuraiActionDefinition strike, int windowIndex,
            Vector3 bodyPoint, Func<bool> canContinue, uint revision, WeaponTrailSweep sweep, bool swept)
        {
            for (int i = 0; i < contacts.Count && CanContinue(revision, canContinue); i++)
            {
                WeaponTrailContact contact = contacts[i];
                var target = Receiver(contact.Collider);
                if (!ValidTarget(target) || targets.Contains(target)) continue;
                if (detector.Obstructed(bodyPoint, contact.Point, profile.obstructionMask, this)
                    || detector.Obstructed(contact.BladeBase, contact.Point, profile.obstructionMask, this)) continue;
                if (!CanContinue(revision, canContinue)) return;
                targets.Add(target);
                SamuraiHitResult result = target.ReceiveHit(new SamuraiDamageHit
                {
                    Damage = strike.damage * swingDamageMultiplier, StaminaDamage = strike.staminaDamage, Unblockable = strike.unblockable,
                    ToughnessDamage = strike.ToughnessDamageAt(windowIndex), ImpactStrength = strike.ImpactStrengthAt(windowIndex),
                    KnockbackDistance = strike.knockbackDistance, KnockbackSeconds = strike.knockbackSeconds,
                    BladeMotion = ContactMotion(sweep, contact.Point, swept ? contact.Time : 1f),
                    GuardReactionSide = strike.guardReactionSide,
                    SourcePosition = owner.DamageRoot.position, Point = contact.Point, Source = owner
                });
                ContactResolved?.Invoke(target, result, contact.Point);
                if (!CanContinue(revision, canContinue)) return;
            }
        }

        bool CanContinue(uint revision, Func<bool> canContinue) =>
            revision == swingRevision && canContinue();

        static Vector3 ContactMotion(WeaponTrailSweep sweep, Vector3 point, float time)
        {
            Vector3 from = Vector3.Lerp(sweep.FromBase, sweep.ToBase, time);
            Vector3 blade = Vector3.Lerp(sweep.FromTip, sweep.ToTip, time) - from;
            float along = blade.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(point - from, blade) / blade.sqrMagnitude) : .5f;
            return Vector3.Lerp(sweep.ToBase - sweep.FromBase, sweep.ToTip - sweep.FromTip, along);
        }

        static ISamuraiDamageReceiver Receiver(Collider collider) =>
            collider != null ? collider.GetComponentInParent<ISamuraiDamageReceiver>() : null;
        bool ValidTarget(ISamuraiDamageReceiver target) => target != null && !ReferenceEquals(target, owner)
            && target.CanReceiveDamage && target.Team != owner.Team;
        public bool IsTarget(Collider collider) => ValidTarget(Receiver(collider));
        public bool IsEnvironment(Collider collider) => collider != null && Receiver(collider) == null;
    }
}
