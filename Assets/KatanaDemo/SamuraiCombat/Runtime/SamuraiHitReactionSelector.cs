using UnityEngine;

namespace OCDesign.Samurai
{
    public enum SamuraiHitDirection { Front, Right, Back, Left }

    public struct SamuraiHitReactionSelection
    {
        public AnimationClip Clip;
        public string StateName;
        public SamuraiHitDirection Direction;
        public int Tier;
    }

    /// <summary>普通受击只选择表现，不改变伤害、格挡资格、击退或反应优先级。</summary>
    public static class SamuraiHitReactionSelector
    {
        static readonly string[] StatePrefixes = { "HitFront", "HitRight", "HitBack", "HitLeft" };
        static readonly string[] StateNames =
        {
            "HitFront1", "HitFront2", "HitFront3",
            "HitRight1", "HitRight2", "HitRight3",
            "HitBack1", "HitBack2", "HitBack3",
            "HitLeft1", "HitLeft2", "HitLeft3"
        };

        public static SamuraiHitReactionSelection Select(SamuraiCombatProfile profile, SamuraiDamageHit hit, Transform defender)
        {
            var selection = new SamuraiHitReactionSelection
            {
                Clip = profile.hit, StateName = "Hit", Direction = SamuraiHitDirection.Front, Tier = 0
            };
            if (!profile.directionalHitReactionsEnabled) return selection;
            selection.Direction = Direction(hit, defender);
            selection.Tier = StrengthTier(profile, hit.StaminaDamage);
            AnimationClip[] clips;
            switch (selection.Direction)
            {
                case SamuraiHitDirection.Right: clips = profile.hitRightReactions; break;
                case SamuraiHitDirection.Back: clips = profile.hitBackReactions; break;
                case SamuraiHitDirection.Left: clips = profile.hitLeftReactions; break;
                default: clips = profile.hitFrontReactions; break;
            }
            var clip = clips != null && selection.Tier < clips.Length ? clips[selection.Tier] : null;
            if (clip != null)
            {
                selection.Clip = clip;
                selection.StateName = StateNames[(int)selection.Direction * 3 + selection.Tier];
            }
            return selection;
        }

        /// <summary>方向表示来击侧：+X运动从左来，+Z运动从背后来；竖直或零轨迹用来源位置回退。</summary>
        public static SamuraiHitDirection Direction(SamuraiDamageHit hit, Transform defender)
        {
            Vector3 motion = defender != null ? defender.InverseTransformDirection(hit.BladeMotion) : hit.BladeMotion;
            float horizontalSquared = motion.x * motion.x + motion.z * motion.z;
            Vector3 toward;
            if (Finite(motion) && horizontalSquared > .0000000001f
                && horizontalSquared > motion.sqrMagnitude * .0225f)
                toward = -motion;
            else
            {
                toward = defender != null ? defender.InverseTransformDirection(hit.SourcePosition - defender.position) : Vector3.forward;
                if (!Finite(toward) || toward.x * toward.x + toward.z * toward.z <= .0000000001f)
                    return SamuraiHitDirection.Front;
            }
            if (Mathf.Abs(toward.x) > Mathf.Abs(toward.z))
                return toward.x > 0f ? SamuraiHitDirection.Right : SamuraiHitDirection.Left;
            return toward.z >= 0f ? SamuraiHitDirection.Front : SamuraiHitDirection.Back;
        }

        public static int StrengthTier(SamuraiCombatProfile profile, float staminaDamage)
        {
            float stamina = Finite(staminaDamage) ? Mathf.Max(0f, staminaDamage) : 0f;
            float medium = Mathf.Max(0f, profile.hitMediumStaminaDamage);
            float heavy = Mathf.Max(medium, profile.hitHeavyStaminaDamage);
            return stamina >= heavy ? 2 : stamina >= medium ? 1 : 0;
        }

        public static bool IsReactionState(string state)
        {
            if (string.IsNullOrEmpty(state)) return false;
            for (int i = 0; i < StatePrefixes.Length; i++)
            {
                string prefix = StatePrefixes[i];
                if (state.Length == prefix.Length + 1 && state.StartsWith(prefix, System.StringComparison.Ordinal)
                    && state[state.Length - 1] >= '1' && state[state.Length - 1] <= '3') return true;
            }
            return false;
        }

        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
