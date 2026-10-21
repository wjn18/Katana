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
        readonly SamuraiCombatProfile profile;
        readonly WeaponTrailDetector detector = new WeaponTrailDetector();
        readonly HashSet<ISamuraiDamageReceiver> targets = new HashSet<ISamuraiDamageReceiver>();
        Vector3 previousBase, previousTip, previousOwnerPosition;
        float previousNormalized;
        float swingDamageMultiplier = 1f;
        bool sampleValid, sampling;
        uint swingRevision;
        public event Action<WeaponTrailSweep> SweepQueried;
        public event Action<ISamuraiDamageReceiver, SamuraiHitResult, Vector3> ContactResolved;
        public event Action HistoryCleared;

        public SamuraiWeaponAttack(ISamuraiDamageReceiver owner, Transform bladeBase, Transform bladeTip, SamuraiCombatProfile profile)
        {
            this.owner = owner; this.bladeBase = bladeBase; this.bladeTip = bladeTip; this.profile = profile;
        }
        public void BeginSwing(float damageMultiplier = 1f)
        {
            swingRevision++;
            swingDamageMultiplier = Mathf.Max(1f, damageMultiplier);
            targets.Clear();
            sampleValid = bladeBase != null && bladeTip != null;
            if (!sampleValid) return;
            previousBase = bladeBase.position; previousTip = bladeTip.position; previousNormalized = 0f;
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
            if (sampling || strike == null || bladeBase == null || bladeTip == null
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
            Vector3 currentBase = bladeBase.position, currentTip = bladeTip.position;
            if (!sampleValid)
            {
                previousBase = currentBase; previousTip = currentTip; previousNormalized = normalized;
                previousOwnerPosition = owner.DamageRoot.position;
            }
            if (normalized >= strike.damageStart && previousNormalized <= strike.damageEnd)
            {
                float span = Mathf.Max(.00001f, normalized - previousNormalized);
                float first = Mathf.Clamp01((strike.damageStart - previousNormalized) / span);
                float last = Mathf.Clamp01((strike.damageEnd - previousNormalized) / span);
                var sweep = new WeaponTrailSweep
                {
                    FromBase = Vector3.Lerp(previousBase, currentBase, first),
                    FromTip = Vector3.Lerp(previousTip, currentTip, first),
                    ToBase = Vector3.Lerp(previousBase, currentBase, last),
                    ToTip = Vector3.Lerp(previousTip, currentTip, last),
                    Radius = Mathf.Max(.01f, profile.bladeRadius)
                };
                SweepQueried?.Invoke(sweep);
                if (!CanContinue(revision, canContinue)) return;
                var contacts = detector.Query(sweep, profile.hitMask, profile.obstructionMask, this);
                ResolveContacts(contacts, strike, bodyPoint, canContinue, revision);
                if (!CanContinue(revision, canContinue)) return;

                // 补刀弧内侧和冲刺贴身滑过的空隙；帧间路径裁到有效窗，不能在收刀位置补伤害。
                float assistStart = Mathf.Max(strike.damageStart, strike.closeRangeStart);
                float assistEnd = Mathf.Min(strike.damageEnd, strike.closeRangeEnd);
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
                        ResolveContacts(contacts, strike, origin, canContinue, revision);
                    }
                }
            }
            // 回调可能弹反、切换招式或复位，不能将旧轨迹写回新一招。
            if (!CanContinue(revision, canContinue)) return;
            previousBase = currentBase; previousTip = currentTip; previousNormalized = normalized; sampleValid = true;
            previousOwnerPosition = owner.DamageRoot.position;
        }

        void ResolveContacts(IReadOnlyList<WeaponTrailContact> contacts, SamuraiActionDefinition strike,
            Vector3 bodyPoint, Func<bool> canContinue, uint revision)
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
                    KnockbackDistance = strike.knockbackDistance, KnockbackSeconds = strike.knockbackSeconds,
                    SourcePosition = owner.DamageRoot.position, Point = contact.Point, Source = owner
                });
                ContactResolved?.Invoke(target, result, contact.Point);
                if (!CanContinue(revision, canContinue)) return;
            }
        }

        bool CanContinue(uint revision, Func<bool> canContinue) =>
            revision == swingRevision && canContinue();

        static ISamuraiDamageReceiver Receiver(Collider collider) =>
            collider != null ? collider.GetComponentInParent<ISamuraiDamageReceiver>() : null;
        bool ValidTarget(ISamuraiDamageReceiver target) => target != null && !ReferenceEquals(target, owner)
            && target.CanReceiveDamage && target.Team != owner.Team;
        public bool IsTarget(Collider collider) => ValidTarget(Receiver(collider));
        public bool IsEnvironment(Collider collider) => collider != null && Receiver(collider) == null;
    }
}
