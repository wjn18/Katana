using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 在环绕相机完成碰撞/锁定后叠加旋转震屏；蓄力冲刺增加视野，精英处决按接触时序推拉视野。
    /// 不改变相机位置、战斗计时或全局Volume资产。
    /// </summary>
    [DefaultExecutionOrder(500), DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class SamuraiCombatCameraFeedback : MonoBehaviour
    {
        [SerializeField] SamuraiOrbitCamera orbit;
        [SerializeField] SamuraiCombatant player;
        [Header("命中旋转震屏")]
        [SerializeField, Range(.05f, .4f)] float heavyShakeSeconds = .17f;
        [SerializeField, Range(.05f, .5f)] float chargedShakeSeconds = .27f;
        [SerializeField, Range(0f, 3f)] float heavyShakeDegrees = .8f;
        [SerializeField, Range(0f, 3f)] float chargedShakeDegrees = 1.35f;
        [Header("玩家受伤震屏（按实际生命伤害）")]
        [SerializeField, Min(0f)] float mediumDamageThreshold = 15f;
        [SerializeField, Min(0f)] float highDamageThreshold = 35f;
        [SerializeField, Range(0f, 3f)] float mediumDamageShakeDegrees = .65f;
        [SerializeField, Range(0f, 3f)] float highDamageShakeDegrees = 2.1f;
        [SerializeField, Range(0f, .6f)] float mediumDamageShakeSeconds = .16f;
        [SerializeField, Range(0f, .6f)] float highDamageShakeSeconds = .30f;
        [Header("格挡 / 弹反震屏（复用角色防御轻中重阈值）")]
        [Tooltip("X/Y/Z为轻/中/重弹反震幅。普通格挡按下方倍率减弱；0为关闭该档。")]
        [SerializeField] Vector3 parryShakeDegrees = new Vector3(.4f, .8f, 1.2f);
        [Tooltip("X/Y/Z为轻/中/重弹反持续秒数。")]
        [SerializeField] Vector3 parryShakeSeconds = new Vector3(.10f, .13f, .16f);
        [SerializeField, Range(0f, 1f)] float guardShakeMultiplier = .55f;
        [SerializeField, Range(0f, 1f)] float guardShakeDurationMultiplier = .8f;
        [Header("蓄力冲刺镜头")]
        [SerializeField, Range(0f, 24f)] float chargedFovBoost = 14f;
        [SerializeField, Range(-.45f, 0f)] float chargedLensDistortion = -.22f;
        [SerializeField, Range(.01f, .2f)] float rushRiseSeconds = .055f;
        [SerializeField, Range(.03f, .25f)] float rushFallSeconds = .10f;
        [SerializeField, Min(0f)] float minimumRushSpeed = .5f;
        [Header("突进起步热扭曲")]
        [SerializeField, Range(.08f, .4f)] float heatPulseSeconds = .24f;

        const int RuntimeVolumeLayer = 31;
        const float FeedbackEpsilon = .0001f;
        const float ExecutionFovReturnSeconds = .15f;
        Camera view;
        SamuraiOrbitCamera subscribedOrbit;
        SamuraiCombatant subscribedPlayer;
        UniversalAdditionalCameraData cameraData;
        GameObject volumeObject;
        Volume runtimeVolume;
        VolumeProfile runtimeProfile;
        LensDistortion distortion;
        bool ownsCameraData, ownsPostSettings, subscribedRendering;
        bool previousPostProcessing;
        LayerMask previousVolumeMask;
        float baseFov, lastWrittenFov, appliedFovOffset;
        float rushFovOffset, executionFovOffset, executionFovReturnStart, executionFovReturnElapsed;
        bool executionFovReturning;
        float shakeElapsed, shakeDuration, shakeAmplitude;
        bool executionShake;
        public int ExecutionShakeCount { get; private set; }
        public SamuraiCombatant TargetPlayer => player;
        float receivedShakeElapsed, receivedShakeDuration, receivedShakeAmplitude;
        float guardShakeElapsed, guardShakeDuration, guardShakeAmplitude;
        float rushStrength, realPlanarSpeed;
        float rushFallElapsed, rushFallStart, heatElapsed, heatStrength;
        bool rushFalling, heatPlaying;
        int lastHeatAttackSerial = int.MinValue;
        Vector3 lastPlayerPosition, appliedShakeEuler;
        Quaternion appliedBaseRotation = Quaternion.identity;
        bool hasPlayerPosition, appliedShake;

        bool IsImpactShaking => shakeDuration > 0f && shakeElapsed < shakeDuration;
        public bool IsReceivedDamageShaking => receivedShakeDuration > 0f && receivedShakeElapsed < receivedShakeDuration;
        public bool IsGuardShaking => guardShakeDuration > 0f && guardShakeElapsed < guardShakeDuration;
        public bool IsShaking => IsImpactShaking || IsReceivedDamageShaking || IsGuardShaking;
        public bool IsRushActive => rushStrength > FeedbackEpsilon;
        public float RushStrength => rushStrength;
        public float ActualPlanarSpeed => realPlanarSpeed;
        public float FieldOfViewOffset => appliedFovOffset;
        public float ExecutionFovOffset => executionFovOffset;
        public float LensDistortionAmount => distortion != null ? distortion.intensity.value : 0f;
        public Vector3 ShakeEuler => appliedShakeEuler;
        public float ShakeRemaining => Mathf.Max(Mathf.Max(Mathf.Max(0f, shakeDuration - shakeElapsed), ReceivedDamageShakeRemaining), GuardShakeRemaining);
        public float GuardShakeRemaining => Mathf.Max(0f, guardShakeDuration - guardShakeElapsed);
        public int GuardShakeCount { get; private set; }
        public int LastGuardShakeTier { get; private set; }
        public float LastGuardShakeDegrees { get; private set; }
        public float LastGuardShakeSeconds { get; private set; }
        public float LastGuardStaminaCost { get; private set; }
        public bool LastGuardWasPerfect { get; private set; }
        public float ReceivedDamageShakeRemaining => Mathf.Max(0f, receivedShakeDuration - receivedShakeElapsed);
        public float MediumDamageThreshold => Finite(mediumDamageThreshold) ? Mathf.Max(0f, mediumDamageThreshold) : 15f;
        public float HighDamageThreshold => Mathf.Max(MediumDamageThreshold,
            Finite(highDamageThreshold) ? Mathf.Max(0f, highDamageThreshold) : 35f);
        public int ReceivedDamageShakeCount { get; private set; }
        public float LastReceivedHealthDamage { get; private set; }
        /// <summary>最近一次伤害分级：0不震、1中伤、2高伤。</summary>
        public int LastReceivedShakeTier { get; private set; }
        public float LastReceivedShakeDegrees { get; private set; }
        public float LastReceivedShakeSeconds { get; private set; }
        public bool HasRuntimeVolume => runtimeVolume != null;
        public float HeatDistortionStrength => heatStrength;
        public float HeatDistortionTime => heatPlaying ? heatElapsed : 0f;
        public Vector2 HeatDistortionCenter => new Vector2(.5f, .5f);

        public void Configure(SamuraiOrbitCamera targetOrbit, SamuraiCombatant targetPlayer)
        {
            // 重复接线不重建资源、不重置Inspector中的美术参数。
            if (orbit == targetOrbit && player == targetPlayer && view != null)
            {
                if (isActiveAndEnabled) Subscribe();
                return;
            }
            Unsubscribe();
            ReleaseResources();
            orbit = targetOrbit;
            player = targetPlayer;
            lastHeatAttackSerial = int.MinValue;
            view = orbit != null ? orbit.GetComponent<Camera>() : GetComponent<Camera>();
            CaptureBaseFov();
            CapturePlayerPosition();
            if (isActiveAndEnabled) Subscribe();
        }

        void OnEnable()
        {
            if (orbit == null) orbit = GetComponent<SamuraiOrbitCamera>();
            if (player == null && orbit != null && orbit.target != null)
                player = orbit.target.GetComponent<SamuraiCombatant>();
            view = orbit != null ? orbit.GetComponent<Camera>() : GetComponent<Camera>();
            CaptureBaseFov();
            CapturePlayerPosition();
            Subscribe();
        }

        void OnDisable() { Unsubscribe(); ReleaseResources(); }
        void OnDestroy() { Unsubscribe(); ReleaseResources(); }
        void LateUpdate() => SampleNow(Time.deltaTime, Time.unscaledDeltaTime);

        void Subscribe()
        {
            if (subscribedOrbit != orbit || subscribedPlayer != player)
            {
                Unsubscribe();
                subscribedOrbit = orbit;
                subscribedPlayer = player;
                if (subscribedOrbit != null) subscribedOrbit.ViewReset += ClearFeedback;
                if (subscribedPlayer != null)
                {
                    subscribedPlayer.StateChanged += OnStateChanged;
                    subscribedPlayer.DamageReceived += OnDamageReceived;
                    subscribedPlayer.GuardContacted += OnGuardContacted;
                    subscribedPlayer.CombatReset += ClearFeedback;
                }
            }
            if (subscribedRendering || !Application.isPlaying) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            subscribedRendering = true;
        }

        void Unsubscribe()
        {
            if (subscribedOrbit != null) subscribedOrbit.ViewReset -= ClearFeedback;
            if (subscribedPlayer != null)
            {
                subscribedPlayer.StateChanged -= OnStateChanged;
                subscribedPlayer.DamageReceived -= OnDamageReceived;
                subscribedPlayer.GuardContacted -= OnGuardContacted;
                subscribedPlayer.CombatReset -= ClearFeedback;
            }
            subscribedOrbit = null;
            subscribedPlayer = null;
            if (!subscribedRendering) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            subscribedRendering = false;
        }

        void OnStateChanged(string state)
        {
            ResetRush();
            ResetHeat();
            shakeElapsed = shakeDuration = shakeAmplitude = 0f;
            // 反应结束回防不能截断防御余震；复位由CombatReset单独清理。
            if (!IsReceivedDamageShaking && !IsGuardShaking) RestoreRotation();
            realPlanarSpeed = 0f;
            CapturePlayerPosition();
        }

        void OnDamageReceived(SamuraiDamageFeedback feedback)
        {
            if (!CanPlayShake() || !Finite(feedback.HealthDamage) || feedback.HealthDamage <= 0f) return;
            LastReceivedHealthDamage = feedback.HealthDamage;
            LastReceivedShakeTier = feedback.HealthDamage < MediumDamageThreshold ? 0
                : feedback.HealthDamage < HighDamageThreshold ? 1 : 2;
            LastReceivedShakeDegrees = LastReceivedShakeSeconds = 0f;
            if (LastReceivedShakeTier == 0) return;
            float degrees = LastReceivedShakeTier == 2 ? highDamageShakeDegrees : mediumDamageShakeDegrees;
            float seconds = LastReceivedShakeTier == 2 ? highDamageShakeSeconds : mediumDamageShakeSeconds;
            LastReceivedShakeDegrees = Finite(degrees) ? Mathf.Clamp(degrees, 0f, 3f) : 0f;
            LastReceivedShakeSeconds = Finite(seconds) ? Mathf.Clamp(seconds, 0f, .6f) : 0f;
            if (LastReceivedShakeDegrees <= 0f || LastReceivedShakeSeconds <= 0f) return;
            // 连续受伤只保留正在衰减的强度，不把旧的大震幅重新放大，也不缩短余震。
            float remainingEnvelope = 1f - Mathf.Clamp01(receivedShakeElapsed / Mathf.Max(.01f, receivedShakeDuration));
            float remainingAmplitude = IsReceivedDamageShaking
                ? receivedShakeAmplitude * remainingEnvelope * remainingEnvelope : 0f;
            receivedShakeAmplitude = Mathf.Max(remainingAmplitude, LastReceivedShakeDegrees);
            receivedShakeDuration = Mathf.Max(ReceivedDamageShakeRemaining, LastReceivedShakeSeconds);
            receivedShakeElapsed = 0f;
            ReceivedDamageShakeCount++;
        }

        void OnGuardContacted(SamuraiGuardContact contact)
        {
            if (!CanPlayShake() || player.Profile == null) return;
            bool perfect = contact.Result == SamuraiHitResult.PerfectGuard;
            if (!perfect && contact.Result != SamuraiHitResult.Blocked) return;
            // 普通格挡用实际消耗，弹反用来袭削精力值；恢复量/剩余精力不决定震幅。
            float cost = perfect ? contact.IncomingStaminaDamage : contact.ConsumedStamina;
            LastGuardWasPerfect = perfect;
            LastGuardStaminaCost = Finite(cost) ? Mathf.Max(0f, cost) : 0f;
            LastGuardShakeDegrees = LastGuardShakeSeconds = 0f;
            LastGuardShakeTier = -1;
            if (!Finite(cost) || cost <= 0f) return;
            int tier = SamuraiGuardReactionSelector.StrengthTier(player.Profile, cost);
            LastGuardShakeTier = tier;
            float degrees = parryShakeDegrees[tier], seconds = parryShakeSeconds[tier];
            degrees = Finite(degrees) ? Mathf.Clamp(degrees, 0f, 3f) : 0f;
            seconds = Finite(seconds) ? Mathf.Clamp(seconds, 0f, .6f) : 0f;
            if (!perfect)
            {
                degrees *= Finite(guardShakeMultiplier) ? Mathf.Clamp01(guardShakeMultiplier) : 0f;
                seconds *= Finite(guardShakeDurationMultiplier) ? Mathf.Clamp01(guardShakeDurationMultiplier) : 0f;
            }
            LastGuardShakeDegrees = degrees;
            LastGuardShakeSeconds = seconds;
            if (degrees <= 0f || seconds <= 0f) return;
            // 连续防御仅保留衰减后的余量，既不叠加振幅，也不让后续轻击重新激活旧重震。
            float envelope = 1f - Mathf.Clamp01(guardShakeElapsed / Mathf.Max(.01f, guardShakeDuration));
            float remaining = IsGuardShaking ? guardShakeAmplitude * envelope * envelope : 0f;
            guardShakeAmplitude = Mathf.Max(remaining, degrees);
            guardShakeDuration = Mathf.Max(GuardShakeRemaining, seconds);
            guardShakeElapsed = 0f;
            GuardShakeCount++;
        }

        bool CanPlayShake() => Application.isPlaying && isActiveAndEnabled && view != null
            && orbit != null && orbit.isActiveAndEnabled && player != null && player.isActiveAndEnabled;

        /// <summary>由真实命中的反馈入口调用；蓄力比普通重击更重，不移动相机碰撞位置。</summary>
        public void PlayImpact(bool charged)
        {
            executionShake = false;
            if (!CanPlayShake()) return;
            float nextAmplitude = charged ? chargedShakeDegrees : heavyShakeDegrees;
            float nextDuration = charged ? chargedShakeSeconds : heavyShakeSeconds;
            shakeAmplitude = Mathf.Max(IsImpactShaking ? shakeAmplitude : 0f,
                Finite(nextAmplitude) ? Mathf.Clamp(nextAmplitude, 0f, 3f) : 0f);
            shakeDuration = Finite(nextDuration) ? Mathf.Clamp(nextDuration, .05f, .5f) : .17f;
            shakeElapsed = 0f;
        }
        public void PlayExecutionImpact(float degrees,float seconds)
        {
            if(!CanPlayShake())return;
            executionShake=true;shakeElapsed=0;shakeDuration=Mathf.Clamp(seconds,.05f,.4f);shakeAmplitude=Mathf.Clamp(degrees,0,8f);ExecutionShakeCount++;
        }

        /// <summary>正常LateUpdate路径；独立诊断时也可在Orbit求值后传入缩放/真实时间。</summary>
        public void SampleNow(float scaledDeltaSeconds, float realDeltaSeconds)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || view == null || !view.enabled
                || orbit == null || !orbit.isActiveAndEnabled || !orbit.HasSolvedPose
                || player == null || !player.isActiveAndEnabled
                || !Finite(scaledDeltaSeconds) || !Finite(realDeltaSeconds)
                || scaledDeltaSeconds < 0f || realDeltaSeconds < 0f)
            { ClearFeedback(); return; }
            if (subscribedOrbit != orbit || subscribedPlayer != player)
            {
                ClearFeedback();
                Subscribe();
            }
            ObserveExternalFov();
            Vector3 position = player.transform.position;
            Vector3 displacement = hasPlayerPosition ? position - lastPlayerPosition : Vector3.zero;
            displacement.y = 0f;
            lastPlayerPosition = position;
            hasPlayerPosition = true;
            bool teleported = displacement.sqrMagnitude > 2.25f;
            realPlanarSpeed = !teleported && scaledDeltaSeconds > .000001f
                ? displacement.magnitude / scaledDeltaSeconds : 0f;
            if (teleported)
            {
                ClearFeedback();
                return;
            }

            var action = player.Actions != null ? player.Actions.ActiveAction : null;
            bool charged = player.IsAttacking && player.Profile != null && action != null
                && action == player.Profile.charged;
            float progress = player.StateNormalizedTime;
            bool inRush = charged && progress >= action.stepStart && progress < action.stepEnd;
            float minSpeed = Finite(minimumRushSpeed) ? Mathf.Max(0f, minimumRushSpeed) : .5f;
            bool paused = scaledDeltaSeconds <= 0f || Time.timeScale <= 0f;
            SampleExecutionFov(scaledDeltaSeconds, paused);
            bool interrupted = paused || player.IsHitStopped || !charged;
            bool startedHeat = false;
            if (inRush && !paused && !player.IsHitStopped && realPlanarSpeed > minSpeed)
            {
                rushFalling = false;
                // StateChanged 比 AttackSerial 递增更早；只在真正开始移动时锁定这一击。
                int serial = player.Actions.AttackSerial;
                if (serial != lastHeatAttackSerial)
                {
                    lastHeatAttackSerial = serial;
                    heatElapsed = 0f;
                    heatStrength = 1f;
                    heatPlaying = startedHeat = true;
                }
                float expectedPeak = Mathf.Max(minSpeed + 1f,
                    1.5f * Mathf.Max(0f, action.stepDistance)
                    / Mathf.Max(.02f, action.Duration * (action.stepEnd - action.stepStart)));
                float desired = Mathf.InverseLerp(minSpeed, expectedPeak * .85f, realPlanarSpeed);
                float rise = Finite(rushRiseSeconds) ? Mathf.Clamp(rushRiseSeconds, .01f, .2f) : .055f;
                rushStrength = Mathf.Lerp(rushStrength, desired, 1f - Mathf.Exp(-realDeltaSeconds / rise));
                ApplyRush();
            }
            else if (!interrupted && progress >= action.stepEnd && IsRushActive)
            {
                // 正常突进结束短暂收回视野，避免镜头一帧跳回；中断仍立即复原。
                if (!rushFalling)
                {
                    rushFalling = true;
                    rushFallStart = rushStrength;
                    rushFallElapsed = 0f;
                }
                else rushFallElapsed += realDeltaSeconds;
                float fall = Finite(rushFallSeconds) ? Mathf.Clamp(rushFallSeconds, .03f, .25f) : .10f;
                rushStrength = rushFallStart * (1f - Mathf.SmoothStep(0f, 1f, rushFallElapsed / fall));
                if (IsRushActive) ApplyRush();
                else ResetRush();
            }
            else
            {
                // 墙前静止、局部顿帧、真正暂停与取消立即复原。
                ResetRush();
            }
            if (interrupted || (inRush && realPlanarSpeed <= minSpeed)) ResetHeat();
            else if (heatPlaying && !startedHeat)
            {
                heatElapsed += realDeltaSeconds;
                float duration = Finite(heatPulseSeconds) ? Mathf.Clamp(heatPulseSeconds, .08f, .4f) : .24f;
                float remaining = 1f - Mathf.Clamp01(heatElapsed / duration);
                heatStrength = remaining * remaining;
                if (heatElapsed >= duration) ResetHeat();
            }

            // 局部顿帧期间仍有真实时间震屏；真正暂停只保持当前姿态，不推进震动时间。
            if (!paused)
            {
                if (IsImpactShaking) shakeElapsed = Mathf.Min(shakeDuration, shakeElapsed + realDeltaSeconds);
                if (IsReceivedDamageShaking)
                    receivedShakeElapsed = Mathf.Min(receivedShakeDuration, receivedShakeElapsed + realDeltaSeconds);
                if (IsGuardShaking) guardShakeElapsed = Mathf.Min(guardShakeDuration, guardShakeElapsed + realDeltaSeconds);
            }
            ApplyShake();
        }

        public void ClearFeedback()
        {
            ResetExecutionFov();
            ResetRush();
            ResetHeat();
            shakeElapsed = shakeDuration = shakeAmplitude = 0f;
            receivedShakeElapsed = receivedShakeDuration = receivedShakeAmplitude = 0f;
            guardShakeElapsed = guardShakeDuration = guardShakeAmplitude = 0f;
            executionShake = false;
            appliedShakeEuler = Vector3.zero;
            RestoreRotation();
            realPlanarSpeed = 0f;
            CapturePlayerPosition();
        }

        void CapturePlayerPosition()
        {
            hasPlayerPosition = player != null;
            if (hasPlayerPosition) lastPlayerPosition = player.transform.position;
        }

        void CaptureBaseFov()
        {
            if (view == null) return;
            baseFov = lastWrittenFov = view.fieldOfView;
            appliedFovOffset = rushFovOffset = 0f;
            ResetExecutionFov();
        }

        void ObserveExternalFov()
        {
            if (view == null) return;
            if (appliedFovOffset == 0f || Mathf.Abs(view.fieldOfView - lastWrittenFov) > .01f)
            {
                baseFov = view.fieldOfView;
                appliedFovOffset = 0f;
            }
        }

        void ApplyRush()
        {
            if (view == null || view.orthographic) { ResetRush(); return; }
            float boost = Finite(chargedFovBoost) ? Mathf.Clamp(chargedFovBoost, 0f, 24f) : 14f;
            rushFovOffset = boost * rushStrength;
            ApplyFov();
            if (!EnsureVolume()) return;
            AcquirePostSettings();
            float amount = Finite(chargedLensDistortion) ? Mathf.Clamp(chargedLensDistortion, -.45f, 0f) : -.22f;
            distortion.intensity.value = amount * rushStrength;
            distortion.scale.value = 1f + .025f * rushStrength;
        }

        void ResetRush()
        {
            rushStrength = 0f;
            rushFalling = false;
            rushFallElapsed = rushFallStart = 0f;
            rushFovOffset = 0f;
            ApplyFov();
            if (distortion != null) { distortion.intensity.value = 0f; distortion.scale.value = 1f; }
            if (runtimeVolume != null) runtimeVolume.weight = 0f;
            RestorePostSettings();
        }

        void SampleExecutionFov(float deltaSeconds, bool paused)
        {
            if (view.orthographic) { ResetExecutionFov(); return; }
            // 处决和接触停顿共用实际播放时钟；真正暂停保持当前视野。
            if (paused) return;
            var pair = player.IsExecuting ? player.ActiveExecutionPair : null;
            float push = pair != null && Finite(pair.cameraPushFov) ? Mathf.Clamp(pair.cameraPushFov, 0f, 16f) : 0f;
            float recoil = pair != null && Finite(pair.cameraRecoilFov) ? Mathf.Clamp(pair.cameraRecoilFov, 0f, 8f) : 0f;
            if (pair != null && (push > 0f || recoil > 0f))
            {
                float elapsed = player.ExecutionPlaybackElapsed;
                float impact = pair.PlaybackSecondsAt(pair.strikeSeconds);
                float duration = pair.PlaybackDuration;
                if (Finite(elapsed) && Finite(impact) && Finite(duration) && duration > 0f)
                {
                    executionFovReturning = false;
                    executionFovReturnElapsed = executionFovReturnStart = 0f;
                    float sinceImpact = elapsed - impact;
                    if (sinceImpact <= 0f)
                        executionFovOffset = -push * Mathf.SmoothStep(0f, 1f, elapsed / Mathf.Max(.01f, impact));
                    else if (sinceImpact < .045f)
                        executionFovOffset = Mathf.Lerp(-push, recoil, Mathf.SmoothStep(0f, 1f, sinceImpact / .045f));
                    else if (sinceImpact < .16f)
                        executionFovOffset = Mathf.Lerp(recoil, -push * .35f,
                            Mathf.SmoothStep(0f, 1f, (sinceImpact - .045f) / .115f));
                    else
                        executionFovOffset = -push * .35f * (1f - Mathf.SmoothStep(0f, 1f,
                            (sinceImpact - .16f) / Mathf.Max(.05f, duration - impact - .28f)));
                    return;
                }
            }
            // 正常收尾或被打断时短促平滑收回；复位、停用与换玩家由ClearFeedback立即清理。
            if (Mathf.Abs(executionFovOffset) <= FeedbackEpsilon) { ResetExecutionFov(); return; }
            if (!executionFovReturning)
            {
                executionFovReturning = true;
                executionFovReturnStart = executionFovOffset;
                executionFovReturnElapsed = 0f;
            }
            executionFovReturnElapsed += deltaSeconds;
            executionFovOffset = executionFovReturnStart * (1f - Mathf.SmoothStep(0f, 1f,
                executionFovReturnElapsed / ExecutionFovReturnSeconds));
            if (executionFovReturnElapsed >= ExecutionFovReturnSeconds) ResetExecutionFov();
        }

        void ResetExecutionFov()
        {
            executionFovOffset = executionFovReturnStart = executionFovReturnElapsed = 0f;
            executionFovReturning = false;
        }

        void ApplyFov()
        {
            if (view == null) return;
            // 同一基础FOV上合并两种表现，ResetRush不能抹掉处决镜头，也不能把旧偏移累计进基础值。
            ObserveExternalFov();
            float offset = view.orthographic ? 0f : rushFovOffset + executionFovOffset;
            float target = Mathf.Abs(offset) > FeedbackEpsilon ? Mathf.Clamp(baseFov + offset, 10f, 100f) : baseFov;
            appliedFovOffset = target - baseFov;
            lastWrittenFov = target;
            view.fieldOfView = target;
        }

        void ResetHeat()
        {
            heatPlaying = false;
            heatElapsed = heatStrength = 0f;
            // 清理不释放这一击的触发锁；撞墙或顿帧恢复不能再次爆出起步波。
        }

        void ApplyShake()
        {
            if (!IsShaking) { RestoreRotation(); return; }
            appliedShakeEuler = Vector3.ClampMagnitude(
                ShakeSample(shakeElapsed, shakeDuration, shakeAmplitude)
                + ShakeSample(receivedShakeElapsed, receivedShakeDuration, receivedShakeAmplitude)
                + ShakeSample(guardShakeElapsed, guardShakeDuration, guardShakeAmplitude), executionShake && IsImpactShaking ? 6f : 3f);
            appliedBaseRotation = orbit.UnshakenRotation;
            view.transform.rotation = appliedBaseRotation * Quaternion.Euler(appliedShakeEuler);
            appliedShake = true;
        }

        static Vector3 ShakeSample(float elapsed, float duration, float amplitude)
        {
            if (duration <= 0f || elapsed >= duration || amplitude <= 0f) return Vector3.zero;
            float normalized = Mathf.Clamp01(elapsed / Mathf.Max(.01f, duration));
            float envelope = (1f - normalized) * (1f - normalized);
            return new Vector3(
                -.8f * Mathf.Cos(elapsed * 93f),
                .5f * Mathf.Sin(elapsed * 111f + .7f),
                .38f * Mathf.Cos(elapsed * 79f + .3f)) * (amplitude * envelope);
        }

        void RestoreRotation()
        {
            if (appliedShake && view != null)
                view.transform.rotation = orbit != null && orbit.HasSolvedPose
                    ? orbit.UnshakenRotation : appliedBaseRotation;
            appliedShake = false;
            appliedShakeEuler = Vector3.zero;
        }

        bool EnsureVolume()
        {
            if (runtimeVolume != null && distortion != null) return true;
            if (!Application.isPlaying || view == null
                || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                return false;
            if (cameraData == null)
            {
                cameraData = view.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData == null)
                {
                    cameraData = view.gameObject.AddComponent<UniversalAdditionalCameraData>();
                    cameraData.hideFlags = HideFlags.DontSave;
                    ownsCameraData = true;
                }
            }
            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "Samurai charge rush profile (runtime)";
            runtimeProfile.hideFlags = HideFlags.DontSave;
            distortion = runtimeProfile.Add<LensDistortion>(true);
            distortion.hideFlags = HideFlags.DontSave;
            distortion.intensity.value = 0f;
            distortion.xMultiplier.value = 1f;
            distortion.yMultiplier.value = .85f;
            distortion.center.value = new Vector2(.5f, .5f);
            distortion.scale.value = 1f;
            volumeObject = new GameObject("Samurai charge rush volume (runtime)");
            volumeObject.hideFlags = HideFlags.DontSave;
            volumeObject.layer = RuntimeVolumeLayer;
            volumeObject.transform.SetParent(view.transform, false);
            runtimeVolume = volumeObject.AddComponent<Volume>();
            runtimeVolume.isGlobal = true;
            runtimeVolume.priority = 10000f;
            runtimeVolume.weight = 0f;
            runtimeVolume.sharedProfile = runtimeProfile;
            return true;
        }

        void AcquirePostSettings()
        {
            if (cameraData == null || ownsPostSettings) return;
            previousPostProcessing = cameraData.renderPostProcessing;
            previousVolumeMask = cameraData.volumeLayerMask;
            cameraData.renderPostProcessing = true;
            cameraData.volumeLayerMask = previousVolumeMask.value | (1 << RuntimeVolumeLayer);
            ownsPostSettings = true;
        }

        void RestorePostSettings()
        {
            if (!ownsPostSettings) return;
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = previousPostProcessing;
                cameraData.volumeLayerMask = previousVolumeMask;
            }
            ownsPostSettings = false;
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
        {
            if (runtimeVolume == null) return;
            runtimeVolume.weight = renderingCamera == view && IsRushActive && isActiveAndEnabled ? 1f : 0f;
            // 本地URP在此回调之后更新Volume；脚本更新模式也显式重采样当前相机。
            if (renderingCamera == view && cameraData != null && VolumeManager.instance.isInitialized
                && !cameraData.requiresVolumeFrameworkUpdate)
                view.UpdateVolumeStack(cameraData);
        }

        void OnEndCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
        {
            if (runtimeVolume != null) runtimeVolume.weight = 0f;
        }

        void ReleaseResources()
        {
            ClearFeedback();
            if (runtimeVolume != null) runtimeVolume.sharedProfile = null;
            if (volumeObject != null)
            {
                volumeObject.SetActive(false);
                DestroyOwned(volumeObject);
            }
            if (runtimeProfile != null)
            {
                foreach (var component in runtimeProfile.components)
                    if (component != null) DestroyOwned(component);
                runtimeProfile.components.Clear();
                DestroyOwned(runtimeProfile);
            }
            if (ownsCameraData && cameraData != null) DestroyOwned(cameraData);
            ownsCameraData = false;
            cameraData = null;
            runtimeProfile = null;
            runtimeVolume = null;
            distortion = null;
            volumeObject = null;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
