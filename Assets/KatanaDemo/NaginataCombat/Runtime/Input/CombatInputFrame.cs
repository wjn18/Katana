using UnityEngine;

namespace OCDesign.Combat
{
    /// <summary>
    /// 单帧操作意图。输入设备与战斗逻辑只交换数据，便于以后接入 AI、回放或其他设备。
    /// </summary>
    public readonly struct CombatInputFrame
    {
        private readonly bool hasAttack;
        private readonly int attackIndex;

        public Vector2 Move { get; }
        public Vector2 Look { get; }
        public bool Sprint { get; }
        public bool PrimaryAttackPressed { get; }
        public bool HeavyAttackPressed { get; }

        // 单独保留有效标记，使 default(CombatInputFrame) 也不会意外请求第 0 种攻击。
        public int AttackIndex => hasAttack ? attackIndex : -1;
        public static CombatInputFrame None => default;

        public CombatInputFrame(Vector2 move, Vector2 look, bool sprint, int attackIndex = -1,
            bool primaryAttackPressed = false, bool heavyAttackPressed = false)
        {
            Move = move;
            Look = look;
            Sprint = sprint;
            PrimaryAttackPressed = primaryAttackPressed;
            HeavyAttackPressed = heavyAttackPressed;
            this.attackIndex = attackIndex;
            hasAttack = attackIndex >= 0;
        }
    }
}
