using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>不依赖额外字体或 UI 包的训练场 HUD；英文标签保证默认字体可读。</summary>
    [DisallowMultipleComponent]
    public sealed class SamuraiCombatHUD : MonoBehaviour
    {
        public SamuraiCombatant player;
        public SamuraiTrainingEnemy enemy;
        public SamuraiPlayerInput input;
        public Camera worldCamera;
        [Range(0.75f, 2f)] public float uiScale = 1f;
        public bool showControls = true;

        [System.NonSerialized] private GUIStyle title;
        [System.NonSerialized] private GUIStyle text;
        [System.NonSerialized] private GUIStyle small;
        [System.NonSerialized] private GUIStyle warning;
        [System.NonSerialized] private GUIStyle centered;
        [System.NonSerialized] private SamuraiCombatant observedPlayer;
        [System.NonSerialized] private float parryFeedbackUntil;
        private readonly Color panel = new Color(0.035f, 0.05f, 0.065f, 0.93f);
        private readonly Color healthColor = new Color(0.84f, 0.27f, 0.25f);
        private readonly Color staminaColor = new Color(0.30f, 0.76f, 0.55f);
        private readonly Color gold = new Color(1f, 0.76f, 0.3f);

        private void OnEnable()
        {
            // GUIStyle 的原生状态不跨编辑器域重载缓存，首次 OnGUI 再按当前皮肤建立。
            title = text = small = warning = centered = null;
            BindPlayerEvents();
        }

        private void OnDisable() => UnbindPlayerEvents();

        private void BindPlayerEvents()
        {
            if (observedPlayer == player) return;
            UnbindPlayerEvents();
            observedPlayer = player;
            if (observedPlayer != null) observedPlayer.HitReceived += OnHitReceived;
        }

        private void UnbindPlayerEvents()
        {
            if (observedPlayer != null) observedPlayer.HitReceived -= OnHitReceived;
            observedPlayer = null;
        }

        private void OnHitReceived(SamuraiHitResult result)
        {
            if (result == SamuraiHitResult.PerfectGuard || result == SamuraiHitResult.Parried)
                parryFeedbackUntil = Time.unscaledTime + 1.05f;
        }

        public void Configure(SamuraiCombatant combatant, SamuraiTrainingEnemy opponent,
            SamuraiPlayerInput playerInput, Camera view = null)
        {
            player = combatant;
            enemy = opponent;
            input = playerInput;
            worldCamera = view;
            if (isActiveAndEnabled) BindPlayerEvents();
        }

        private void InitializeStyles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            text = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            title.normal.textColor = Color.white;
            text.normal.textColor = new Color(0.94f, 0.96f, 0.98f);
            small.normal.textColor = new Color(0.88f, 0.92f, 0.95f);
            warning = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            warning.normal.textColor = gold;
            centered = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
        }

        private void OnGUI()
        {
            if (player == null) return;
            BindPlayerEvents();
            InitializeStyles();
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            Color previousContentColor = GUI.contentColor;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            float scale = Mathf.Clamp(uiScale * Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.65f, 2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            DrawPanel(new Rect(20, 20, 330, 190));
            GUI.Label(new Rect(34, 28, 300, 27), "DOULI / SAMURAI COMBAT", title);
            DrawBar(new Rect(34, 63, 300, 18), player.Health, player.MaxHealth, healthColor, "HEALTH");
            DrawBar(new Rect(34, 89, 300, 18), player.Stamina, player.MaxStamina, staminaColor, "STAMINA");
            string state = player.StateName ?? "Idle";
            if (player.ComboIndex >= 0) state += "  /  chain " + (player.ComboIndex + 1);
            if (player.IsCriticalAttack) state += "  /  CRITICAL x2";
            if (player.IsInvulnerable) state += "  /  evade window";
            GUI.Label(new Rect(34, 116, 300, 22), state, text);
            if (player.IsCharging)
                DrawBar(new Rect(34, 145, 300, 16), player.ChargeNormalizedTime, 1f,
                    player.IsChargeCriticalWindowOpen ? new Color(.35f, .9f, 1f) : gold,
                    player.IsChargeCriticalWindowOpen ? "PERFECT RELEASE - 2X DAMAGE" :
                    player.ChargeNormalizedTime >= 1f ? "CHARGED - RELEASE RMB" : "CHARGING");
            else GUI.Label(new Rect(34, 144, 300, 20), "Face incoming strikes to guard / watch stamina", small);
            string defense = player.IsParryWindowOpen ? "PARRY WINDOW OPEN" :
                player.ParryCooldownRemaining > 0.01f ? "PARRY READY IN " + player.ParryCooldownRemaining.ToString("0.0") + "s" :
                "F  Hold guard / time the press to parry";
            if (player.IsParryWindowOpen) DrawSolid(new Rect(30, 170, 308, 27), new Color(0.12f, 0.38f, 0.32f, 1f));
            GUI.Label(new Rect(34, 173, 300, 22), defense, small);

            SamuraiTargetLock targetLock = input != null ? input.targetLock : null;
            SamuraiCombatant shownEnemy = targetLock != null && targetLock.CurrentTarget != null ? targetLock.CurrentTarget :
                enemy != null ? enemy.actor : null;
            if (shownEnemy != null)
            {
                DrawPanel(new Rect(width - 350, 20, 330, 118));
                bool isLocked = targetLock != null && targetLock.CurrentTarget == shownEnemy;
                GUI.Label(new Rect(width - 336, 28, 300, 26), isLocked ? "LOCKED TARGET" :
                    enemy != null && enemy.activeTraining ? "SPARRING OPPONENT" : "TRAINING DUMMY", title);
                DrawBar(new Rect(width - 336, 64, 300, 18), shownEnemy.Health, shownEnemy.MaxHealth, healthColor, "HEALTH");
                GUI.Label(new Rect(width - 336, 92, 300, 36), enemy != null && enemy.actor == shownEnemy ? enemy.Intent : shownEnemy.StateName, small);
            }
            if (enemy != null && enemy.actor != null) DrawEnemyWarning(width, height, scale);
            DrawTargetMarker(targetLock, scale);
            if (Time.unscaledTime < parryFeedbackUntil)
            {
                DrawPanel(new Rect(width * 0.5f - 130f, 160f, 260f, 43f));
                GUI.Label(new Rect(width * 0.5f - 125f, 163f, 250f, 35f), "PARRY SUCCESS", warning);
            }

            if (showControls)
            {
                DrawPanel(new Rect(20, height - 160, 620, 140));
                GUI.Label(new Rect(34, height - 153, 592, 21), "WASD / Arrows  Move     Shift  Run     Space  Dodge", text);
                GUI.Label(new Rect(34, height - 129, 592, 21), "F  Hold guard / timed press to parry     Tab / MMB  Toggle target lock", text);
                GUI.Label(new Rect(34, height - 105, 592, 21), "LMB  Light / click to chain     RMB  Tap heavy / hold charged strike", text);
                GUI.Label(new Rect(34, height - 81, 592, 21), "Dodge or guard during recovery to cancel     T  Toggle opponent", text);
                GUI.Label(new Rect(34, height - 57, 592, 21), "R  Reset     Mouse  Orbit (unlocked)     Wheel  Zoom     Esc  Release cursor", text);
            }
            if (player.IsDead)
            {
                DrawPanel(new Rect(width * 0.5f - 190, height * 0.5f - 45, 380, 90));
                GUI.Label(new Rect(width * 0.5f - 180, height * 0.5f - 37, 360, 35), "DEFEATED", warning);
                GUI.Label(new Rect(width * 0.5f - 180, height * 0.5f, 360, 30), "Press R to reset the training arena", centered);
            }
            else if (input != null && !input.HasCursorCapture)
            {
                float noticeY = height - (showControls ? 220f : 68f);
                DrawPanel(new Rect(width * 0.5f - 230, noticeY, 460, 46));
                GUI.Label(new Rect(width * 0.5f - 220, noticeY + 5, 440, 35), "Click to capture mouse and resume controls", centered);
            }
            GUI.color = previousColor;
            GUI.contentColor = previousContentColor;
            GUI.matrix = previousMatrix;
        }

        private void DrawTargetMarker(SamuraiTargetLock targetLock, float scale)
        {
            if (targetLock == null || targetLock.CurrentTarget == null || worldCamera == null) return;
            var locked = targetLock.CurrentTarget;
            float markerHeight = locked.Motor != null ? locked.Motor.height + 0.3f : 1.8f;
            Vector3 point = worldCamera.WorldToScreenPoint(locked.transform.position + Vector3.up * markerHeight);
            if (point.z <= 0f) return;
            float x = point.x / scale, y = (Screen.height - point.y) / scale;
            const float half = 16f, corner = 7f, line = 2f;
            DrawSolid(new Rect(x - half, y - half, corner, line), gold);
            DrawSolid(new Rect(x - half, y - half, line, corner), gold);
            DrawSolid(new Rect(x + half - corner, y - half, corner, line), gold);
            DrawSolid(new Rect(x + half - line, y - half, line, corner), gold);
            DrawSolid(new Rect(x - half, y + half - line, corner, line), gold);
            DrawSolid(new Rect(x - half, y + half - corner, line, corner), gold);
            DrawSolid(new Rect(x + half - corner, y + half - line, corner, line), gold);
            DrawSolid(new Rect(x + half - line, y + half - corner, line, corner), gold);
            GUI.Label(new Rect(x - 55f, y + 18f, 110f, 22f), "LOCKED", centered);
        }

        private void DrawEnemyWarning(float width, float height, float scale)
        {
            if (!enemy.IsTelegraphing) return;
            float x = width * 0.5f;
            float y = height * 0.25f;
            if (worldCamera != null)
            {
                Vector3 point = worldCamera.WorldToScreenPoint(enemy.actor.transform.position + Vector3.up * 2.1f);
                if (point.z > 0f)
                {
                    x = Mathf.Clamp(point.x / scale, 165f, width - 165f);
                    y = Mathf.Clamp((Screen.height - point.y) / scale, 150f, height - 170f);
                }
            }
            DrawPanel(new Rect(x - 160, y - 16, 320, 57));
            GUI.Label(new Rect(x - 155, y - 13, 310, 30), enemy.Intent, warning);
            DrawBar(new Rect(x - 145, y + 22, 290, 7), enemy.TelegraphProgress, 1f, gold, string.Empty);
        }

        private void DrawPanel(Rect rect) => DrawSolid(rect, panel);

        private void DrawBar(Rect rect, float value, float maximum, Color color, string label)
        {
            DrawSolid(rect, new Color(0.14f, 0.17f, 0.19f, 1f));
            Rect fill = rect;
            fill.width *= maximum > 0f ? Mathf.Clamp01(value / maximum) : 0f;
            DrawSolid(fill, color);
            if (!string.IsNullOrEmpty(label))
            {
                string amount = maximum > 1f ? "  " + Mathf.CeilToInt(value) + " / " + Mathf.CeilToInt(maximum) : string.Empty;
                GUI.Label(new Rect(rect.x + 6, rect.y - 2, rect.width - 12, rect.height + 4), label + amount, small);
            }
        }

        private static void DrawSolid(Rect rect, Color color)
        {
            Color before = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = before;
        }
    }
}
