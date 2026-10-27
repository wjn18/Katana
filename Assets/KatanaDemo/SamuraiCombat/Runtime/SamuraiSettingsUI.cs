using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OCDesign.Samurai
{
    /// <summary>原场景中可编辑的 uGUI 设置面板。仅候选项动态复用，布局由场景保存。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public sealed class SamuraiSettingsUI : MonoBehaviour
    {
        [Header("战斗依赖")]
        public SamuraiCombatant actor;
        public SamuraiPlayerInput input;
        public SamuraiTrainingEnemy enemy;
        public SamuraiMartialArtMenu martialArts;
        public GameObject combatRoot;
        [Header("场景中的设置界面")]
        public GameObject panelRoot;
        public GameObject[] pages;
        public Button[] tabButtons;
        public Image[] tabUnderlines;
        public Button resumeButton;
        public Text noticeText;
        public Text artsHelpText;
        [Header("武技页")]
        public Button[] stanceButtons;
        public Button[] slotButtons;
        public Text candidatesTitle;
        public Text fixedBranchText;
        public ScrollRect candidatesScroll;
        public RectTransform candidatesContent;
        public Button candidateTemplate;
        public Button defaultsButton;
        public Button discardButton;
        public Button applyButton;
        [Header("训练页")]
        public Button toggleTrainingButton;
        public Button[] trainingModeButtons;
        public Button resetTrainingButton;
        public Text trainingStatus;
        [Header("显示页")]
        public Slider uiScaleSlider;
        public Text uiScaleText;
        public Text performanceText;
        public Text displayText;
        public Color activeColor = new Color(.95f, .78f, .43f, 1f);
        public Color inactiveColor = new Color(.65f, .69f, .71f, 1f);

        readonly List<Button> candidatePool = new List<Button>();
        CanvasScaler scaler;
        [SerializeField, HideInInspector] Vector2 baseResolution;
        [System.NonSerialized] bool buttonsBound;
        bool isOpen, capturedBeforeOpen, combatWasActive;
        CursorLockMode cursorLockBeforeOpen;
        bool cursorVisibleBeforeOpen;
        float previousTimeScale = 1f, noticeUntil;
        int selectedTab;
        float sampleSeconds, samplePeak;
        int sampleFrames, peakIndex;
        readonly float[] peaks = new float[4];
        string performance = "正在采样帧率…";
        const string ScalePreference = "OCDesign.Samurai.HUD.Scale";
        public bool IsOpen => isOpen;
        public int SelectedTab => selectedTab;
        public GameObject CombatRoot => combatRoot;

        void Awake()
        {
            if (martialArts == null && actor != null) martialArts = actor.GetComponent<SamuraiMartialArtMenu>();
            ResolveScaler();
            if (panelRoot != null) panelRoot.SetActive(false);
            BindButtons();
            float scale = Mathf.Clamp(PlayerPrefs.GetFloat(ScalePreference, 1f), .8f, 1.15f);
            if (uiScaleSlider != null) uiScaleSlider.SetValueWithoutNotify(scale);
            SetUIScale(scale);
        }

        void OnEnable()
        {
            ResolveScaler();
            BindButtons();
            // 脚本热重载不保留普通 C# 草稿对象，不能把游戏留在零时标。
            if (isOpen && (martialArts == null || martialArts.Draft == null)) Close();
        }

        void ResolveScaler()
        {
            if (scaler == null) scaler = GetComponentInParent<CanvasScaler>();
            // 基准单独序列化，重启组件或脚本域重载不把已经缩放的分辨率再当作基准。
            if (scaler != null && (baseResolution.x <= 0f || baseResolution.y <= 0f))
                baseResolution = scaler.referenceResolution;
        }

        void BindButtons()
        {
            // 运行时 UnityEvent 监听不跨脚本域重载；正常 Awake/OnEnable 则只绑定一次。
            if (buttonsBound) return;
            buttonsBound = true;
            if (resumeButton != null) resumeButton.onClick.AddListener(Close);
            for (int i = 0; tabButtons != null && i < tabButtons.Length; i++)
            { int index = i; if (tabButtons[i] != null) tabButtons[i].onClick.AddListener(() => SelectTab(index)); }
            for (int i = 0; stanceButtons != null && i < stanceButtons.Length; i++)
            {
                int index = i;
                if (stanceButtons[i] != null) stanceButtons[i].onClick.AddListener(() =>
                { if (martialArts != null && martialArts.SelectStance(index)) RefreshMartialArts(); });
            }
            for (int i = 0; slotButtons != null && i < slotButtons.Length && i < SamuraiMartialArtLoadout.DisplaySlots.Length; i++)
            {
                var slot = SamuraiMartialArtLoadout.DisplaySlots[i];
                if (slotButtons[i] != null) slotButtons[i].onClick.AddListener(() =>
                { if (martialArts != null && martialArts.SelectSlot(slot)) RefreshMartialArts(); });
            }
            if (defaultsButton != null) defaultsButton.onClick.AddListener(() =>
            { if (martialArts == null || !martialArts.CanEditDraft) return; martialArts.RestoreDefaults(); RefreshMartialArts(); ShowNotice("默认装备已载入草稿，应用后生效"); });
            if (discardButton != null) discardButton.onClick.AddListener(DiscardChanges);
            if (applyButton != null) applyButton.onClick.AddListener(ApplyChanges);
            if (toggleTrainingButton != null) toggleTrainingButton.onClick.AddListener(() =>
            { if (enemy != null) enemy.ToggleActive(); RefreshTraining(); });
            for (int i = 0; trainingModeButtons != null && i < trainingModeButtons.Length; i++)
            { int index = i; if (trainingModeButtons[i] != null) trainingModeButtons[i].onClick.AddListener(() => SetTrainingMode(index)); }
            if (resetTrainingButton != null) resetTrainingButton.onClick.AddListener(ResetTraining);
            if (uiScaleSlider != null) uiScaleSlider.onValueChanged.AddListener(SetUIScale);
        }

        void Update()
        {
            SamplePerformance();
            var keys = Keyboard.current;
            if (Application.isFocused && keys != null)
            {
                if (keys.escapeKey.wasPressedThisFrame) { if (isOpen) Close(); else Open(); }
                else if (keys.mKey.wasPressedThisFrame) { if (isOpen) SelectTab(1); else Open(1); }
            }
            if (!isOpen) return;
            if (noticeText != null && Time.unscaledTime >= noticeUntil)
                noticeText.text = selectedTab == 1 && martialArts != null && !martialArts.CanEditDraft
                    ? "当前动作中仅可查看；请收招后重新打开，或到训练页复位。"
                    : "游戏已暂停  ·  Esc 返回游戏";
            if (selectedTab == 3)
            {
                if (performanceText != null) performanceText.text = performance;
                if (displayText != null) displayText.text = Screen.width + " × " + Screen.height + "  ·  " +
                    (Screen.fullScreen ? "全屏" : "窗口") + "\n图形 API：" + SystemInfo.graphicsDeviceType;
            }
        }

        public bool Open(int tab = 0)
        {
            if (isOpen) { SelectTab(tab); return true; }
            if (!Application.isPlaying || !isActiveAndEnabled || panelRoot == null) return false;
            // 在取消尚未发出的按键前记录装备编辑权限；不能借取消蓄力在攻击中改装备。
            if (martialArts != null) martialArts.BeginDraft(actor != null && actor.CanAct && !actor.IsDead);
            previousTimeScale = Time.timeScale;
            cursorLockBeforeOpen = Cursor.lockState;
            cursorVisibleBeforeOpen = Cursor.visible;
            capturedBeforeOpen = input != null && input.HasCursorCapture;
            combatWasActive = combatRoot != null && combatRoot.activeSelf;
            isOpen = true;
            if (input != null) input.SuspendInput();
            if (actor != null) actor.CancelInput();
            if (enemy != null && enemy.actor != null) enemy.actor.CancelInput();
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (combatRoot != null) combatRoot.SetActive(false);
            panelRoot.SetActive(true);
            RefreshHelp();
            SelectTab(tab);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            return true;
        }

        public void OpenFromButton() => Open();

        public void Close()
        {
            if (!isOpen) return;
            isOpen = false;
            if (martialArts != null) martialArts.DiscardDraft();
            if (panelRoot != null) panelRoot.SetActive(false);
            if (combatRoot != null) combatRoot.SetActive(combatWasActive);
            Time.timeScale = previousTimeScale;
            if (input != null)
            {
                input.ResumeInput();
                if (capturedBeforeOpen && Application.isFocused && Application.isPlaying) input.CaptureCursor();
            }
            if (!capturedBeforeOpen || input == null)
            { Cursor.lockState = cursorLockBeforeOpen; Cursor.visible = cursorVisibleBeforeOpen; }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            capturedBeforeOpen = false;
        }

        public void SelectTab(int index)
        {
            selectedTab = Mathf.Clamp(index, 0, 3);
            for (int i = 0; pages != null && i < pages.Length; i++) if (pages[i] != null) pages[i].SetActive(i == selectedTab);
            for (int i = 0; tabButtons != null && i < tabButtons.Length; i++) SetSelected(tabButtons[i], i == selectedTab);
            for (int i = 0; tabUnderlines != null && i < tabUnderlines.Length; i++)
                if (tabUnderlines[i] != null) tabUnderlines[i].enabled = i == selectedTab;
            noticeUntil = 0f;
            if (selectedTab == 1) RefreshMartialArts();
            if (selectedTab == 2) RefreshTraining();
        }

        void RefreshHelp()
        {
            if (artsHelpText == null) return;
            var p = actor != null ? actor.Profile : null;
            if (p == null) return;
            artsHelpText.text = "琢技\n命中、防御与弹反积累蓝环；满槽转为金色。\n以强击或武技开启 " +
                p.artsDurationSeconds.ToString("0.#") + " 秒强化，首招免精力。\n强击伤害 ×" + p.artsDamageMultiplier.ToString("0.#") +
                " · 武技伤害 ×" + p.artsMartialArtDamageMultiplier.ToString("0.#") +
                "\n强化招式精力消耗 ×" + p.artsStaminaCostMultiplier.ToString("0.##") + "，圆环显示剩余时间。";
        }

        public void RefreshMartialArts()
        {
            bool available = martialArts != null && martialArts.Draft != null;
            bool editable = available && martialArts.CanEditDraft;
            if (defaultsButton != null) defaultsButton.interactable = editable;
            if (applyButton != null) applyButton.interactable = editable;
            if (discardButton != null) discardButton.interactable = available;
            if (!available) { if (candidatesTitle != null) candidatesTitle.text = "此角色尚未配置武技"; return; }
            var draft = martialArts.Draft;
            int stance = martialArts.SelectedStanceIndex;
            var selectedSlot = martialArts.SelectedSlot;
            for (int i = 0; stanceButtons != null && i < stanceButtons.Length; i++)
            {
                if (stanceButtons[i] == null) continue;
                bool valid = i < draft.StanceCount && actor.Profile.stances[i] != null;
                stanceButtons[i].gameObject.SetActive(valid);
                if (valid) { SetLabel(stanceButtons[i], actor.Profile.stances[i].DisplayName); SetSelected(stanceButtons[i], i == stance); }
            }
            for (int i = 0; slotButtons != null && i < slotButtons.Length && i < SamuraiMartialArtLoadout.DisplaySlots.Length; i++)
            {
                var slot = SamuraiMartialArtLoadout.DisplaySlots[i];
                bool fixedBranch = SamuraiMartialArtCommands.IsFixedStrongBranch(actor.Profile.stances[stance], slot);
                var equipped = draft.Get(stance, slot);
                SetLabel(slotButtons[i], SamuraiMartialArtCommands.InputLabel(slot) + "\n" +
                    (fixedBranch ? "固定强击分支 · 查看衔接" : equipped != null ? equipped.DisplayName : "未装备"));
                SetSelected(slotButtons[i], slot == selectedSlot);
            }
            bool fixedSelected = SamuraiMartialArtCommands.IsFixedStrongBranch(actor.Profile.stances[stance], selectedSlot);
            if (candidatesTitle != null) candidatesTitle.text = fixedSelected ? "中架势 · 固定强击衔接" : "候选武技 / " + SamuraiMartialArtCommands.InputLabel(selectedSlot);
            if (fixedBranchText != null) fixedBranchText.gameObject.SetActive(fixedSelected);
            if (candidatesScroll != null) candidatesScroll.gameObject.SetActive(!fixedSelected);
            if (fixedSelected) return;
            var options = draft.GetOptions(stance, selectedSlot);
            var current = draft.Get(stance, selectedSlot);
            for (int i = 0; i < options.Length + 1; i++)
            {
                var choice = i == 0 ? null : options[i - 1];
                Button button = CandidateAt(i);
                if (button == null) continue;
                button.gameObject.SetActive(true);
                button.interactable = editable;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                { if (martialArts.SelectCandidate(selectedSlot, choice)) { RefreshMartialArts(); ShowNotice("草稿已更新，应用后生效"); } });
                SetLabel(button, (current == choice ? "●  " : "") + (choice != null ? choice.DisplayName : "未装备"));
                SetSelected(button, current == choice);
            }
            for (int i = options.Length + 1; i < candidatePool.Count; i++) candidatePool[i].gameObject.SetActive(false);
            if (candidatesContent != null) candidatesContent.sizeDelta = new Vector2(candidatesContent.sizeDelta.x, Mathf.Max(380f, (options.Length + 1) * 70f));
            if (candidatesScroll != null) candidatesScroll.verticalNormalizedPosition = 1f;
        }

        Button CandidateAt(int index)
        {
            if (candidateTemplate == null || candidatesContent == null) return null;
            if (candidatePool.Count == 0)
                for (int i = 0; i < candidatesContent.childCount; i++)
                {
                    var existing = candidatesContent.GetChild(i).GetComponent<Button>();
                    if (existing != null && existing != candidateTemplate && existing.name.StartsWith("Candidate_")) candidatePool.Add(existing);
                }
            while (candidatePool.Count <= index)
            {
                var button = Instantiate(candidateTemplate, candidatesContent);
                button.name = "Candidate_" + candidatePool.Count;
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(0f, -candidatePool.Count * 70f);
                candidatePool.Add(button);
            }
            return candidatePool[index];
        }

        public void ApplyChanges()
        {
            if (martialArts != null && martialArts.ApplyDraft()) ShowNotice("武技装备已应用并保存");
            else ShowNotice("当前无法修改装备，请收招后重新打开设置");
            RefreshMartialArts();
        }
        public void DiscardChanges()
        {
            if (martialArts == null) return;
            bool canEdit = martialArts.CanEditDraft;
            martialArts.DiscardDraft();
            martialArts.BeginDraft(canEdit);
            RefreshMartialArts();
            ShowNotice("未应用的修改已取消");
        }
        public void SetTrainingMode(int mode)
        {
            if (enemy != null) enemy.SetTrainingMode((SamuraiTrainingMode)Mathf.Clamp(mode, 0, 2));
            RefreshTraining();
        }
        public void ResetTraining()
        {
            if (actor != null) actor.ResetCombat();
            if (enemy != null) enemy.ResetTraining();
            if (input != null)
            {
                if (input.targetLock != null) input.targetLock.ClearLock();
                if (input.orbitCamera != null) input.orbitCamera.ResetView();
            }
            if (martialArts != null) { martialArts.DiscardDraft(); martialArts.BeginDraft(); }
            RefreshTraining();
            ShowNotice("双方角色已复位，武技装备保持");
        }
        void RefreshTraining()
        {
            if (toggleTrainingButton != null)
            { toggleTrainingButton.interactable = enemy != null; SetLabel(toggleTrainingButton, enemy != null && enemy.activeTraining ? "对练已开启 · 点击设为木桩" : "静止木桩 · 点击开启对练"); }
            for (int i = 0; trainingModeButtons != null && i < trainingModeButtons.Length; i++)
            { if (trainingModeButtons[i] != null) trainingModeButtons[i].interactable = enemy != null; SetSelected(trainingModeButtons[i], enemy != null && (int)enemy.trainingMode == i); }
            if (trainingStatus != null) trainingStatus.text = enemy == null ? "当前场景没有训练对手" :
                "当前模式：" + (enemy.trainingMode == SamuraiTrainingMode.Normal ? "常规对练" : enemy.trainingMode == SamuraiTrainingMode.TripleCombo ? "固定三连" : "混合弹反") +
                "\n对手状态：" + (enemy.actor != null && enemy.actor.IsDead ? "已击败，可复位" : enemy.activeTraining ? "返回游戏后继续对练" : "静止木桩，仍可受击与死亡");
        }
        public void SetUIScale(float value)
        {
            value = Mathf.Clamp(value, .8f, 1.15f);
            if (scaler != null && baseResolution.x > 0f) scaler.referenceResolution = baseResolution / value;
            if (uiScaleText != null) uiScaleText.text = Mathf.RoundToInt(value * 100f) + "%";
            PlayerPrefs.SetFloat(ScalePreference, value);
        }
        void SamplePerformance()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            sampleSeconds += dt; sampleFrames++; samplePeak = Mathf.Max(samplePeak, dt);
            if (sampleSeconds < .25f) return;
            peaks[peakIndex] = samplePeak; peakIndex = (peakIndex + 1) % peaks.Length;
            float peak = 0f; foreach (float value in peaks) peak = Mathf.Max(peak, value);
            performance = string.Format(CultureInfo.InvariantCulture, "{0:0.0} FPS\n平均帧耗时   {1:0.0} ms\n近 1 秒峰值   {2:0.0} ms", sampleFrames / sampleSeconds, sampleSeconds * 1000f / sampleFrames, peak * 1000f);
            sampleSeconds = samplePeak = 0f; sampleFrames = 0;
        }
        void ShowNotice(string value) { if (noticeText != null) noticeText.text = value; noticeUntil = Time.unscaledTime + 3f; }
        void SetSelected(Button button, bool selected)
        {
            if (button == null) return;
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.color = selected ? activeColor : inactiveColor;
            if (button.image != null) button.image.color = selected ? new Color(.22f, .20f, .14f, .95f) : new Color(.055f, .075f, .085f, .90f);
        }
        static void SetLabel(Button button, string label)
        { if (button != null) { var text = button.GetComponentInChildren<Text>(true); if (text != null) text.text = label; } }
        void OnDisable() => Close();
        void OnDestroy() => Close();
    }
}
