using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OCDesign.Samurai
{
    /// <summary>庭院祈愿点：只保存本场景的复活点，不保存敌人、宝箱或完整游戏进度。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class SamuraiCourtyardCheckpoint : MonoBehaviour
    {
        public SamuraiCombatant player;
        public SamuraiPlayerInput input;
        [Tooltip("安全落脚点；朝向同时作为 R 重试后的朝向。")]
        public Transform respawnAnchor;
        [Min(.25f)] public float interactionRadius = 2.2f;
        public string checkpointId = "first-courtyard-shrine";
        [Tooltip("仅此组件使用的偏好键；隔离验证必须换用临时键。")]
        public string persistenceKey = "OCDesign.Samurai.CourtyardCheckpoint.v1";
        [Min(0f)] public float enemyExclusionRadius = 8f;
        public LayerMask visibilityMask = ~0;
        [Tooltip("复用现有 NotoSansSC 中文字体；未设置时从本场景 HUD 取用。")]
        public Font promptFont;
        [Tooltip("可绑定预置文字；为空时仅在运行时建立独立提示层。")]
        public Text promptLabel;

        [Serializable]
        sealed class SavedCheckpoint
        {
            public int version = 1;
            public string id;
            public string scenePath;
        }

        readonly RaycastHit[] visibilityHits = new RaycastHit[32];
        readonly Collider[] nearbyColliders = new Collider[128];
        GameObject ownedCanvas;
        bool ownsLabel, chinese, nearby, ready;
        float nextScan, feedbackUntil;
        string feedback, unavailableReason;
        const float ScanInterval = .25f;
        public bool HasSavedCheckpoint { get; private set; }

        IEnumerator Start()
        {
            // 等待玩家 Awake/Start 完成，避免原出生捕获覆盖已读出的锚点。
            yield return null;
            if (!isActiveAndEnabled) yield break;
            ResolveReferences();
            TryRestore();
        }

        void OnEnable() { nextScan = 0f; }

        void Update()
        {
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + ScanInterval;
                ResolveReferences();
                nearby = player != null && (player.transform.position - transform.position).sqrMagnitude
                    <= Mathf.Max(.25f, interactionRadius) * Mathf.Max(.25f, interactionRadius);
                ready = nearby && CanActivate(out unavailableReason);
            }
            bool visible = Application.isPlaying && input != null && input.HasCursorCapture && Time.timeScale > 0f;
            if (visible && nearby && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
                Activate();
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

        public bool CanActivate() => CanActivate(out _);

        /// <summary>即时检查，不使用 UI 的四分之一秒缓存；不写入偏好或移动角色。</summary>
        public bool CanActivate(out string reason)
        {
            reason = null;
            if (!Application.isPlaying || !isActiveAndEnabled || player == null || input == null
                || !player.isActiveAndEnabled || player.gameObject.scene != gameObject.scene
                || input.gameObject.scene != gameObject.scene || input.actor != player
                || Time.timeScale <= 0f || !input.HasCursorCapture)
            { reason = "unavailable"; return false; }
            if (!ValidAnchor() || string.IsNullOrWhiteSpace(checkpointId) || string.IsNullOrWhiteSpace(persistenceKey)
                || string.IsNullOrEmpty(gameObject.scene.path))
            { reason = "unconfigured"; return false; }
            float radius = Mathf.Max(.25f, interactionRadius);
            if ((player.transform.position - transform.position).sqrMagnitude > radius * radius)
            { reason = "out-of-range"; return false; }
            if (player.IsDead || !player.CanAct || player.IsAttacking || player.IsCharging || player.IsGuarding
                || player.IsDodging || player.IsParrying || player.IsHitStopped || player.IsDeflected
                || player.IsInHitReaction || player.IsBeingKnockedBack || player.IsToughnessBroken
                || player.IsExecuting || player.IsBeingExecuted)
            { reason = "in-combat"; return false; }
            if (!HasLineOfSight()) { reason = "blocked"; return false; }
            if (EnemyNearby()) { reason = "enemy-nearby"; return false; }
            return true;
        }

        /// <summary>与 C 键相同的正式入口；保存的只有此点 ID 和场景路径。</summary>
        public bool Activate()
        {
            if (!CanActivate(out unavailableReason))
            {
                ShowFeedback(unavailableReason == "enemy-nearby" || unavailableReason == "in-combat"
                    ? "敌人尚近，暂不能祈愿" : "此刻无法祈愿",
                    unavailableReason == "enemy-nearby" || unavailableReason == "in-combat"
                    ? "Enemies nearby - cannot pray" : "Cannot pray here yet");
                return false;
            }
            try
            {
                PlayerPrefs.SetString(persistenceKey, JsonUtility.ToJson(new SavedCheckpoint
                    { id = checkpointId, scenePath = gameObject.scene.path }));
                PlayerPrefs.Save();
            }
            catch (Exception)
            { ShowFeedback("复活点保存失败", "Checkpoint could not be saved"); return false; }
            ApplyAnchor();
            HasSavedCheckpoint = true;
            ShowFeedback("复活点已保存 · 生命与精力恢复", "Respawn point saved - health and stamina restored");
            return true;
        }

        /// <summary>仅接受同场景、同 ID、版本匹配的记录；不覆盖其他偏好，不刷新敌人。</summary>
        public bool TryRestore()
        {
            ResolveReferences();
            if (!Application.isPlaying || !isActiveAndEnabled || player == null || !player.isActiveAndEnabled
                || player.gameObject.scene != gameObject.scene || player.Profile == null || !ValidAnchor()
                || string.IsNullOrWhiteSpace(checkpointId) || string.IsNullOrWhiteSpace(persistenceKey)
                || string.IsNullOrEmpty(gameObject.scene.path) || !PlayerPrefs.HasKey(persistenceKey)) return false;
            SavedCheckpoint saved;
            try { saved = JsonUtility.FromJson<SavedCheckpoint>(PlayerPrefs.GetString(persistenceKey)); }
            catch (Exception) { return false; }
            if (saved == null || saved.version != 1 || saved.id != checkpointId
                || saved.scenePath != gameObject.scene.path) return false;
            ApplyAnchor();
            HasSavedCheckpoint = true;
            return true;
        }

        bool ValidAnchor() => respawnAnchor != null && respawnAnchor.gameObject.scene == gameObject.scene
            && Finite(respawnAnchor.position) && Finite(respawnAnchor.forward);

        static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

        void ApplyAnchor()
        {
            if (player.Movement == null) player.ResetCombat();
            var motor = player.Motor;
            bool enabledBefore = motor != null && motor.enabled;
            if (enabledBefore) motor.enabled = false;
            Vector3 forward = respawnAnchor.forward; forward.y = 0f;
            Quaternion facing = forward.sqrMagnitude > .0001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
            player.transform.SetPositionAndRotation(respawnAnchor.position, facing);
            player.Movement.CaptureSpawn();
            if (enabledBefore) motor.enabled = true;
            player.ResetCombat();
            if (input != null && input.targetLock != null) input.targetLock.ClearLock();
            if (input != null && input.orbitCamera != null) input.orbitCamera.ResetView();
        }

        bool HasLineOfSight()
        {
            Vector3 origin = player.Motor != null ? player.transform.TransformPoint(player.Motor.center)
                : player.transform.position + Vector3.up * .75f;
            Vector3 ray = transform.position + Vector3.up * 1.1f - origin;
            float distance = ray.magnitude;
            if (distance < .01f) return true;
            int count = gameObject.scene.GetPhysicsScene().Raycast(origin, ray / distance, visibilityHits,
                distance, visibilityMask, QueryTriggerInteraction.Ignore);
            if (count == visibilityHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var collider = visibilityHits[i].collider;
                if (collider == null || collider.transform.IsChildOf(player.transform)
                    || collider.transform.IsChildOf(transform)) continue;
                if (visibilityHits[i].distance < distance - .02f) return false;
            }
            return true;
        }

        bool EnemyNearby()
        {
            float radius = Mathf.Max(0f, enemyExclusionRadius);
            if (radius <= 0f) return false;
            int count = gameObject.scene.GetPhysicsScene().OverlapSphere(player.transform.position, radius,
                nearbyColliders, ~0, QueryTriggerInteraction.Ignore);
            // 查询溢出时保守拒绝，避免漏掉尚未处理的敌人。
            if (count == nearbyColliders.Length) return true;
            for (int i = 0; i < count; i++)
            {
                var candidate = nearbyColliders[i] != null
                    ? nearbyColliders[i].GetComponentInParent<SamuraiCombatant>() : null;
                if (candidate != null && candidate != player && candidate.isActiveAndEnabled
                    && candidate.gameObject.scene == gameObject.scene && candidate.Team != player.Team
                    && !candidate.IsDead && (candidate.transform.position - player.transform.position).sqrMagnitude <= radius * radius)
                    return true;
            }
            return false;
        }

        void ShowFeedback(string zh, string en)
        {
            EnsurePrompt();
            feedback = chinese ? zh : en;
            feedbackUntil = Time.time + 2.5f;
        }

        void RefreshPrompt(bool visible)
        {
            bool hasFeedback = Time.time < feedbackUntil;
            bool show = visible && (hasFeedback || nearby);
            if (show) EnsurePrompt();
            if (promptLabel == null) return;
            promptLabel.enabled = show;
            if (!show) return;
            string text = hasFeedback ? feedback : ready
                ? chinese ? "C 祈愿 · 保存复活点" : "C  Pray - save respawn point"
                : unavailableReason == "enemy-nearby" || unavailableReason == "in-combat"
                    ? chinese ? "敌人尚近，暂不能祈愿" : "Enemies nearby - cannot pray"
                    : chinese ? "靠近神龛，安全时按 C 祈愿" : "Approach the shrine and press C when safe";
            if (promptLabel.text != text) promptLabel.text = text;
        }

        void EnsurePrompt()
        {
            if (promptLabel != null)
            { chinese = promptLabel.font != null && promptLabel.font.HasCharacter('祈'); return; }
            if (!Application.isPlaying) return;
            if (promptFont == null)
                foreach (var hud in FindObjectsByType<SamuraiHUDView>(FindObjectsInactive.Include))
                {
                    if (hud.gameObject.scene != gameObject.scene) continue;
                    Text source = hud.briefFeedback != null ? hud.briefFeedback : hud.deathText;
                    if (source != null && source.font != null) { promptFont = source.font; break; }
                }
            if (promptFont == null) promptFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            chinese = promptFont != null && promptFont.HasCharacter('祈');
            ownedCanvas = new GameObject("Checkpoint prompt (runtime)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            ownedCanvas.transform.SetParent(transform, false);
            var canvas = ownedCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 80;
            var scaler = ownedCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
            var labelObject = new GameObject("Prayer hint", typeof(RectTransform), typeof(Text), typeof(Shadow));
            labelObject.transform.SetParent(ownedCanvas.transform, false);
            var rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f); rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 100f); rect.sizeDelta = new Vector2(1100f, 54f);
            promptLabel = labelObject.GetComponent<Text>(); ownsLabel = true;
            promptLabel.font = promptFont; promptLabel.fontSize = 25; promptLabel.alignment = TextAnchor.MiddleCenter;
            promptLabel.color = new Color(1f, .88f, .59f, 1f); promptLabel.raycastTarget = false;
            promptLabel.supportRichText = false;
            var shadow = labelObject.GetComponent<Shadow>(); shadow.effectColor = new Color(0f, 0f, 0f, .85f);
            shadow.effectDistance = new Vector2(1f, -2f);
        }

        void OnDisable() => ClearPrompt();
        void OnDestroy() => ClearPrompt();
        void ClearPrompt()
        {
            if (promptLabel != null) promptLabel.enabled = false;
            if (ownedCanvas != null)
            {
                ownedCanvas.SetActive(false);
                if (Application.isPlaying) Destroy(ownedCanvas); else DestroyImmediate(ownedCanvas);
            }
            ownedCanvas = null;
            if (ownsLabel) promptLabel = null;
            ownsLabel = false;
            feedbackUntil = 0f;
        }
    }
}
