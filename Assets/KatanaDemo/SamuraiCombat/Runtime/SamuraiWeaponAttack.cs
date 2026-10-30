using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 武器检测与受击接口之间的适配器：有效窗口、阵营、单招去重、遮挡与伤害提交。
    /// 几何算法由WeaponTrailDetector和SamuraiAttackVolumeDetector承担；不持有状态机或输入。
    /// </summary>
    public sealed class SamuraiWeaponAttack : IWeaponTrailFilter
    {
        readonly ISamuraiDamageReceiver owner;
        readonly Transform bladeBase, bladeTip;
        readonly Transform leftFoot, leftToes, rightFoot, rightToes;
        readonly SamuraiCombatProfile profile;
        readonly WeaponTrailDetector detector = new WeaponTrailDetector();
        readonly SamuraiAttackVolumeDetector volumeDetector = new SamuraiAttackVolumeDetector();
        readonly List<WeaponTrailContact> windowContacts = new List<WeaponTrailContact>(32);
        readonly HashSet<ISamuraiDamageReceiver> targets = new HashSet<ISamuraiDamageReceiver>();
        Vector3 previousBase, previousTip, previousOwnerPosition;
        Quaternion previousOwnerRotation;
        float previousNormalized;
        float swingDamageMultiplier = 1f;
        bool sampleValid, historyValid, sampling;
        uint swingRevision;
        int hitWindowIndex = -1;
        public event Action<WeaponTrailSweep> SweepQueried;
        public event Action<SamuraiAttackVolume, Vector3, Quaternion, float> VolumeQueried;
        public event Action<ISamuraiDamageReceiver, SamuraiHitResult, Vector3> ContactResolved;
        public event Action HistoryCleared;

        public SamuraiWeaponAttack(ISamuraiDamageReceiver owner, Transform bladeBase, Transform bladeTip, SamuraiCombatProfile profile, Animator animator = null)
        {
            this.owner = owner; this.bladeBase = bladeBase; this.bladeTip = bladeTip; this.profile = profile;
            volumeDetector.PoseQueried += (region, position, rotation, phase) => VolumeQueried?.Invoke(region, position, rotation, phase);
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
            previousNormalized = 0f;
            previousOwnerPosition = owner.DamageRoot.position;
            previousOwnerRotation = owner.DamageRoot.rotation;
            historyValid = true;
        }
        public void CancelSwing()
        {
            swingRevision++;
            swingDamageMultiplier = 1f;
            sampleValid = false;
            historyValid = false;
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
            currentQueryNormalized = normalized;
            bool hasEndpoints = TryEndpoints(strike, out Vector3 currentBase, out Vector3 currentTip);
            if (!hasEndpoints && !strike.useAttackVolumes) return;
            if (!historyValid || normalized < previousNormalized)
            {
                previousNormalized = normalized;
                previousOwnerPosition = owner.DamageRoot.position;
                previousOwnerRotation = owner.DamageRoot.rotation;
            }
            if (!sampleValid) { previousBase = currentBase; previousTip = currentTip; }
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
                windowContacts.Clear();
                if (strike.useAttackVolumes && strike.attackVolumes != null && windowIndex < strike.attackVolumes.Length)
                {
                    var region = strike.attackVolumes[windowIndex];
                    if (region != null && region.IsUsable)
                    {
                        var regionContacts = volumeDetector.Query(region, Mathf.Max(window.x, previousNormalized), Mathf.Min(window.y, normalized),
                            previousOwnerPosition, previousOwnerRotation, owner.DamageRoot.position, owner.DamageRoot.rotation,
                            previousNormalized, normalized, profile.hitMask, profile.obstructionMask, this);
                        windowContacts.AddRange(regionContacts);
                    }
                }
                if (!CanContinue(revision, canContinue)) return;
                if (hasEndpoints)
                {
                    if (!QueryOriginalWeaponPath(strike, windowIndex, window, canContinue, revision))
                    {
                        SweepQueried?.Invoke(sweep);
                        if (!CanContinue(revision, canContinue)) return;
                        windowContacts.AddRange(detector.Query(sweep, profile.hitMask, profile.obstructionMask, this));
                    }
                }
                // 两种接触按窗内时间合并，弹反回调必须先于更晚的目标接触。
                windowContacts.Sort((a, b) => a.Time.CompareTo(b.Time));
                ResolveContacts(windowContacts, strike, windowIndex, bodyPoint, canContinue, revision, sweep, true);
                if (!CanContinue(revision, canContinue)) return;

                // 补刀弧内侧和冲刺贴身滑过的空隙；帧间路径裁到有效窗，不能在收刀位置补伤害。
                float assistStart = Mathf.Max(window.x, strike.closeRangeStart);
                float assistEnd = Mathf.Min(window.y, strike.closeRangeEnd);
                // 新Enemy1配置只补原武器首窗；未标记的玩家Charged等保留既有补偿分支。
                bool closeRangeEligible = strike.enemyCloseRangeConfigurationVersion < 1
                    || windowIndex == 0 && strike.hitSource == SamuraiHitSource.Weapon;
                if (closeRangeEligible && !strike.useAttackVolumes && strike.closeRangeReach > 0f && assistEnd >= assistStart
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
                        var contacts = detector.QuerySector(origin, owner.DamageRoot.forward, strike.closeRangeReach,
                            strike.closeRangeHalfAngle, profile.hitMask, profile.obstructionMask, this);
                        ResolveContacts(contacts, strike, windowIndex, origin, canContinue, revision, sweep, false);
                    }
                }
            }
            // 回调可能弹反、切换招式或复位，不能将旧轨迹写回新一招。
            if (!CanContinue(revision, canContinue)) return;
            previousBase = currentBase; previousTip = currentTip; previousNormalized = normalized; sampleValid = hasEndpoints; historyValid = true;
            previousOwnerPosition = owner.DamageRoot.position;
            previousOwnerRotation = owner.DamageRoot.rotation;
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

        // 根运动的短促Generic挥击按原身体轨迹细分。大帧弦线可能从地下起步而漏掉原本经过玩家的刀弧。
        // 只改变原刀轨的时间采样密度；半径、有效窗、遮挡与伤害提交仍使用原配置。
        bool QueryOriginalWeaponPath(SamuraiActionDefinition strike, int windowIndex, Vector2 window,
            Func<bool> canContinue, uint revision)
        {
            var frames = strike.weaponRangeFrames;
            if (!strike.useRootMotion || strike.enemyRootMotionConfigurationVersion < 1 || strike.useAttackVolumes
                || strike.hitSource != SamuraiHitSource.Weapon || windowIndex != 0 || frames == null || frames.Length < 2) return false;
            if (frames[0].phase > window.x + .00001f || frames[frames.Length - 1].phase < window.y - .00001f) return false;
            for (int i = 0; i < frames.Length; i++)
                if (!SamuraiAttackVolume.Finite(frames[i].phase) || !FiniteRangePoint(frames[i].from) || !FiniteRangePoint(frames[i].to)
                    || (i > 0 && frames[i].phase <= frames[i - 1].phase)) return false;
            float queryFirst = Mathf.Max(window.x, previousNormalized), queryLast = Mathf.Min(window.y, currentQueryNormalized);
            float first = Mathf.Max(queryFirst, frames[0].phase), last = Mathf.Min(queryLast, frames[frames.Length - 1].phase);
            if (last < first) return false;
            if (last - first < .0000001f)
            {
                RangeEndpoints(frames, first, out Vector3 start, out Vector3 end);
                var still = new WeaponTrailSweep { FromBase = start, ToBase = start, FromTip = end, ToTip = end,
                    Radius = Mathf.Max(.01f, strike.hitRadius > 0f ? strike.hitRadius : profile.bladeRadius) };
                SweepQueried?.Invoke(still);
                if (CanContinue(revision, canContinue)) windowContacts.AddRange(detector.Query(still, profile.hitMask, profile.obstructionMask, this));
                return true;
            }
            float fromPhase = first;
            for (int key = 0; key <= frames.Length && CanContinue(revision, canContinue); key++)
            {
                float toPhase = key < frames.Length ? frames[key].phase : last;
                if (toPhase <= fromPhase || toPhase > last) continue;
                RangeEndpoints(frames, fromPhase, out Vector3 fromBase, out Vector3 fromTip);
                RangeEndpoints(frames, toPhase, out Vector3 toBase, out Vector3 toTip);
                var part = new WeaponTrailSweep { FromBase = fromBase, FromTip = fromTip, ToBase = toBase, ToTip = toTip,
                    Radius = Mathf.Max(.01f, strike.hitRadius > 0f ? strike.hitRadius : profile.bladeRadius) };
                SweepQueried?.Invoke(part);
                if (!CanContinue(revision, canContinue)) return true;
                var contacts = detector.Query(part, profile.hitMask, profile.obstructionMask, this);
                for (int i = 0; i < contacts.Count; i++)
                {
                    var contact = contacts[i];
                    contact.Motion = ContactMotion(part, contact.Point, contact.Time); contact.HasMotion = true;
                    float phase = Mathf.Lerp(fromPhase, toPhase, contact.Time);
                    contact.Time = queryLast > queryFirst ? Mathf.Clamp01((phase - queryFirst) / (queryLast - queryFirst)) : 0f;
                    windowContacts.Add(contact);
                }
                fromPhase = toPhase;
                if (fromPhase >= last) break;
            }
            return true;
        }

        float currentQueryNormalized;
        void RangeEndpoints(SamuraiWeaponRangeFrame[] frames, float phase, out Vector3 bladeFrom, out Vector3 bladeTo)
        {
            int hi = 1;
            while (hi < frames.Length - 1 && frames[hi].phase < phase) hi++;
            var a = frames[hi - 1]; var b = frames[hi];
            float t = b.phase > a.phase ? Mathf.Clamp01((phase - a.phase) / (b.phase - a.phase)) : 0f;
            float rootT = currentQueryNormalized > previousNormalized ? Mathf.Clamp01((phase - previousNormalized) / (currentQueryNormalized - previousNormalized)) : 1f;
            Quaternion rotation = Quaternion.Slerp(previousOwnerRotation, owner.DamageRoot.rotation, rootT);
            Vector3 root = Vector3.Lerp(previousOwnerPosition, owner.DamageRoot.position, rootT);
            Vector3 scale = owner.DamageRoot.lossyScale;
            bladeFrom = root + rotation * Vector3.Scale(Vector3.Lerp(a.from, b.from, t), scale);
            bladeTo = root + rotation * Vector3.Scale(Vector3.Lerp(a.to, b.to, t), scale);
        }
        static bool FiniteRangePoint(Vector3 p) => SamuraiAttackVolume.Finite(p.x) && SamuraiAttackVolume.Finite(p.y) && SamuraiAttackVolume.Finite(p.z);

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
                    BladeMotion = contact.HasMotion ? contact.Motion : ContactMotion(sweep, contact.Point, swept ? contact.Time : 1f),
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
