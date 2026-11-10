using UnityEngine;

namespace OCDesign.Samurai
{
    [CreateAssetMenu(menuName = "OC Design/Samurai/Combat Profile")]
    public sealed class SamuraiCombatProfile : ScriptableObject
    {
        [Header("Animator 状态：Idle / Walk / Run / Guard / GuardHit / GuardBreak / Hit / Death / Charge / Parry / Deflected")]
        public AnimationClip idle;
        public AnimationClip walk;
        public AnimationClip run;
        public AnimationClip guard;
        public AnimationClip guardHit;
        [Header("格挡受击：方向与精力伤害分级")]
        [Tooltip("Guard_B为右侧持刀防御姿态；不是允许从背后格挡。")]
        public AnimationClip guardB;
        [Tooltip("Guard方向的轻/中/重受击；空项回退现有GuardHit。")]
        public AnimationClip[] guardHitReactions = new AnimationClip[3];
        [Tooltip("Guard_B方向的轻/中/重受击；根据接触刀段的横向来势选择。")]
        public AnimationClip[] guardBHitReactions = new AnimationClip[3];
        [Min(0f)] public float guardMediumStaminaDamage = 35f;
        [Min(0f)] public float guardHeavyStaminaDamage = 55f;
        public AnimationClip guardBreak;
        public AnimationClip hit;
        [Header("普通受击：来击方向与精力伤害分级")]
        [Tooltip("仅明确启用的角色使用四方向轻/中/重受击；未启用、空片段或缺状态均回退原Hit。")]
        public bool directionalHitReactionsEnabled;
        [Tooltip("前方来击的轻/中/重反应，对应HitFront1/2/3。方向按实际冲击轨迹判断。")]
        public AnimationClip[] hitFrontReactions = new AnimationClip[3];
        [Tooltip("右侧来击的轻/中/重反应，对应HitRight1/2/3。")]
        public AnimationClip[] hitRightReactions = new AnimationClip[3];
        [Tooltip("后方来击的轻/中/重反应，对应HitBack1/2/3。")]
        public AnimationClip[] hitBackReactions = new AnimationClip[3];
        [Tooltip("左侧来击的轻/中/重反应，对应HitLeft1/2/3。")]
        public AnimationClip[] hitLeftReactions = new AnimationClip[3];
        [Min(0f)] public float hitMediumStaminaDamage = 35f;
        [Min(0f)] public float hitHeavyStaminaDamage = 55f;
        public AnimationClip death;
        [Tooltip("死亡时停用角色胶囊，尸体不阻挡玩家；根位置停止物理移动，死亡动画继续，复位恢复原启用状态。")]
        public bool disableCollisionOnDeath;
        public AnimationClip charge;
        public AnimationClip parry;
        [Tooltip("攻击被弹反后的独立反应；未配置或控制器缺少 Deflected 状态时回退到 Hit 表现。")]
        public AnimationClip deflected;
        public string deflectedStateName="Deflected";
        [Tooltip("Play the complete Deflected clip before allowing actions again.")]
        public bool deflectedUseFullClip;
        [Tooltip("Cumulative backward distance in metres, keyed in Deflected animation seconds. Movement uses character collision.")]
        public AnimationCurve deflectedRetreat;
        [HideInInspector] public int deflectedStumbleVersion;
        [Tooltip("Use a brief independent parry attempt without sustained blocking.")]
        public bool parryOnlyDefense;
        [HideInInspector] public int parryOnlyConfigurationVersion;
        [Tooltip("顺序：前、右前、右、右后、后、左后、左、左前。空项回退到Walk或Run。")]
        public AnimationClip[] directionalWalk = new AnimationClip[8];
        public AnimationClip[] directionalRun = new AnimationClip[8];
        public SamuraiActionDefinition[] combo = new SamuraiActionDefinition[4];
        [Header("架势与可选武技；留空沿用原连段")]
        [Tooltip("玩家顺序为高架势、中架势。旧角色留空即可保持原combo，不共享运行时选择状态。")]
        public SamuraiStanceDefinition[] stances = new SamuraiStanceDefinition[0];
        [Min(0)] public int defaultStanceIndex;
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
        [Header("敌人韧性 / 连续受击硬直抗性，玩家默认关闭")]
        public bool toughnessEnabled;
        [Min(1f)] public float maxToughness = 100f;
        [Min(0f)] public float toughnessRecoveryDelay = 3f;
        [Min(0f)] public float toughnessRecoveryPerSecond = 20f;
        [Range(0f, 1f)] public float toughnessGuardRecoveryMultiplier = .25f;
        [Range(0f, 1f)] public float toughnessGuardDamageMultiplier = .7f;
        [Range(0f, 1f)] public float toughnessPerfectGuardDamageMultiplier = .25f;
        [Range(0f, 1f)] public float deflectedToughnessFraction = .25f;
        [Min(.1f)] public float toughnessBreakSeconds = 3f;
        [Range(0f, 1f)] public float toughnessBrokenPose = .55f;
        [Tooltip("使用既有被弹反片段定格与恢复，避免倒地过渡中的腾空姿势。")]
        public bool toughnessBreakUsesDeflected;
        [Tooltip("进入破韧时生命至少50%使用的倒伏保持循环；需ToughnessBrokenHealthy状态。未配置或缺状态时沿用原定格，不改变失衡和恢复时长。")]
        public AnimationClip toughnessBrokenHealthyLoop;
        [Tooltip("进入破韧时生命低于50%使用的倒伏保持循环；需ToughnessBrokenLowHealth状态。本次失衡期间跨越生命阈值不切换。")]
        public AnimationClip toughnessBrokenLowHealthLoop;
        [Tooltip("N7倒伏结束后的完整起身片段；需ToughnessGetupHealthy状态。缺失时保留原恢复动作。")]
        public AnimationClip toughnessGetupHealthy;
        [Tooltip("N8倒伏结束后的完整起身片段；需ToughnessGetupLowHealth状态。沿用本次倒伏选择，不重新判断生命。")]
        public AnimationClip toughnessGetupLowHealth;
        [Tooltip("Down-to-getup blend in seconds; negative keeps the existing profile blend capped at .08 seconds.")]
        public float toughnessGetupTransitionSeconds=-1f;
        [HideInInspector] public int elitePoseContinuityVersion;
        [Range(0f, 1f)] public float executionHealthFraction = 1f;
        [Header("受处决：伤害在敌兵配置，未配置配对时禁止处决")]
        public SamuraiExecutionDamageMode executionDamageMode = SamuraiExecutionDamageMode.MaximumHealthFraction;
        [Min(0f)] public float executionFixedDamage = 45f;
        public SamuraiExecutionPair executionSurvivingPair;
        public SamuraiExecutionPair executionLethalPair;
        [HideInInspector] public int executionPairConfigurationVersion;
        [Header("背刺：仅明确配置的未警觉普通兵")]
        public SamuraiExecutionPair backstabPair;
        [Tooltip("玩家E背刺入口，与伏地处决开关独立。")]
        public bool backstabEnabled;
        [Tooltip("玩家离已验证的背刺站位最远距离；不扩大旧伏地处决范围。")]
        [Range(.05f,.6f)] public float backstabApproachRange=.55f;
        [Range(5f,80f)] public float backstabRearHalfAngle=50f;
        [Range(5f,90f)] public float backstabFacingAngle=60f;
        [Min(.1f)] public float backstabMaximumTargetHeight=2.2f;
        [HideInInspector] public int backstabConfigurationVersion;
        [Min(0f)] public float baseStaggerResistance = 10f;
        [Min(0f)] public float maximumAccumulatedResistance = 90f;
        [Min(0f)] public float resistanceDecayDelay = 1.5f;
        [Min(0f)] public float resistanceDecayPerSecond = 30f;
        [Min(0f)] public float chargeStaggerResistanceBonus = 15f;
        [Range(0f, 1f)] public float resistedKnockbackMultiplier = .25f;
        [Header("玩家主动处决；不启用敌人处决玩家")]
        [Tooltip("玩家E入口；目标还必须配置专用受方配对。")]
        public bool executionEnabled;
        public SamuraiActionDefinition executionAction;
        [Min(0f)] public float executionSurfaceRange = 1f;
        [HideInInspector] public int toughnessConfigurationVersion;
        [Header("Q 残心")]
        [Tooltip("仅对明确启用的角色开放残心；旧配置与敌人默认保持关闭。")]
        public bool kiPulseEnabled;
        [Tooltip("无架势专用残心时的回退动画。留空保持原先直接回待机；移动、攻防闪和受击可立即打断。")]
        public AnimationClip kiPulseAnimation;
        public string kiPulseStateName;
        [Range(0f, 1f)] public float kiPulseRecoveryFraction = .8f;
        [Min(.01f)] public float kiPulseGatherSeconds = .45f;
        [Min(0f)] public float kiPulsePerfectSeconds = .15f;
        [UnityEngine.Serialization.FormerlySerializedAs("kiPulseStanceUpgradeEnabled")]
        [Tooltip("成功残心后切到另一架势获得独立流转奖励；普通/完美残心均可，时机判定保持独立。")]
        public bool kiPulseFluxEnabled;
        [UnityEngine.Serialization.FormerlySerializedAs("kiPulseStanceWindowSeconds")]
        [Tooltip("从成功Q的原始战斗时刻计时；0关闭，命中停顿和暂停不会消耗窗口。")]
        [Min(0f)] public float kiPulseFluxWindowSeconds = .30f;
        [Tooltip("每次流转额外恢复的最大精力比例，独立于本轮白条。当前默认0.10为项目调参，非原作精确数值。")]
        [Range(0f, 1f)] public float kiPulseFluxRecoveryFraction = .10f;
        [Header("冬月：残心 / 流转后的架势组合")]
        public bool frostMoonEnabled;
        [Tooltip("成功残心或流转后的冬月组合受付时间；当前为项目可调窗口，非原作精确测量。")]
        [Min(0f)] public float frostMoonWindowSeconds = .30f;
        [Header("琢技：交锋蓄槽 / 强攻击或武技启动")]
        public bool artsEnabled;
        [Min(1f)] public float artsGaugeMaximum = 100f;
        [Min(0f)] public float artsAttackGain = 8f;
        [Min(0f)] public float artsGuardGain = 4f;
        [Min(0f)] public float artsDeflectGain = 20f;
        [Min(.1f)] public float artsDurationSeconds = 6f;
        [Min(1f)] public float artsDamageMultiplier = 1.2f;
        [Tooltip("武技使用此生命伤害倍率；不与强攻击倍率重复相乘。完美蓄力释放倍率仍独立叠乘。")]
        [Min(1f)] public float artsMartialArtDamageMultiplier = 1.2f;
        [Range(0f, 1f)] public float artsStaminaCostMultiplier = .7f;
        [Tooltip("额外强化名单，保留旧配置兼容；标记Is Martial Art的武技与Heavy/Charged自动符合资格。")]
        public SamuraiActionDefinition[] artsAdditionalAttacks = new SamuraiActionDefinition[0];
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
        [Header("防御受力后退")]
        [Tooltip("普通格挡中档后退的基准距离（米）；轻/重再乘对应倍率。0关闭，不使用攻击的受伤击退距离。")]
        [Min(0f)] public float guardKnockbackDistance;
        [Tooltip("轻档后退相对中档基准的倍率；旧配置默认1，保持原后退距离。")]
        [Min(0f)] public float guardLightKnockbackMultiplier = 1f;
        [Tooltip("重档后退相对中档基准的倍率；强度阈值与格挡动画共用。")]
        [Min(0f)] public float guardHeavyKnockbackMultiplier = 1f;
        [Tooltip("防御后退的战斗时间；高初速度，随后快速减速，局部命中停顿期间冻结。")]
        [Min(.01f)] public float guardKnockbackSeconds = .18f;
        [Tooltip("成功弹反相对同一攻击强度普通格挡的后退倍率；共用时长与减速曲线。")]
        [Range(0f, 1f)] public float parryKnockbackMultiplier = .5f;
        [Header("F 及时格挡 / 弹反")]
        [Tooltip("F新按下后的有效窗口；继续持F转为普通格挡，连按不能刷新冷却中的窗口。")]
        [Min(.01f)] public float parryActiveSeconds = .20f;
        [Min(0f)] public float parryCooldownSeconds = .7f;
        [Min(.05f)] public float parryStunSeconds = 1.1f;
        [Tooltip("成功时播放Parry反制动画；此恢复时间后允许攻击或继续持防。")]
        [Min(.05f)] public float parrySuccessRecoverySeconds = .28f;
        [Header("F 连续弹反")]
        [Tooltip("显式启用后使用可连续接刀、成功回耐的弹反；未启用的角色沿用原完美格挡规则。")]
        public bool deflectEnabled;
        [Min(.01f)] public float deflectActiveSeconds = .20f;
        [Tooltip("一次未成功尝试的冷却，从实际按下时刻开始；成功后立即清除。")]
        [Min(0f)] public float deflectMissCooldownSeconds = .35f;
        [Range(0f, 1f)] public float deflectRecoveryFraction = .10f;
        [Tooltip("成功后攻击与闪避的恢复时间；期间仍可持防或再次按F接刀。")]
        [Min(.01f)] public float deflectSuccessRecoverySeconds = .12f;
        [Tooltip("成功弹反施加给攻击者的被弹反硬直；立即中断攻击与待接连段。")]
        [Min(.05f)] public float deflectStunSeconds = .7f;
        [Header("刀刃扫掠与遮挡")]
        [Tooltip("刀根、刀尖及刀刃插值点的SphereCast半径；不需要武器Collider或Rigidbody。")]
        [Min(0.01f)] public float bladeRadius = 0.13f;
        public LayerMask hitMask = ~0;
        public LayerMask obstructionMask = ~0;
    }
}
