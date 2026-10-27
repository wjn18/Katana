using UnityEngine;

namespace OCDesign.Samurai
{
    public struct SamuraiGuardReactionSelection
    {
        public AnimationClip Clip;
        public string StateName;
        public bool Backhand;
        public int Tier;
    }

    /// <summary>根据实际来刀方向和攻击精力伤害选择反应；不决定防御资格或扣耐。</summary>
    public static class SamuraiGuardReactionSelector
    {
        public static SamuraiGuardReactionSelection Select(SamuraiCombatProfile profile,
            SamuraiDamageHit hit, Transform defender, bool previousBackhand)
        {
            bool backhand = previousBackhand;
            Vector3 motion = defender != null ? defender.InverseTransformDirection(hit.BladeMotion) : hit.BladeMotion;
            // Guard接住左侧来刀（向局部+X运动），Guard_B接住右侧来刀（向局部-X）。
            // 竖劈、突刺、初始重叠不凭攻击者站位猜挥刀方向。
            bool lateral = Finite(motion.x) && Finite(motion.y) && Finite(motion.z)
                && motion.sqrMagnitude > .0000000001f && Mathf.Abs(motion.x) > motion.magnitude * .15f;
            if (lateral) backhand = motion.x < 0f;
            else if (hit.GuardReactionSide != SamuraiGuardSide.Automatic)
                backhand = hit.GuardReactionSide == SamuraiGuardSide.GuardB;
            int tier = StrengthTier(profile, hit.StaminaDamage);
            AnimationClip clip = At(backhand ? profile.guardBHitReactions : profile.guardHitReactions, tier);
            if (clip == null && backhand)
            {
                backhand = false;
                clip = At(profile.guardHitReactions, tier);
            }
            if (clip == null) { backhand = false; tier = 0; clip = profile.guardHit; }
            return new SamuraiGuardReactionSelection
            {
                Clip = clip, Backhand = backhand, Tier = tier,
                StateName = backhand ? "GuardBHit" + (tier + 1) : tier == 0 ? "GuardHit" : "GuardHit" + (tier + 1)
            };
        }

        /// <summary>动画与受力后退共用的攻击强度；不受动画配置缺失或剩余精力影响。</summary>
        public static int StrengthTier(SamuraiCombatProfile profile, float staminaDamage)
        {
            float stamina = Finite(staminaDamage) ? Mathf.Max(0f, staminaDamage) : 0f;
            float medium = Mathf.Max(0f, profile.guardMediumStaminaDamage);
            float heavy = Mathf.Max(medium, profile.guardHeavyStaminaDamage);
            return stamina >= heavy ? 2 : stamina >= medium ? 1 : 0;
        }

        public static bool IsReactionState(string state) => state == "GuardHit" || state == "GuardHit2"
            || state == "GuardHit3" || state == "GuardBHit1" || state == "GuardBHit2" || state == "GuardBHit3";

        static AnimationClip At(AnimationClip[] clips, int index) =>
            clips != null && index < clips.Length ? clips[index] : null;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
