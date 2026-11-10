using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 场景内 uGUI 的数据绑定。所有图像、文本和布局由场景保存；
    /// 只有头顶目标与预警根节点随世界目标投影移动；精英条读取固定屏幕布局，不在运行时构造或排列 UI。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class SamuraiHUDView : MonoBehaviour
    {
        [Header("战斗引用")]
        public SamuraiCombatant player;
        public SamuraiTrainingEnemy enemy;
        public SamuraiPlayerInput input;
        public Camera worldCamera;
        public Canvas canvas;
        [Tooltip("由设置界面管理显隐，本组件不改动该节点的启用状态。")]
        public GameObject combatRoot;

        [Header("生命与精力")]
        public Image healthFill;
        [Tooltip("位于实际生命条下方，保留本次扣除的生命区间。")]
        public Image healthDamageFill;
        public Image staminaFill;
        public Image staminaBorder;
        [Min(0f)] public float healthDamageHoldSeconds = .45f;
        [Min(0f)] public float healthDamageShrinkSeconds = .45f;
        [Min(.1f)] public float staminaWarningSeconds = 1.2f;
        [Min(.1f)] public float staminaWarningFrequency = 3f;
        public Color healthDamageColor = new Color(.98f, .81f, .79f, .56f);
        public Color staminaWarningColor = new Color(.86f, .97f, .89f, .48f);
        [Tooltip("放在精力底层；显示当前精力加尚可回收的总额度。")]
        public Image kiPendingFill;
        [Tooltip("放在灰白待回收条之上、绿色精力条之下。")]
        public Image kiRecoverableFill;
        public Image kiKeyGlow;

        [Header("琢技")]
        public GameObject artsRoot;
        public Image artsRing;
        public Image artsSymbol;
        public Text artsTimer;
        [Min(0f)] public float artsReadyPulseSpeed = 3.5f;
        [Range(0f, 1f)] public float artsReadyMinimumAlpha = .62f;

        [Header("架势")]
        public Image highStanceFrame;
        public Image midStanceFrame;
        public Image highStanceIcon;
        public Image midStanceIcon;
        public GameObject highStanceMarker;
        public GameObject midStanceMarker;
        public int highStanceIndex = 0;
        public int middleStanceIndex = 1;

        [Header("目标与攻击预警")]
        public RectTransform targetRoot;
        public Image targetHealthFill;
        public Image targetHealthDamageFill;
        public Image targetToughnessFill;
        public Text targetExecutionPrompt;
        public Image lockMarker;
        public RectTransform warningRoot;
        public Image warningFill;
        [Tooltip("目标血条在角色根节点上方的世界高度，单位：米。")]
        [Min(0f)] public float targetHeight = 1.65f;
        [Min(0f)] public float warningHeight = 1.9f;
        [Tooltip("投影后在父 RectTransform 局部坐标中的额外偏移。")]
        public Vector2 targetScreenOffset;
        public Vector2 warningScreenOffset;
        [Min(0f)] public float targetDisplayDistance = 20f;

        [Header("固定精英HUD，不随世界坐标投影")]
        public RectTransform eliteRoot;
        public Image eliteHealthFill;
        public Image eliteHealthDamageFill;
        public Image eliteToughnessFill;
        public Text eliteName;
        public Text eliteHealthText;
        public Text eliteToughnessText;
        public string eliteDisplayName = "镇门鬼将";

        [Header("短时反馈")]
        public Text briefFeedback;
        [Min(.05f)] public float feedbackSeconds = .7f;
        public GameObject deathPanel;
        public Text deathText;
        public string deathMessage = "战败\nR 重试";
        public GameObject captureHint;
        public GameObject chargeRoot;
        public Image chargeFill;

        [Header("可调整的界面色彩")]
        public Color healthColor = new Color(.76f, .20f, .15f, 1f);
        public Color staminaColor = new Color(.25f, .67f, .44f, 1f);
        public Color pendingColor = new Color(.55f, .62f, .65f, .8f);
        public Color kiRecoverableColor = new Color(.30f, .75f, .84f, 1f);
        public Color kiPerfectColor = new Color(.78f, .99f, 1f, 1f);
        public Color kiIdleColor = new Color(.50f, .60f, .63f, .22f);
        public Color artsBuildingColor = new Color(.30f, .63f, .91f, 1f);
        public Color goldColor = new Color(.95f, .73f, .32f, 1f);
        public Color inactiveFrameColor = new Color(.47f, .44f, .37f, .55f);
        public Color selectedIconColor = new Color(1f, .91f, .68f, 1f);
        public Color inactiveIconColor = new Color(.64f, .65f, .61f, .42f);

        private SamuraiCombatant observedPlayer;
        private SamuraiCombatant observedHealthTarget;
        private readonly SamuraiHUDHealthTrail playerHealthTrail = new SamuraiHUDHealthTrail();
        private readonly SamuraiHUDHealthTrail targetHealthTrail = new SamuraiHUDHealthTrail();
        private readonly SamuraiEliteHUDPresenter elitePresenter = new SamuraiEliteHUDPresenter();
        private Image observedStaminaBorder;
        private Color staminaBorderColor;
        private float staminaWarningStartedAt = float.NegativeInfinity;
        private RectTransform observedTargetRoot;
        private RectTransform observedWarningRoot;
        private RectTransform targetParent;
        private RectTransform warningParent;
        private float feedbackStartedAt = float.NegativeInfinity;
        private Color feedbackColor;
        private int displayedArtsTenth = int.MinValue;

        private void OnEnable()
        {
            displayedArtsTenth = int.MinValue;
            RefreshNow();
        }

        private void OnDisable()
        {
            UnbindPlayer();
            BindHealthTarget(null);
            ResetVitalFeedback();
            elitePresenter.Reset();
            SetActive(eliteRoot, false);
        }

        // 相机完成本帧的跟随后再投影，避免头顶血条比角色画面慢一帧。
        private void LateUpdate() => RefreshNow();

        /// <summary>供场景装配、Inspector 调整及运行检查显式同步一次。</summary>
        public void RefreshNow()
        {
            BindPlayer();
            CacheProjectionParents();
            if (player == null)
            {
                BindHealthTarget(null);
                ResetVitalFeedback();
                SetActive(targetRoot, false);
                SetActive(warningRoot, false);
                SetActive(chargeRoot, false);
                SetActive(deathPanel, false);
                SetActive(captureHint, false);
                SetActive(artsRoot, false);
                elitePresenter.Reset();
                SetActive(eliteRoot, false);
                SetText(briefFeedback, string.Empty);
                return;
            }

            RefreshVitals();
            RefreshArts();
            RefreshStances();
            RefreshCharge();
            RefreshFeedback();
            RefreshTarget();
            RefreshEliteTarget();
            SetActive(deathPanel, player.IsDead);
            SetText(deathText, deathMessage);
            SetActive(captureHint, !player.IsDead && input != null && !input.HasCursorCapture);
        }

        private void RefreshVitals()
        {
            float maximum = Mathf.Max(1f, player.MaxStamina);
            float stamina = Mathf.Clamp(player.Stamina, 0f, maximum);
            float pending = Mathf.Clamp(player.KiPulsePending, 0f, maximum - stamina);
            float recoverable = Mathf.Clamp(player.KiPulseRecoverable, 0f, pending);
            float healthTrail = playerHealthTrail.Step(player.Health, player.MaxHealth, Time.time,
                healthDamageHoldSeconds, healthDamageShrinkSeconds);
            SetFill(healthDamageFill, healthTrail, healthDamageColor);
            SetFill(healthFill, player.Health / Mathf.Max(1f, player.MaxHealth), healthColor);
            SetFill(kiPendingFill, (stamina + pending) / maximum, pendingColor);
            SetFill(kiRecoverableFill, (stamina + recoverable) / maximum,
                player.IsPerfectKiPulseWindow ? kiPerfectColor : kiRecoverableColor);
            if (observedStaminaBorder != staminaBorder)
            {
                if (observedStaminaBorder != null) SetColor(observedStaminaBorder, staminaBorderColor);
                observedStaminaBorder = staminaBorder;
                if (staminaBorder != null) staminaBorderColor = staminaBorder.color;
            }
            float elapsed = Time.time - staminaWarningStartedAt;
            bool warning = !player.IsDead && elapsed >= 0f && elapsed < staminaWarningSeconds;
            float flash = warning ? .5f + .5f * Mathf.Cos(elapsed * staminaWarningFrequency * Mathf.PI * 2f) : 0f;
            SetFill(staminaFill, stamina / maximum, Color.Lerp(staminaColor, staminaWarningColor, flash));
            SetColor(staminaBorder, Color.Lerp(staminaBorderColor, staminaWarningColor, flash));
            SetColor(kiKeyGlow, player.IsPerfectKiPulseWindow ? kiPerfectColor :
                player.IsKiPulseWindowOpen ? kiRecoverableColor : kiIdleColor);
        }

        private void RefreshArts()
        {
            bool available = player.Profile != null && player.Profile.artsEnabled;
            SetActive(artsRoot, available);
            SetEnabled(artsRing, available);
            SetEnabled(artsSymbol, available);
            bool active = available && player.IsArtsActive;
            bool ready = available && player.IsArtsReady;
            float fill = active
                ? player.ArtsRemainingSeconds / Mathf.Max(.1f, player.ArtsDurationSeconds)
                : ready ? 1f : player.ArtsGauge / Mathf.Max(1f, player.ArtsGaugeMaximum);
            Color color = active || ready ? goldColor : artsBuildingColor;
            if (ready)
                color.a *= Mathf.Lerp(artsReadyMinimumAlpha, 1f,
                    .5f + .5f * Mathf.Sin(Time.unscaledTime * artsReadyPulseSpeed));
            SetFill(artsRing, fill, color);
            SetColor(artsSymbol, color);
            SetActive(artsTimer != null ? artsTimer.gameObject : null, active);
            if (!active)
            {
                displayedArtsTenth = int.MinValue;
                SetText(artsTimer, string.Empty);
                return;
            }
            // 只在显示到十分之一秒的读数改变时格式化，避免每个渲染帧分配字符串。
            int tenth = Mathf.CeilToInt(Mathf.Max(0f, player.ArtsRemainingSeconds) * 10f);
            if (tenth == displayedArtsTenth) return;
            displayedArtsTenth = tenth;
            SetText(artsTimer, (tenth * .1f).ToString("0.0", CultureInfo.InvariantCulture) + "s");
        }

        private void RefreshStances()
        {
            int stance = player.Actions != null && player.Actions.CurrentStance != null
                ? player.Actions.StanceIndex : -1;
            bool high = stance == highStanceIndex;
            bool middle = stance == middleStanceIndex;
            SetColor(highStanceFrame, high ? goldColor : inactiveFrameColor);
            SetColor(midStanceFrame, middle ? goldColor : inactiveFrameColor);
            SetColor(highStanceIcon, high ? selectedIconColor : inactiveIconColor);
            SetColor(midStanceIcon, middle ? selectedIconColor : inactiveIconColor);
            SetActive(highStanceMarker, high);
            SetActive(midStanceMarker, middle);
        }

        private void RefreshCharge()
        {
            bool charging = !player.IsDead && player.IsCharging;
            SetActive(chargeRoot, charging);
            SetFill(chargeFill, charging ? player.ChargeNormalizedTime : 0f,
                player.IsChargeCriticalWindowOpen ? kiPerfectColor : goldColor);
        }

        private void RefreshTarget()
        {
            SamuraiTargetLock targetLock = input != null ? input.targetLock : null;
            // Match the actual E arbitration: a valid backstab first, otherwise the existing
            // downed execution. The prompt may therefore follow a nearby unlocked enemy.
            SamuraiCombatant backstab = player.FindBackstabTarget();
            bool readyBackstab = backstab != null;
            SamuraiCombatant execution = readyBackstab ? null : player.FindExecutionTarget();
            bool readyExecution = execution != null && player.CanExecuteTarget(execution);
            SamuraiCombatant actionTarget = readyBackstab ? backstab : readyExecution ? execution : null;
            // Keep the actual paired receiver, including its lethal fall, when no user lock
            // exists or a different opponent was locked before the backstab began.
            SamuraiCombatant target = player.ActiveBackstabTarget;
            if (target == null) target = actionTarget;
            if (target == null) target = targetLock != null && targetLock.CurrentTarget != null
                ? targetLock.CurrentTarget : enemy != null ? enemy.actor : null;
            bool fixedElite = eliteRoot != null && SamuraiEliteHUDPresenter.IsElite(target);
            BindHealthTarget(target);
            float trail = target != null ? targetHealthTrail.Step(target.Health, target.MaxHealth, Time.time,
                healthDamageHoldSeconds, healthDamageShrinkSeconds) : 0f;
            bool showTarget = target != null && target.isActiveAndEnabled && (!target.IsDead || targetHealthTrail.IsVisible) &&
                (targetDisplayDistance <= 0f || (target.transform.position - player.transform.position).sqrMagnitude
                    <= targetDisplayDistance * targetDisplayDistance);
            if (showTarget)
                showTarget = Project(targetRoot, targetParent,
                    target.transform.position + Vector3.up * TargetHeight(target, targetHeight), targetScreenOffset);
            SetActive(targetRoot, showTarget);
            bool showWorldMeters = showTarget && !fixedElite;
            if (targetHealthFill != null) SetActive(targetHealthFill.transform.parent.gameObject, showWorldMeters);
            if (showWorldMeters)
            {
                SetFill(targetHealthDamageFill, trail, healthDamageColor);
                SetFill(targetHealthFill, target.Health / Mathf.Max(1f, target.MaxHealth), healthColor);
            }
            bool showToughness = showWorldMeters && !target.IsDead && target.Profile != null && target.Profile.toughnessEnabled;
            if (targetToughnessFill != null) SetActive(targetToughnessFill.transform.parent.gameObject, showToughness);
            if (showToughness)
            {
                float ratio = target.Toughness != null ? target.Toughness.Value / target.Toughness.Maximum : 1f;
                SetFill(targetToughnessFill, ratio, ratio <= .25f ? new Color(1f, .34f, .12f, 1f) : goldColor);
            }
            if (targetExecutionPrompt != null)
            {
                bool showPrompt = showTarget && actionTarget == target && input != null
                    && input.HasCursorCapture && Time.timeScale > 0f;
                SetActive(targetExecutionPrompt.gameObject, showPrompt);
                SetText(targetExecutionPrompt, showPrompt ? (readyBackstab ? "[E 背刺]" : "[E 处决]") : string.Empty);
            }
            SetEnabled(lockMarker, showTarget && targetLock != null && targetLock.CurrentTarget == target);
            SetColor(lockMarker, goldColor);

            bool showWarning = enemy != null && enemy.actor != null && enemy.IsTelegraphing &&
                enemy.actor.isActiveAndEnabled;
            if (showWarning)
                showWarning = Project(warningRoot, warningParent,
                    enemy.actor.transform.position + Vector3.up * TargetHeight(enemy.actor, warningHeight), warningScreenOffset);
            SetActive(warningRoot, showWarning);
            if (showWarning) SetFill(warningFill, enemy.TelegraphProgress, goldColor);
        }

        private void RefreshEliteTarget()
        {
            var target = elitePresenter.Resolve(player, input != null ? input.targetLock : null, enemy, targetDisplayDistance);
            bool visible = target != null;
            SetActive(eliteRoot, visible);
            if (!visible) return;
            SetText(eliteName, eliteDisplayName);
            SetFill(eliteHealthDamageFill, elitePresenter.HealthTrail(Time.time, healthDamageHoldSeconds, healthDamageShrinkSeconds), healthDamageColor);
            SetFill(eliteHealthFill, target.Health / Mathf.Max(1f, target.MaxHealth), healthColor);
            SetText(eliteHealthText, Mathf.CeilToInt(Mathf.Max(0f, target.Health)).ToString(CultureInfo.InvariantCulture)
                + " / " + Mathf.CeilToInt(target.MaxHealth).ToString(CultureInfo.InvariantCulture));
            bool toughness = target.Profile != null && target.Profile.toughnessEnabled;
            if (eliteToughnessFill != null) SetActive(eliteToughnessFill.transform.parent.gameObject, toughness);
            if (eliteToughnessText != null) SetActive(eliteToughnessText.gameObject, toughness);
            if (!toughness) return;
            float value = target.Toughness != null ? target.Toughness.Value : target.Profile.maxToughness;
            float maximum = target.Toughness != null ? target.Toughness.Maximum : target.Profile.maxToughness;
            float ratio = value / Mathf.Max(1f, maximum);
            SetFill(eliteToughnessFill, ratio, target.IsToughnessBroken ? new Color(1f, .34f, .12f, 1f) : goldColor);
            SetText(eliteToughnessText, target.IsToughnessBroken ? "韧性 · 失衡" : "韧性  "
                + Mathf.CeilToInt(Mathf.Max(0f, value)).ToString(CultureInfo.InvariantCulture) + " / "
                + Mathf.CeilToInt(maximum).ToString(CultureInfo.InvariantCulture));
        }

        private bool Project(RectTransform root, RectTransform parent, Vector3 worldPoint, Vector2 offset)
        {
            if (root == null || parent == null || canvas == null || worldCamera == null) return false;
            Vector3 viewport = worldCamera.WorldToViewportPoint(worldPoint);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) return false;
            Vector3 screenPoint = worldCamera.ViewportToScreenPoint(viewport);
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, uiCamera,
                out Vector2 localPoint)) return false;
            // 设置投影根的位置，不改子节点布局、尺寸、锚点或用户的局部偏移。
            root.position = parent.TransformPoint(localPoint + offset);
            return true;
        }
        private static float TargetHeight(SamuraiCombatant target, float minimum) => target != null && target.Motor != null
            ? Mathf.Max(minimum, target.Motor.height * Mathf.Abs(target.transform.lossyScale.y) + .15f) : minimum;

        private void CacheProjectionParents()
        {
            if (observedTargetRoot != targetRoot || targetRoot != null && targetParent != targetRoot.parent)
            {
                observedTargetRoot = targetRoot;
                targetParent = targetRoot != null ? targetRoot.parent as RectTransform : null;
            }
            if (observedWarningRoot != warningRoot || warningRoot != null && warningParent != warningRoot.parent)
            {
                observedWarningRoot = warningRoot;
                warningParent = warningRoot != null ? warningRoot.parent as RectTransform : null;
            }
        }

        private void BindPlayer()
        {
            if (observedPlayer == player) return;
            UnbindPlayer();
            observedPlayer = player;
            if (observedPlayer == null) return;
            observedPlayer.KiPulsePerformed += OnKiPulse;
            observedPlayer.FluxPerformed += OnFlux;
            observedPlayer.DeflectPerformed += OnDeflect;
            observedPlayer.DeflectReceived += OnDeflectReceived;
            observedPlayer.HitReceived += OnHitReceived;
            observedPlayer.StaminaInsufficient += OnStaminaInsufficient;
            observedPlayer.CombatReset += ResetVitalFeedback;
            observedPlayer.CombatReset += ClearFeedback;
            observedPlayer.Died += ClearFeedback;
        }

        private void UnbindPlayer()
        {
            if (observedPlayer != null)
            {
                observedPlayer.KiPulsePerformed -= OnKiPulse;
                observedPlayer.FluxPerformed -= OnFlux;
                observedPlayer.DeflectPerformed -= OnDeflect;
                observedPlayer.DeflectReceived -= OnDeflectReceived;
                observedPlayer.HitReceived -= OnHitReceived;
                observedPlayer.StaminaInsufficient -= OnStaminaInsufficient;
                observedPlayer.CombatReset -= ResetVitalFeedback;
                observedPlayer.CombatReset -= ClearFeedback;
                observedPlayer.Died -= ClearFeedback;
            }
            observedPlayer = null;
            ResetVitalFeedback();
            ClearFeedback();
        }

        private void BindHealthTarget(SamuraiCombatant target)
        {
            if (observedHealthTarget == target) return;
            if (observedHealthTarget != null) observedHealthTarget.CombatReset -= ResetTargetHealthTrail;
            observedHealthTarget = target;
            ResetTargetHealthTrail();
            if (observedHealthTarget != null) observedHealthTarget.CombatReset += ResetTargetHealthTrail;
        }

        private void ResetTargetHealthTrail() => targetHealthTrail.Clear();

        private void OnStaminaInsufficient() => staminaWarningStartedAt = Time.time;

        private void ResetVitalFeedback()
        {
            playerHealthTrail.Clear();
            targetHealthTrail.Clear();
            elitePresenter.ResetHealthTrail();
            staminaWarningStartedAt = float.NegativeInfinity;
            SetColor(staminaFill, staminaColor);
            SetColor(observedStaminaBorder, staminaBorderColor);
        }

        private void OnKiPulse(float amount, bool perfect)
        {
            ShowFeedback(perfect ? "完美残心" : "残心",
                perfect ? kiPerfectColor : kiRecoverableColor);
        }

        private void OnFlux(float amount, bool pulseWasPerfect) =>
            ShowFeedback(pulseWasPerfect ? "完美残心 · 流转" : "残心 · 流转",
                pulseWasPerfect ? kiPerfectColor : kiRecoverableColor);

        private void OnDeflect(float amount, Vector3 point, ISamuraiDamageReceiver source) =>
            ShowFeedback("弹反", goldColor);

        private void OnDeflectReceived(float seconds) =>
            ShowFeedback("攻击被弹反", new Color(1f, .38f, .16f, 1f));

        private void OnHitReceived(SamuraiHitResult result)
        {
            // 启用逐次弹反时，专门的 DeflectPerformed 事件已经提供一次反馈。
            if (player != null && player.Profile != null && player.Profile.deflectEnabled) return;
            if (result == SamuraiHitResult.PerfectGuard || result == SamuraiHitResult.Parried)
                ShowFeedback("弹反", goldColor);
        }

        private void ShowFeedback(string label, Color color)
        {
            feedbackStartedAt = player != null && player.Actions != null ? player.Actions.Clock : 0f;
            feedbackColor = color;
            SetText(briefFeedback, label);
            if (briefFeedback != null) briefFeedback.color = color;
        }

        private void RefreshFeedback()
        {
            if (briefFeedback == null) return;
            float clock = player.Actions != null ? player.Actions.Clock : 0f;
            float elapsed = clock - feedbackStartedAt;
            if (player.IsDead || elapsed < 0f || elapsed >= feedbackSeconds)
            {
                ClearFeedback();
                return;
            }
            Color color = feedbackColor;
            color.a *= 1f - Mathf.InverseLerp(feedbackSeconds * .55f, feedbackSeconds, elapsed);
            if (briefFeedback.color != color) briefFeedback.color = color;
        }

        private void ClearFeedback()
        {
            feedbackStartedAt = float.NegativeInfinity;
            SetText(briefFeedback, string.Empty);
        }

        private static void SetFill(Image image, float amount, Color color)
        {
            if (image == null) return;
            float value = Mathf.Clamp01(amount);
            if (!Mathf.Approximately(image.fillAmount, value)) image.fillAmount = value;
            SetColor(image, color);
        }

        private static void SetColor(Image image, Color color)
        {
            if (image != null && image.color != color) image.color = color;
        }

        private static void SetEnabled(Image image, bool enabled)
        {
            if (image != null && image.enabled != enabled) image.enabled = enabled;
        }

        private static void SetText(Text text, string value)
        {
            if (text != null && text.text != value) text.text = value;
        }

        private static void SetActive(RectTransform transform, bool active) =>
            SetActive(transform != null ? transform.gameObject : null, active);

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }
    }
}
