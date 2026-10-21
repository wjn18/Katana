using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 在环绕相机完成碰撞/锁定后叠加旋转震屏；蓄力冲刺按真实位移增加视野与镜头畸变。
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
        float shakeElapsed, shakeDuration, shakeAmplitude;
        float rushStrength, realPlanarSpeed;
        float rushFallElapsed, rushFallStart, heatElapsed, heatStrength;
        bool rushFalling, heatPlaying;
        int lastHeatAttackSerial = int.MinValue;
        Vector3 lastPlayerPosition, appliedShakeEuler;
        Quaternion appliedBaseRotation = Quaternion.identity;
        bool hasPlayerPosition, appliedShake;

        public bool IsShaking => shakeDuration > 0f && shakeElapsed < shakeDuration;
        public bool IsRushActive => rushStrength > FeedbackEpsilon;
        public float RushStrength => rushStrength;
        public float ActualPlanarSpeed => realPlanarSpeed;
        public float FieldOfViewOffset => appliedFovOffset;
        public float LensDistortionAmount => distortion != null ? distortion.intensity.value : 0f;
        public Vector3 ShakeEuler => appliedShakeEuler;
        public float ShakeRemaining => Mathf.Max(0f, shakeDuration - shakeElapsed);
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
                if (subscribedPlayer != null) subscribedPlayer.StateChanged += OnStateChanged;
            }
            if (subscribedRendering || !Application.isPlaying) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            subscribedRendering = true;
        }

        void Unsubscribe()
        {
            if (subscribedOrbit != null) subscribedOrbit.ViewReset -= ClearFeedback;
            if (subscribedPlayer != null) subscribedPlayer.StateChanged -= OnStateChanged;
            subscribedOrbit = null;
            subscribedPlayer = null;
            if (!subscribedRendering) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            subscribedRendering = false;
        }

        void OnStateChanged(string state) => ClearFeedback();

        /// <summary>由真实命中的反馈入口调用；蓄力比普通重击更重，不移动相机碰撞位置。</summary>
        public void PlayImpact(bool charged)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || view == null
                || orbit == null || !orbit.isActiveAndEnabled || player == null || !player.isActiveAndEnabled)
                return;
            float nextAmplitude = charged ? chargedShakeDegrees : heavyShakeDegrees;
            float nextDuration = charged ? chargedShakeSeconds : heavyShakeSeconds;
            shakeAmplitude = Mathf.Max(IsShaking ? shakeAmplitude : 0f,
                Finite(nextAmplitude) ? Mathf.Clamp(nextAmplitude, 0f, 3f) : 0f);
            shakeDuration = Finite(nextDuration) ? Mathf.Clamp(nextDuration, .05f, .5f) : .17f;
            shakeElapsed = 0f;
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
            if (IsShaking && !paused) shakeElapsed = Mathf.Min(shakeDuration, shakeElapsed + realDeltaSeconds);
            ApplyShake();
        }

        public void ClearFeedback()
        {
            ResetRush();
            ResetHeat();
            shakeElapsed = shakeDuration = shakeAmplitude = 0f;
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
            appliedFovOffset = 0f;
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
            float targetFov = Mathf.Clamp(baseFov + boost * rushStrength, 10f, 100f);
            appliedFovOffset = targetFov - baseFov;
            lastWrittenFov = targetFov;
            view.fieldOfView = targetFov;
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
            if (view != null && appliedFovOffset != 0f)
            {
                if (Mathf.Abs(view.fieldOfView - lastWrittenFov) <= .01f)
                    view.fieldOfView = baseFov;
                else baseFov = view.fieldOfView;
                lastWrittenFov = view.fieldOfView;
            }
            appliedFovOffset = 0f;
            if (distortion != null) { distortion.intensity.value = 0f; distortion.scale.value = 1f; }
            if (runtimeVolume != null) runtimeVolume.weight = 0f;
            RestorePostSettings();
        }

        void ResetHeat()
        {
            heatPlaying = false;
            heatElapsed = heatStrength = 0f;
            // 清理不释放这一击的触发锁；撞墙或顿帧恢复不能再次爆出起步波。
        }

        void ApplyShake()
        {
            if (!IsShaking || shakeAmplitude <= 0f) { RestoreRotation(); return; }
            float normalized = Mathf.Clamp01(shakeElapsed / Mathf.Max(.01f, shakeDuration));
            float envelope = (1f - normalized) * (1f - normalized);
            float time = shakeElapsed;
            appliedShakeEuler = new Vector3(
                -.8f * Mathf.Cos(time * 93f),
                .5f * Mathf.Sin(time * 111f + .7f),
                .38f * Mathf.Cos(time * 79f + .3f)) * (shakeAmplitude * envelope);
            appliedBaseRotation = orbit.UnshakenRotation;
            view.transform.rotation = appliedBaseRotation * Quaternion.Euler(appliedShakeEuler);
            appliedShake = true;
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
