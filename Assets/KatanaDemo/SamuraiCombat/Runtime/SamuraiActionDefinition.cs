using UnityEngine;

namespace OCDesign.Samurai
{
    public enum SamuraiHitSource { Weapon, LeftFoot, RightFoot }
    /// <summary>所有窗口均为源片段归一化时间；可单独减慢首个命中窗之前的动作。</summary>
    [CreateAssetMenu(menuName = "OC Design/Samurai/Action")]
    public sealed class SamuraiActionDefinition : ScriptableObject
    {
        public AnimationClip clip;
        public string stateName = "Light01";
        [Header("招式身份")]
        [Tooltip("显示用名称；留空沿用Animator状态名，不改变状态或资源引用。")]
        public string displayName;
        [Tooltip("明确归类为武技；触发方式（蓄力、冲刺或闪避派生）与武技身份相互独立。")]
        public bool isMartialArt;
        [Header("冬月防御，仅合法冬月派生时生效")]
        [Tooltip("归一化抗打断窗口[start,end)。照常扣血；不能阻止死亡或不可防御攻击。零长度关闭。")]
        public Vector2 frostMoonSuperArmorWindow;
        [Tooltip("归一化起手防御窗口[start,end)。仅挡正面可防御攻击，照常扣精，破防会中断；不赠送F弹反回精。零长度关闭。")]
        public Vector2 frostMoonGuardWindow;
        [Tooltip("此招独有的蓄力准备状态；留空时保持架势待机。原版居合专用Charge，其他动作不借用。")]
        public string chargeStateName;
        [Min(0f)] public float damage = 20f;
        [Min(0f)] public float staminaDamage = 25f;
        [Min(0f)] public float staminaCost = 14f;
        [Header("攻击前摇播放速率")]
        [Tooltip("只作用于首个伤害窗之前；挥击与收招恢复1倍，不修改源片段/伤害窗口。")]
        [Range(.1f, 1f)] public float attackWindupSpeed = 1f;
        [HideInInspector] public int windupConfigurationVersion;
        [Header("韧性伤害与硬直冲击，独立于生命/精力伤害")]
        [Min(0f)] public float toughnessDamage;
        [Min(0f)] public float impactStrength;
        [Tooltip("可选逐命中窗覆盖；留空沿用本招标量。长度须等于伤害窗口数。")]
        public float[] toughnessDamageByWindow = new float[0];
        public float[] impactStrengthByWindow = new float[0];
        [Tooltip("自身执行本招时的临时硬直抗性；横轴为动作归一化时间，段间/收招回落。")]
        public AnimationCurve staggerResistanceBonus = AnimationCurve.Constant(0f, 1f, 0f);
        [HideInInspector] public int toughnessConfigurationVersion;
        public bool unblockable;
        [Tooltip("接触刀段没有明显横向运动时的防御侧回退；Automatic沿用防御者当前架势。实际横向刀轨优先。")]
        public SamuraiGuardSide guardReactionSide;

        [Header("命中后退，仅实际受伤或破防生效")]
        [Tooltip("目标沿远离攻击来源的水平方向后退；0关闭。位移经角色碰撞体，不穿墙。")]
        [Min(0f)] public float knockbackDistance;
        [Min(.01f)] public float knockbackSeconds = .18f;

        [Header("命中与连段")]
        [Range(0f, 1f)] public float damageStart = 0.22f;
        [Range(0f, 1f)] public float damageEnd = 0.48f;
        [Tooltip("整段动画的归一化时间：X起始、Y结束，秒数=相位×Clip时长。留空沿用Damage Start/End。区域与真实武器轨迹共用此门控；收招误伤应调整本窗。按时间排序且不重叠，每窗对同一目标只命中一次。")]
        public Vector2[] damageWindows = new Vector2[0];
        public SamuraiHitSource hitSource;
        [Tooltip("0沿用角色刀刃半径；脚部攻击使用独立半径。")]
        [Min(0f)] public float hitRadius;
        [Range(0f, 1f)] public float comboInputStart = 0.12f;
        [Range(0f, 1f)] public float comboInputEnd = 0.85f;
        [Range(0f, 1f)] public float comboLink = 0.65f;
        [Tooltip("续连进入此招的混合秒数；负值沿用角色默认。单独起手及其他状态不受影响。")]
        public float comboBlendSeconds = -1f;
        [Tooltip("防御或闪避只能在命中段结束后的此处取消后摇。")]
        [Range(0f, 1f)] public float cancelStart = 0.58f;

        [Header("残心时机，默认使用角色配置")]
        public bool overrideKiPulseTiming;
        [Tooltip("只在覆盖开启时使用；实际开窗不会早于该招的 Cancel Start。")]
        [Range(0f, 1f)] public float kiPulseStart = .58f;
        [Min(.01f)] public float kiPulseGatherSeconds = .45f;
        [Min(0f)] public float kiPulsePerfectSeconds = .15f;

        [Header("近身补偿，距离为0时关闭")]
        [Tooltip("身体中心到目标碰撞体最近表面的距离；只补刀弧内侧。")]
        [Min(0f)] public float closeRangeReach;
        [Range(0f, 90f)] public float closeRangeHalfAngle = 55f;
        [Range(0f, 1f)] public float closeRangeStart = .10f;
        [Range(0f, 1f)] public float closeRangeEnd = .42f;
        [HideInInspector] public int enemyCloseRangeConfigurationVersion;

        [Header("逐命中窗攻击区域")]
        [Tooltip("启用后以独立区域配合真实武器轨迹；替代旧统一近身补偿。关闭区域项或清空数组只保留真实轨迹。")]
        public bool useAttackVolumes;
        [Tooltip("按Damage Windows下标一一对应。区域生效时间为该窗与Frames首尾Phase的交集。真实武器轨迹仍可在该窗内独立命中；要关闭收招伤害请改Damage Windows。")]
        public SamuraiAttackVolume[] attackVolumes = new SamuraiAttackVolume[0];
        [HideInInspector] public int attackVolumeConfigurationVersion;

        [Header("胶囊位移")]
        [Tooltip("由原始动画根运动驱动水平位移，经CharacterController碰撞；忽略固定跨步与接近减速。")]
        public bool useRootMotion;
        [Tooltip("原始根骨相对起点的X位移，单位米。玩家横轴为归一化时间；已接入敌人使用动画秒，避免密集键求值失真。")]
        public AnimationCurve rootMotionX = new AnimationCurve();
        [Tooltip("原始根骨相对起点的Z位移，单位米；保留负向回收及完整时间曲线。")]
        public AnimationCurve rootMotionZ = new AnimationCurve();
        [Tooltip("对原根骨水平轨迹统一倍乘，时长不变，位移速度同比变化；不修改源曲线。")]
        [Min(0f)] public float rootMotionDistanceMultiplier = 1f;
        [HideInInspector] public int enemyRootMotionConfigurationVersion;
        [Header("敌人原刀轨采样")]
        [Tooltip("正式身体动画首窗的局部米制武器端点；供AI规划及根运动帧间补采样。实际半径、窗口和伤害仍取原配置。")]
        public SamuraiWeaponRangeFrame[] weaponRangeFrames = new SamuraiWeaponRangeFrame[0];
        [Min(0f)] public float weaponRangeRadius;
        [Min(0f)] public float stepDistance = 0.28f;
        [Range(0f, 1f)] public float stepStart = 0.08f;
        [Range(0f, 1f)] public float stepEnd = 0.40f;
        [Header("武技接近减速，默认关闭")]
        [Tooltip("仅调整胶囊跨步，不改变动画播放速度。默认靠近或接触后减速；推动敌人开启时仅在路径受阻后减速。")]
        public bool approachEnabled;
        [Tooltip("默认是两角色胶囊之间的减速距离，另保留6厘米硬间隙；推动敌人时改为玩家或被推敌人与固定阻挡之间的减速距离。")]
        [Min(0f)] public float approachStopGap = .4f;
        [Min(0f)] public float approachSlowSpeed = .25f;
        [Tooltip("仅在接近位移开启时生效：推动前方敌方胶囊，命中与目标距离不减速；墙体或无法推动的阻挡仍限速。")]
        public bool pushEnemies;
        [Header("仅用于闪避")]
        [Range(0f, 1f)] public float invulnerableStart = 0.10f;
        [Range(0f, 1f)] public float invulnerableEnd = 0.58f;

        public float Duration => clip != null ? Mathf.Max(0.02f, clip.length) : 0.02f;
        public float WindupEnd => Mathf.Clamp01(GetDamageWindow(0).x);
        public float AttackPlaybackSpeed(float normalized) => normalized < WindupEnd
            ? Mathf.Clamp(attackWindupSpeed, .1f, 1f) : 1f;
        public float PlaybackDuration => Duration * (1f - WindupEnd + WindupEnd / Mathf.Clamp(attackWindupSpeed, .1f, 1f));
        public int DamageWindowCount => damageWindows != null && damageWindows.Length > 0 ? damageWindows.Length : 1;
        public float ToughnessDamageAt(int index) => toughnessDamageByWindow != null && toughnessDamageByWindow.Length == DamageWindowCount
            ? Mathf.Max(0f, toughnessDamageByWindow[Mathf.Clamp(index, 0, DamageWindowCount - 1)]) : Mathf.Max(0f, toughnessDamage);
        public float ImpactStrengthAt(int index) => impactStrengthByWindow != null && impactStrengthByWindow.Length == DamageWindowCount
            ? Mathf.Max(0f, impactStrengthByWindow[Mathf.Clamp(index, 0, DamageWindowCount - 1)]) : Mathf.Max(0f, impactStrength);
        public float StaggerResistanceAt(float time) => staggerResistanceBonus != null
            ? Mathf.Max(0f, staggerResistanceBonus.Evaluate(Mathf.Clamp01(time))) : 0f;
        public Vector2 GetDamageWindow(int index) => damageWindows != null && damageWindows.Length > 0
            ? damageWindows[index] : new Vector2(damageStart, damageEnd);
        public float DamageEnd
        {
            get
            {
                if (damageWindows == null || damageWindows.Length == 0) return damageEnd;
                float end = 0f;
                foreach (var window in damageWindows) end = Mathf.Max(end, window.y);
                return Mathf.Clamp01(end);
            }
        }
        public float CancelStart => Mathf.Clamp01(Mathf.Max(DamageEnd, cancelStart));
        public float KiPulseStart => overrideKiPulseTiming ? Mathf.Max(CancelStart, Mathf.Clamp01(kiPulseStart)) : CancelStart;
        public bool IsUsable => clip != null && !string.IsNullOrWhiteSpace(stateName);
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? stateName : displayName;
        public float RootMotionCurveTimeScale => enemyRootMotionConfigurationVersion >= 1 ? Duration : 1f;
        public Vector3 RootMotionPosition(float normalizedTime) => new Vector3(
            rootMotionX != null ? rootMotionX.Evaluate(Mathf.Clamp01(normalizedTime) * RootMotionCurveTimeScale) : 0f, 0f,
            rootMotionZ != null ? rootMotionZ.Evaluate(Mathf.Clamp01(normalizedTime) * RootMotionCurveTimeScale) : 0f) *
            (float.IsNaN(rootMotionDistanceMultiplier) || float.IsInfinity(rootMotionDistanceMultiplier)
                ? 1f : Mathf.Max(0f, rootMotionDistanceMultiplier));
        public float StepFraction(float normalizedTime)
        {
            float start = Mathf.Clamp01(stepStart);
            float end = Mathf.Clamp(stepEnd, start + 0.001f, 1.001f);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, normalizedTime));
        }
    }
}
