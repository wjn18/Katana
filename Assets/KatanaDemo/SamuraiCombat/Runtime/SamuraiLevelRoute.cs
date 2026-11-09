using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OCDesign.Samurai
{
    /// <summary>山路钥匙、侧门与内闩桥门；只保存路线解锁状态，不保存完整世界进度。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]
    public sealed class SamuraiLevelRoute : MonoBehaviour
    {
        public enum RouteTarget { Key, SideGate, BridgeGate }

        public SamuraiCombatant player;
        public SamuraiPlayerInput input;
        public Font promptFont;
        public Transform keyAnchor;
        public GameObject keyVisual;
        [Tooltip("射线仅忽略钥匙自身；为空时使用 keyVisual，再回退 keyAnchor。")]
        public Transform keyRoot;
        public Transform sideGateRoot;
        public Transform bridgeGateRoot;
        public Transform[] sideGateHinges = new Transform[0];
        public Transform[] bridgeGateHinges = new Transform[0];
        public float[] sideGateOpenAngles = { -100f, 100f };
        public float[] bridgeGateOpenAngles = { -100f, 100f };
        public Collider sideGateBlocker;
        public Collider bridgeGateBlocker;
        public GameObject sideLockVisual;
        public GameObject bridgeLatchVisual;
        [Tooltip("世界空间中指向精英庭院内侧的水平向量。")]
        public Vector3 bridgeInsideDirection = Vector3.left;
        [Min(0f)] public float insideThreshold = .65f;
        [Min(.25f)] public float interactionRadius = 3f;
        [Min(.25f)] public float keyRadius = 1.8f;
        [Min(.05f)] public float openSeconds = .9f;
        public LayerMask visibilityMask = ~0;
        public string persistenceKey = "OCDesign.Samurai.LevelRoute.v1";
        public string routeId = "summit-loop-v1";

        public bool HasKey { get; private set; }
        public bool SideGateOpen { get; private set; }
        public bool BridgeGateOpen { get; private set; }
        public float SideGateProgress => sideProgress;
        public float BridgeGateProgress => bridgeProgress;

        [Serializable]
        sealed class SavedRoute
        {
            public int version;
            public string routeId;
            public string scenePath;
            public bool HasKey;
            public bool SideGateOpen;
            public bool BridgeGateOpen;
        }

        readonly RaycastHit[] rayHits = new RaycastHit[48];
        Transform[] cachedSideHinges, cachedBridgeHinges;
        Quaternion[] sideClosed, bridgeClosed;
        float sideProgress, bridgeProgress, nextScan, feedbackUntil;
        bool hasSelection, chinese;
        RouteTarget selected;
        GameObject ownedCanvas;
        Text promptLabel;
        string feedback;
        const float ScanInterval = .2f;

        void Awake()
        {
            EnsureGateCaches();
            if (!TryRestore()) ApplyVisualState(true);
        }

        void Start() => ResolveReferences();
        void OnEnable() { nextScan = 0f; }

        void Update()
        {
            TickOpening(Time.deltaTime);
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + ScanInterval;
                ResolveReferences();
                SelectNearestTarget();
            }
            bool visible = Application.isPlaying && input != null && input.HasCursorCapture
                && Time.timeScale > 0f && player != null && !player.IsDead;
            if (visible && Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            {
                SelectNearestTarget();
                if (hasSelection) TryInteract(selected);
                SelectNearestTarget();
            }
            RefreshPrompt(visible);
        }

        void ResolveReferences()
        {
            if (input == null && player != null) input = player.GetComponent<SamuraiPlayerInput>();
            if (input == null)
                foreach (var candidate in FindObjectsByType<SamuraiPlayerInput>())
                    if (candidate.gameObject.scene == gameObject.scene)
                    { input = candidate; break; }
            if (player == null && input != null)
                player = input.actor != null ? input.actor : input.GetComponent<SamuraiCombatant>();
        }

        public bool CanInteract(RouteTarget target, out string reason)
        {
            reason = null;
            if (!Application.isPlaying || !isActiveAndEnabled || player == null || input == null
                || !player.isActiveAndEnabled || player.gameObject.scene != gameObject.scene
                || input.gameObject.scene != gameObject.scene || input.actor != player
                || Time.timeScale <= 0f || !input.HasCursorCapture)
            { reason = "unavailable"; return false; }
            if (!Configured(target) || string.IsNullOrWhiteSpace(persistenceKey) || string.IsNullOrWhiteSpace(routeId)
                || string.IsNullOrEmpty(gameObject.scene.path))
            { reason = "unconfigured"; return false; }
            if (Completed(target)) { reason = "already-complete"; return false; }
            float radius = Radius(target);
            if ((PlayerPoint() - TargetPoint(target)).sqrMagnitude > radius * radius)
            { reason = "out-of-range"; return false; }
            if (player.IsDead || !player.CanAct || player.IsAttacking || player.IsCharging || player.IsGuarding
                || player.IsDodging || player.IsParrying || player.IsHitStopped || player.IsDeflected
                || player.IsInHitReaction || player.IsBeingKnockedBack || player.IsToughnessBroken
                || player.IsExecuting || player.IsBeingExecuted)
            { reason = "in-combat"; return false; }
            if (target == RouteTarget.BridgeGate && !InsideBridge())
            { reason = "inside-only"; return false; }
            if (!HasLineOfSight(target)) { reason = "blocked"; return false; }
            if (target == RouteTarget.SideGate && !HasKey)
            { reason = "key-required"; return false; }
            return true;
        }

        public bool TryInteract(RouteTarget target)
        {
            string reason;
            if (!CanInteract(target, out reason))
            {
                if (reason == "inside-only")
                    ShowFeedback("桥门由内侧上闩。山路侧门需要钥匙。", "The bridge is barred inside. Find the mountain-path key.");
                else if (reason == "key-required")
                    ShowFeedback("山路侧门需要钥匙。", "The mountain-path gate needs a key.");
                else if (reason == "in-combat")
                    ShowFeedback("当前动作中无法交互", "Cannot interact during this action");
                return false;
            }
            bool nextKey = HasKey || target == RouteTarget.Key;
            bool nextSide = SideGateOpen || target == RouteTarget.SideGate;
            bool nextBridge = BridgeGateOpen || target == RouteTarget.BridgeGate;
            if (!SaveState(nextKey, nextSide, nextBridge))
            { ShowFeedback("路线状态保存失败，请重试", "Route state could not be saved. Try again."); return false; }
            HasKey = nextKey; SideGateOpen = nextSide; BridgeGateOpen = nextBridge;
            ApplyVisualState(false);
            if (target == RouteTarget.Key)
                ShowFeedback("已取得山路钥匙 · B 可开启山路侧门", "Mountain-path key acquired - B opens the side gate");
            else if (target == RouteTarget.SideGate)
                ShowFeedback("山路侧门已解锁", "Mountain-path gate unlocked");
            else ShowFeedback("桥门内闩已拔开 · 捷径已打通", "Bridge unbarred - shortcut unlocked");
            nextScan = 0f;
            return true;
        }

        bool SaveState(bool hasKey, bool sideOpen, bool bridgeOpen)
        {
            try
            {
                PlayerPrefs.SetString(persistenceKey, JsonUtility.ToJson(new SavedRoute
                {
                    version = 1, routeId = routeId, scenePath = gameObject.scene.path,
                    HasKey = hasKey, SideGateOpen = sideOpen, BridgeGateOpen = bridgeOpen
                }));
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Awake 先恢复门姿与钥匙表现；不移动角色，也不改变战斗/存档点配置。</summary>
        public bool TryRestore()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || string.IsNullOrWhiteSpace(persistenceKey)
                || string.IsNullOrWhiteSpace(routeId) || string.IsNullOrEmpty(gameObject.scene.path)
                || !PlayerPrefs.HasKey(persistenceKey)) return false;
            SavedRoute saved;
            try { saved = JsonUtility.FromJson<SavedRoute>(PlayerPrefs.GetString(persistenceKey)); }
            catch (Exception) { return false; }
            if (saved == null || saved.version != 1 || saved.routeId != routeId || saved.scenePath != gameObject.scene.path)
                return false;
            HasKey = saved.HasKey || saved.SideGateOpen;
            SideGateOpen = saved.SideGateOpen; BridgeGateOpen = saved.BridgeGateOpen;
            ApplyVisualState(true);
            nextScan = 0f;
            return true;
        }

        /// <summary>与 Update 共用的门动画入口；暂停冻结，无捕获或战斗中已触发的开门仍继续。</summary>
        public void TickOpening(float deltaTime)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || Time.timeScale <= 0f
                || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            EnsureGateCaches();
            float amount = deltaTime / Mathf.Max(.05f, openSeconds);
            bool changed = false;
            if (SideGateOpen && sideProgress < 1f)
            {
                sideProgress = Mathf.Min(1f, sideProgress + amount);
                ApplyGate(cachedSideHinges, sideClosed, sideGateOpenAngles, sideGateBlocker, sideProgress);
                changed = true;
            }
            if (BridgeGateOpen && bridgeProgress < 1f)
            {
                bridgeProgress = Mathf.Min(1f, bridgeProgress + amount);
                ApplyGate(cachedBridgeHinges, bridgeClosed, bridgeGateOpenAngles, bridgeGateBlocker, bridgeProgress);
                changed = true;
            }
            if (changed) Physics.SyncTransforms();
        }

        void ApplyVisualState(bool immediate)
        {
            EnsureGateCaches();
            if (keyVisual != null) keyVisual.SetActive(!HasKey);
            if (sideLockVisual != null) sideLockVisual.SetActive(!SideGateOpen);
            if (bridgeLatchVisual != null) bridgeLatchVisual.SetActive(!BridgeGateOpen);
            if (immediate)
            { sideProgress = SideGateOpen ? 1f : 0f; bridgeProgress = BridgeGateOpen ? 1f : 0f; }
            ApplyGate(cachedSideHinges, sideClosed, sideGateOpenAngles, sideGateBlocker, sideProgress);
            ApplyGate(cachedBridgeHinges, bridgeClosed, bridgeGateOpenAngles, bridgeGateBlocker, bridgeProgress);
            if (immediate) Physics.SyncTransforms();
        }

        void EnsureGateCaches()
        {
            CacheHinges(sideGateHinges, ref cachedSideHinges, ref sideClosed);
            CacheHinges(bridgeGateHinges, ref cachedBridgeHinges, ref bridgeClosed);
        }

        static void CacheHinges(Transform[] source, ref Transform[] cached, ref Quaternion[] closed)
        {
            int count = source != null ? source.Length : 0;
            bool same = cached != null && cached.Length == count;
            for (int i = 0; same && i < count; i++) same = cached[i] == source[i];
            if (same) return;
            var nextCached = new Transform[count]; var nextClosed = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                nextCached[i] = source[i];
                int oldIndex = cached != null ? Array.IndexOf(cached, source[i]) : -1;
                nextClosed[i] = oldIndex >= 0 && closed != null && oldIndex < closed.Length ? closed[oldIndex]
                    : source[i] != null ? source[i].localRotation : Quaternion.identity;
            }
            cached = nextCached; closed = nextClosed;
        }

        static void ApplyGate(Transform[] hinges, Quaternion[] closed, float[] angles, Collider blocker, float progress)
        {
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            for (int i = 0; hinges != null && i < hinges.Length; i++)
                if (hinges[i] != null)
                {
                    float angle = angles != null && i < angles.Length && !float.IsNaN(angles[i]) && !float.IsInfinity(angles[i])
                        ? angles[i] : (i % 2 == 0 ? -100f : 100f);
                    hinges[i].localRotation = closed[i] * Quaternion.AngleAxis(angle * eased, Vector3.up);
                }
            if (blocker != null) blocker.enabled = progress < .9f;
        }

        bool InsideBridge()
        {
            Vector3 direction = bridgeInsideDirection; direction.y = 0f;
            if (!Finite(direction) || direction.sqrMagnitude < .0001f) return false;
            Vector3 offset = player.transform.position - bridgeGateRoot.position; offset.y = 0f;
            return Vector3.Dot(offset, direction.normalized) > Mathf.Max(0f, insideThreshold);
        }

        bool Configured(RouteTarget target)
        {
            var root = TargetRoot(target);
            if (root == null || root.gameObject.scene != gameObject.scene || !Finite(TargetPoint(target))) return false;
            if (target == RouteTarget.Key) return keyAnchor != null && keyAnchor.gameObject.scene == gameObject.scene;
            return target == RouteTarget.SideGate || target == RouteTarget.BridgeGate;
        }

        bool Completed(RouteTarget target) => target == RouteTarget.Key ? HasKey
            : target == RouteTarget.SideGate ? SideGateOpen : target == RouteTarget.BridgeGate && BridgeGateOpen;

        float Radius(RouteTarget target) => Mathf.Max(.25f, target == RouteTarget.Key ? keyRadius : interactionRadius);
        Vector3 PlayerPoint() => player.Motor != null ? player.transform.TransformPoint(player.Motor.center)
            : player.transform.position + Vector3.up * .75f;
        Vector3 TargetPoint(RouteTarget target) => target == RouteTarget.Key && keyAnchor != null ? keyAnchor.position
            : TargetRoot(target) != null ? TargetRoot(target).position + Vector3.up * 1.1f : Vector3.zero;
        Transform TargetRoot(RouteTarget target) => target == RouteTarget.Key
            ? keyRoot != null ? keyRoot : keyVisual != null ? keyVisual.transform : keyAnchor
            : target == RouteTarget.SideGate ? sideGateRoot : target == RouteTarget.BridgeGate ? bridgeGateRoot : null;
        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z)
            && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);

        bool HasLineOfSight(RouteTarget target)
        {
            Vector3 origin = PlayerPoint(), ray = TargetPoint(target) - origin;
            float distance = ray.magnitude;
            if (distance < .01f) return true;
            Transform currentRoot = TargetRoot(target);
            int count = gameObject.scene.GetPhysicsScene().Raycast(origin, ray / distance, rayHits,
                distance, visibilityMask, QueryTriggerInteraction.Ignore);
            if (count == rayHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var collider = rayHits[i].collider;
                if (collider == null || collider.transform.IsChildOf(player.transform)
                    || currentRoot != null && collider.transform.IsChildOf(currentRoot)) continue;
                if (rayHits[i].distance < distance - .02f) return false;
            }
            return true;
        }

        void SelectNearestTarget()
        {
            hasSelection = false;
            if (player == null || !player.isActiveAndEnabled || player.gameObject.scene != gameObject.scene) return;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                var target = (RouteTarget)i;
                if (Completed(target) || !Configured(target)) continue;
                float distance = (PlayerPoint() - TargetPoint(target)).sqrMagnitude;
                float radius = Radius(target);
                if (distance > radius * radius || distance >= nearest || !HasLineOfSight(target)) continue;
                selected = target; nearest = distance; hasSelection = true;
            }
        }

        void ShowFeedback(string zh, string en)
        {
            EnsurePrompt(); feedback = chinese ? zh : en; feedbackUntil = Time.time + 2.6f;
        }

        void RefreshPrompt(bool visible)
        {
            bool showingFeedback = Time.time < feedbackUntil;
            bool show = visible && (showingFeedback || hasSelection);
            if (show) EnsurePrompt();
            if (promptLabel == null) return;
            promptLabel.enabled = show;
            if (!show) return;
            string text = feedback;
            if (!showingFeedback)
            {
                string reason;
                bool can = CanInteract(selected, out reason);
                if (can && selected == RouteTarget.Key) text = chinese ? "B 拾取山路钥匙" : "B  Pick up mountain-path key";
                else if (can && selected == RouteTarget.SideGate) text = chinese ? "B 用钥匙开启山路侧门" : "B  Unlock mountain-path gate";
                else if (can) text = chinese ? "B 拔开桥门内闩 · 打通捷径" : "B  Unbar bridge - unlock shortcut";
                else if (reason == "inside-only") text = chinese ? "桥门由内侧上闩。山路侧门需要钥匙。"
                    : "The bridge is barred inside. Find the mountain-path key.";
                else if (reason == "key-required") text = chinese ? "山路侧门需要钥匙。" : "The mountain-path gate needs a key.";
                else text = chinese ? "当前无法交互" : "Cannot interact right now";
            }
            if (promptLabel.text != text) promptLabel.text = text;
        }

        void EnsurePrompt()
        {
            if (promptLabel != null || !Application.isPlaying) return;
            if (promptFont == null)
                foreach (var hud in FindObjectsByType<SamuraiHUDView>(FindObjectsInactive.Include))
                {
                    if (hud.gameObject.scene != gameObject.scene) continue;
                    Text source = hud.briefFeedback != null ? hud.briefFeedback : hud.deathText;
                    if (source != null && source.font != null) { promptFont = source.font; break; }
                }
            if (promptFont == null) promptFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            chinese = promptFont != null && promptFont.HasCharacter('桥') && promptFont.HasCharacter('钥');
            ownedCanvas = new GameObject("Level route prompt (runtime)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            ownedCanvas.transform.SetParent(transform, false);
            var canvas = ownedCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 81;
            var scaler = ownedCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
            var labelObject = new GameObject("Route hint", typeof(RectTransform), typeof(Text), typeof(Shadow));
            labelObject.transform.SetParent(ownedCanvas.transform, false);
            var rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f); rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 165f); rect.sizeDelta = new Vector2(1500f, 60f);
            promptLabel = labelObject.GetComponent<Text>();
            promptLabel.font = promptFont; promptLabel.fontSize = 25; promptLabel.alignment = TextAnchor.MiddleCenter;
            promptLabel.color = new Color(1f, .88f, .59f, 1f); promptLabel.raycastTarget = false; promptLabel.supportRichText = false;
            var shadow = labelObject.GetComponent<Shadow>(); shadow.effectColor = new Color(0f, 0f, 0f, .85f);
            shadow.effectDistance = new Vector2(1f, -2f);
        }

        void OnDisable() => ClearPrompt();
        void OnDestroy() => ClearPrompt();
        void ClearPrompt()
        {
            if (ownedCanvas != null)
            {
                ownedCanvas.SetActive(false);
                if (Application.isPlaying) Destroy(ownedCanvas); else DestroyImmediate(ownedCanvas);
            }
            ownedCanvas = null; promptLabel = null; feedbackUntil = 0f;
        }
    }
}
