using UnityEngine;

namespace OCDesign.Samurai
{
    [CreateAssetMenu(menuName = "OC Design/Samurai/Combat Profile")]
    public sealed class SamuraiCombatProfile : ScriptableObject
    {
        [Header("Animator 状态：Idle / Walk / Run / Guard / GuardHit / GuardBreak / Hit / Death / Charge / Parry")]
        public AnimationClip idle;
        public AnimationClip walk;
        public AnimationClip run;
        public AnimationClip guard;
        public AnimationClip guardHit;
        public AnimationClip guardBreak;
        public AnimationClip hit;
        public AnimationClip death;
        public AnimationClip charge;
        public AnimationClip parry;
        [Tooltip("顺序：前、右前、右、右后、后、左后、左、左前。空项回退到Walk或Run。")]
        public AnimationClip[] directionalWalk = new AnimationClip[8];
        public AnimationClip[] directionalRun = new AnimationClip[8];
        public SamuraiActionDefinition[] combo = new SamuraiActionDefinition[4];
        public SamuraiActionDefinition heavy;
        public SamuraiActionDefinition charged;
        [Tooltip("持续奔跑后按轻攻击使用；为空时继续使用普通连段。")]
        public SamuraiActionDefinition sprintAttack;
        [Tooltip("闪避末期或结束后的短窗口内按轻攻击使用；为空时不启用闪避追击。")]
        public SamuraiActionDefinition dodgeAttack;
        [Tooltip("闪避追击再次轻攻击时使用的第一段普攻变体；完成后接普通第二段。")]
        public SamuraiActionDefinition dodgeAttackToCombo1;
        [Tooltip("顺序：前、右前、右、右后、后、左后、左、左前。")]
        public SamuraiActionDefinition[] dodges = new SamuraiActionDefinition[8];

        [Header("生命与耐力")]
        [Min(1f)] public float maxHealth = 120f;
        [Min(1f)] public float maxStamina = 100f;
        [Min(0f)] public float staminaRecovery = 25f;
        [Min(0f)] public float staminaRecoveryDelay = 0.65f;
        [Min(0f)] public float sprintStaminaPerSecond = 9f;
        [Range(0f, 1f)] public float guardRecoveryMultiplier = 0.35f;
        [Header("移动与输入")]
        [Min(0f)] public float walkSpeed = 1.7f;
        [Min(0f)] public float runSpeed = 3.6f;
        [Tooltip("已持续奔跑的最短时间；原地同时按奔跑和攻击仍为普通第一段。")]
        [Min(0f)] public float sprintAttackMinRunSeconds = .15f;
        [Min(0f)] public float guardWalkSpeed = 0.65f;
        [Min(0f)] public float turnSpeed = 720f;
        [Tooltip("在45度方向分界之外额外保留的角度，减少锁定绕行时的片段反复切换。")]
        [Range(0f, 20f)] public float locomotionDirectionHysteresis = 7f;
        [Min(0f)] public float gravity = 22f;
        [Min(0f)] public float terminalFallSpeed = 35f;
        [Range(0f, 0.4f)] public float inputBufferSeconds = 0.18f;
        [Range(0f, 0.25f)] public float transitionSeconds = 0.08f;
        [Header("闪避后追击")]
        [Tooltip("闪避播放到此比例后才接收轻攻击边沿；缓存时长沿用 Input Buffer Seconds。")]
        [Range(0f, 1f)] public float dodgeAttackInputStart = .55f;
        [Tooltip("闪避播放到此比例后允许追击；实际不会早于该闪避的无敌结束点。")]
        [Range(0f, 1f)] public float dodgeAttackStart = .72f;
        [Tooltip("正常完成闪避后仍可按轻攻击追击的时间；其他动作或中断会取消资格。")]
        [Min(0f)] public float dodgeAttackGraceSeconds = .25f;
        [Header("蓄力攻击")]
        [Min(0.1f)] public float chargeSeconds = 1.5f;
        [Tooltip("满蓄后首次有效释放窗口；继续持有不会重新开启。")]
        [Min(0f)] public float chargeCriticalWindowSeconds = .25f;
        [Tooltip("窗口内成功释放蓄力斩的生命伤害倍率，不改变耐力伤害。")]
        [Min(1f)] public float chargeCriticalDamageMultiplier = 2f;
        [Header("方向防御")]
        [Range(0f, 180f)] public float guardHalfAngle = 65f;
        // 旧资产字段保留兼容；实际完美格挡统一使用下方F弹反配置。
        [HideInInspector] public float perfectGuardSeconds = 0.16f;
        [HideInInspector] public float perfectGuardStunSeconds = 0.65f;
        [Range(0f, 1f)] public float guardBreakDamageMultiplier = 0.35f;
        [Header("F 及时格挡 / 弹反")]
        [Tooltip("F新按下后的有效窗口；继续持F转为普通格挡，连按不能刷新冷却中的窗口。")]
        [Min(.01f)] public float parryActiveSeconds = .20f;
        [Min(0f)] public float parryCooldownSeconds = .7f;
        [Min(.05f)] public float parryStunSeconds = 1.1f;
        [Tooltip("成功时播放Parry反制动画；此恢复时间后允许攻击或继续持防。")]
        [Min(.05f)] public float parrySuccessRecoverySeconds = .28f;
        [Header("刀刃扫掠与遮挡")]
        [Tooltip("刀根、刀尖及刀刃插值点的SphereCast半径；不需要武器Collider或Rigidbody。")]
        [Min(0.01f)] public float bladeRadius = 0.13f;
        public LayerMask hitMask = ~0;
        public LayerMask obstructionMask = ~0;
    }
}
