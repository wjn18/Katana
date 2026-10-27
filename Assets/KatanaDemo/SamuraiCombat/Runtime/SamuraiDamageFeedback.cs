namespace OCDesign.Samurai
{
    /// <summary>一次伤害结算的表现快照。HealthDamage为实际生命损失，包含破防减伤和剩余生命上限。</summary>
    public readonly struct SamuraiDamageFeedback
    {
        public readonly SamuraiDamageHit Hit;
        public readonly SamuraiHitResult Result;
        public readonly float HealthDamage;

        public SamuraiDamageFeedback(SamuraiDamageHit hit, SamuraiHitResult result, float healthDamage)
        {
            Hit = hit;
            Result = result;
            HealthDamage = healthDamage;
        }
    }
}
