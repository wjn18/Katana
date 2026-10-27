using OCDesign.Samurai;
using UnityEngine;
using UnityEngine.UI;

namespace OCDesign.EditorTools
{
    /// <summary>只在编辑器装配时运行。所有固定节点和布局都保存在原场景，可直接调整。</summary>
    public static class SamuraiSettingsUIBuilder
    {
        static readonly Color Ink = new Color(.035f, .055f, .065f, .98f);
        static readonly Color Gold = new Color(.86f, .71f, .42f, 1f);
        static readonly Color White = new Color(.93f, .91f, .84f, 1f);
        static readonly Color Muted = new Color(.63f, .68f, .70f, 1f);

        public static SamuraiSettingsUI Build(Transform parent, GameObject combatRoot,
            SamuraiCombatant actor, SamuraiPlayerInput input, SamuraiTrainingEnemy enemy,
            Font font, Sprite panelSprite, Sprite buttonSprite)
        {
            var root = Stretch("SettingsController", parent);
            var ui = root.gameObject.AddComponent<SamuraiSettingsUI>();
            ui.actor = actor; ui.input = input; ui.enemy = enemy; ui.combatRoot = combatRoot;
            ui.martialArts = actor != null ? actor.GetComponent<SamuraiMartialArtMenu>() : null;
            if (actor != null && ui.martialArts == null) ui.martialArts = actor.gameObject.AddComponent<SamuraiMartialArtMenu>();
            if (ui.martialArts != null) ui.martialArts.Configure(actor, input);
            var overlay = Stretch("SettingsOverlay", root);
            ImageFor(overlay, new Color(.005f, .012f, .018f, .87f), true);
            ui.panelRoot = overlay.gameObject;
            var panel = Rect("SettingsPanel", overlay, 1400f, 850f, Vector2.zero, true);
            ImageFor(panel, Ink, true);
            if (panelSprite != null)
            {
                var art = Stretch("PanelArtwork", panel);
                var image = ImageFor(art, Color.white, false); image.sprite = panelSprite; image.type = Image.Type.Sliced;
            }
            Border(panel, 1400f, 850f, Gold);
            Label("Title", panel, font, "设 置", 0, 26, 1400, 56, 42, White, TextAnchor.MiddleCenter);
            Label("PauseState", panel, font, "已暂停", 0, 88, 1400, 30, 21, Muted, TextAnchor.MiddleCenter);
            string[] tabs = { "操作说明", "武技配置", "训练设置", "显示设置" };
            ui.tabButtons = new Button[4]; ui.tabUnderlines = new Image[4]; ui.pages = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                ui.tabButtons[i] = MakeButton("Tab_" + i + "_" + tabs[i], panel, font, tabs[i], 60 + i * 320, 136, 320, 56, 25, null);
                var underline = Rect("SelectedUnderline", ui.tabButtons[i].transform, 276, 3, new Vector2(22, 53));
                ui.tabUnderlines[i] = ImageFor(underline, Gold, false); ui.tabUnderlines[i].enabled = i == 0;
                ui.pages[i] = Rect("Page_" + i + "_" + tabs[i], panel, 1260, 500, new Vector2(70, 222)).gameObject;
                ui.pages[i].SetActive(i == 0);
            }
            Line(panel, "TabDivider", 60, 196, 1280, 1, new Color(.48f, .42f, .29f, .7f));
            BuildHelp(ui, ui.pages[0].transform, font);
            BuildArts(ui, ui.pages[1].transform, font, buttonSprite);
            BuildTraining(ui, ui.pages[2].transform, font, buttonSprite);
            BuildDisplay(ui, ui.pages[3].transform, font);
            Line(panel, "FooterDivider", 60, 748, 1280, 1, Gold);
            ui.noticeText = Label("Notice", panel, font, "游戏已暂停  ·  Esc 返回游戏", 70, 771, 950, 40, 20, Muted);
            ui.resumeButton = MakeButton("ReturnToGame", panel, font, "返回游戏    Esc", 1050, 770, 280, 54, 23, buttonSprite);
            overlay.gameObject.SetActive(false);
            return ui;
        }

        static void BuildHelp(SamuraiSettingsUI ui, Transform page, Font font)
        {
            string[,] commands = {
                { "W A S D / 方向键", "移动" }, { "Shift / 空格", "奔跑 / 闪避" },
                { "鼠标左键", "普通攻击 · 逐次接段" }, { "鼠标右键", "短按强击 / 长按蓄力居合" },
                { "F", "按准时机弹反 / 持续防御" }, { "Q", "收招时残心，回收精力" },
                { "1 / 2", "高架势 / 中架势" }, { "Tab / 鼠标中键", "锁定 / 取消目标" },
                { "鼠标 / 滚轮", "环视 / 缩放" }, { "M / Esc", "武技配置 / 设置" }
            };
            for (int i = 0; i < commands.GetLength(0); i++)
            {
                float y = i * 48f;
                var key = Rect("InputKey_" + i, page, 220, 37, new Vector2(0, y + 2));
                ImageFor(key, new Color(.025f, .035f, .04f, .8f), false);
                Border(key, 220, 37, new Color(.55f, .52f, .42f, 1f));
                Label("Key", key, font, commands[i, 0], 8, 0, 204, 37, 19, White, TextAnchor.MiddleCenter);
                Label("Action_" + i, page, font, commands[i, 1], 244, y, 350, 42, 21, White);
                Line(page, "Rule_" + i, 0, y + 45, 605, 1, new Color(.22f, .27f, .28f, .65f));
            }
            Line(page, "ColumnDivider", 644, 0, 1, 478, new Color(.47f, .40f, .28f, .6f));
            Label("ComboHeading", page, font, "招式衔接", 684, 0, 576, 36, 25, Gold);
            Label("ComboHelp", page, font,
                "高架势：普攻接段短右键接踢击；长按右键 0.25 秒接旋风斩。\n中架势：短右键依次接四段强击；及时衔接普 4 → 强 4、普 5 → 强 2，其余时机收势后从强 1 起手。",
                684, 48, 576, 148, 20, White, TextAnchor.UpperLeft);
            Label("MartialHelp", page, font,
                "F + 左键 / 右键释放已装备武技。\n独立长按右键只蓄力居合，松开释放；奔跑 / 闪避后左键可追击。Q + 空格当前暂留空。",
                684, 208, 576, 104, 20, White, TextAnchor.UpperLeft);
            ui.artsHelpText = Label("ArtsHelp", page, font,
                "琢技\n命中、防御与弹反积累圆环；满槽后以强击或武技自动开启强化。金色圆环显示剩余时间。",
                684, 328, 576, 164, 20, Gold, TextAnchor.UpperLeft);
        }

        static void BuildArts(SamuraiSettingsUI ui, Transform page, Font font, Sprite buttonSprite)
        {
            ui.stanceButtons = new Button[2];
            ui.stanceButtons[0] = MakeButton("Stance_High", page, font, "高架势", 0, 0, 200, 46, 23, buttonSprite);
            ui.stanceButtons[1] = MakeButton("Stance_Middle", page, font, "中架势", 212, 0, 200, 46, 23, buttonSprite);
            Label("DraftHelp", page, font, "两种架势独立装备 · 选择只写入草稿", 440, 3, 820, 40, 20, Muted);
            ui.slotButtons = new Button[SamuraiMartialArtLoadout.DisplaySlots.Length];
            for (int i = 0; i < ui.slotButtons.Length; i++)
                ui.slotButtons[i] = MakeButton("Slot_" + SamuraiMartialArtLoadout.DisplaySlots[i], page, font,
                    SamuraiMartialArtCommands.InputLabel(SamuraiMartialArtLoadout.DisplaySlots[i]) + "\n未装备",
                    0, 64 + i * 62, 510, 56, 18, buttonSprite, TextAnchor.MiddleLeft);
            Line(page, "ColumnDivider", 540, 64, 1, 368, new Color(.47f, .40f, .28f, .7f));
            ui.candidatesTitle = Label("CandidatesTitle", page, font, "候选武技", 574, 62, 686, 38, 22, Gold);
            ui.fixedBranchText = Label("FixedStrongBranchHelp", page, font,
                "普攻接段窗口内短按右键：\n\n及时接第 4 击 → 强击第 4 式收尾\n及时接第 5 击 → 强击第 2 → 3 → 4 式\n其余各击 / 错过接点 → 收势后从强 1 起手\n\n强击依次短按右键续接，末式结束不循环。\n此衔接为固定分支，不占用可装备武技。",
                574, 116, 686, 304, 21, White, TextAnchor.UpperLeft);
            ui.fixedBranchText.gameObject.SetActive(false);
            var scroll = Rect("CandidateScroll", page, 686, 320, new Vector2(574, 112));
            var scrollImage = ImageFor(scroll, new Color(.025f, .04f, .05f, .6f), true);
            ui.candidatesScroll = scroll.gameObject.AddComponent<ScrollRect>();
            ui.candidatesScroll.horizontal = false; ui.candidatesScroll.vertical = true;
            ui.candidatesScroll.scrollSensitivity = 30f; ui.candidatesScroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Stretch("Viewport", scroll); viewport.gameObject.AddComponent<RectMask2D>();
            ui.candidatesContent = Rect("Candidates", viewport, 668, 380, new Vector2(8, 0));
            ui.candidatesScroll.viewport = viewport; ui.candidatesScroll.content = ui.candidatesContent;
            ui.candidateTemplate = MakeButton("CandidateTemplate", ui.candidatesContent, font, "武技候选", 0, 0, 668, 60, 22, buttonSprite, TextAnchor.MiddleLeft);
            ui.candidateTemplate.gameObject.SetActive(false);
            ui.defaultsButton = MakeButton("RestoreDefaults", page, font, "恢复默认（草稿）", 0, 450, 280, 48, 21, buttonSprite);
            ui.discardButton = MakeButton("DiscardDraft", page, font, "取消草稿", 694, 450, 260, 48, 21, buttonSprite);
            ui.applyButton = MakeButton("ApplyAndSave", page, font, "应用并保存", 974, 450, 286, 48, 22, buttonSprite);
        }

        static void BuildTraining(SamuraiSettingsUI ui, Transform page, Font font, Sprite buttonSprite)
        {
            Label("TrainingHeading", page, font, "训练对手", 0, 0, 1260, 44, 28, Gold);
            ui.toggleTrainingButton = MakeButton("ToggleTraining", page, font, "对练已开启 · 点击设为木桩", 0, 66, 510, 58, 22, buttonSprite);
            Label("ToggleHint", page, font, "游戏内快捷键：T\n木桩停止主动进攻，仍可受击与死亡。", 558, 66, 702, 72, 21, Muted, TextAnchor.UpperLeft);
            string[] names = { "常规对练", "固定三连", "混合弹反" };
            string[] descriptions = { "观察间距、接近、攻击与防御", "三次连续攻击，练习逐击弹反", "单刀、二连与延迟重击交替" };
            ui.trainingModeButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                ui.trainingModeButtons[i] = MakeButton("TrainingMode_" + i, page, font, names[i], i * 432, 170, 396, 56, 24, buttonSprite);
                Label("ModeDescription_" + i, page, font, descriptions[i], i * 432, 240, 396, 70, 20, White, TextAnchor.UpperLeft);
            }
            Label("ModeKey", page, font, "游戏内快捷键：G 切换模式", 0, 330, 650, 36, 20, Muted);
            ui.trainingStatus = Label("TrainingStatus", page, font, "当前模式：常规对练\n返回游戏后继续", 0, 382, 850, 90, 22, White, TextAnchor.UpperLeft);
            ui.resetTrainingButton = MakeButton("ResetArena", page, font, "复位双方角色    R", 914, 410, 346, 60, 23, buttonSprite);
        }

        static void BuildDisplay(SamuraiSettingsUI ui, Transform page, Font font)
        {
            Label("ScaleHeading", page, font, "界面缩放", 0, 0, 700, 44, 28, Gold);
            Label("ScaleHint", page, font, "同时调整战斗 HUD 与设置界面", 0, 58, 700, 36, 21, Muted);
            var sliderRoot = Rect("UIScale", page, 570, 50, new Vector2(0, 128));
            var track = Rect("Track", sliderRoot, 570, 8, new Vector2(0, 21)); ImageFor(track, new Color(.20f, .25f, .27f, 1f), true);
            var fillArea = Stretch("FillArea", track); fillArea.offsetMin = new Vector2(7, 0); fillArea.offsetMax = new Vector2(-7, 0);
            var fill = Stretch("Fill", fillArea); ImageFor(fill, Gold, false);
            var handleArea = Stretch("HandleArea", sliderRoot); handleArea.offsetMin = new Vector2(8, 0); handleArea.offsetMax = new Vector2(-8, 0);
            var handle = Rect("Handle", handleArea, 18, 30, Vector2.zero, true); var handleImage = ImageFor(handle, White, true);
            ui.uiScaleSlider = sliderRoot.gameObject.AddComponent<Slider>();
            ui.uiScaleSlider.minValue = .8f; ui.uiScaleSlider.maxValue = 1.15f; ui.uiScaleSlider.value = 1f;
            ui.uiScaleSlider.fillRect = fill; ui.uiScaleSlider.handleRect = handle; ui.uiScaleSlider.targetGraphic = handleImage;
            ui.uiScaleText = Label("ScaleValue", page, font, "100%", 610, 128, 160, 50, 28, White);
            Label("ScaleMinimum", page, font, "80%", 0, 186, 180, 32, 18, Muted);
            Label("ScaleMaximum", page, font, "115%", 450, 186, 120, 32, 18, Muted, TextAnchor.MiddleRight);
            Label("DisplayHeading", page, font, "显示信息", 0, 276, 700, 42, 25, Gold);
            ui.displayText = Label("DisplayInformation", page, font, "", 0, 340, 700, 120, 24, White, TextAnchor.UpperLeft);
            Line(page, "ColumnDivider", 810, 0, 1, 480, new Color(.47f, .40f, .28f, .6f));
            Label("PerformanceHeading", page, font, "性能", 866, 0, 394, 44, 28, Gold);
            ui.performanceText = Label("PerformanceCounters", page, font, "正在采样帧率…", 866, 76, 394, 230, 26, White, TextAnchor.UpperLeft);
            Label("PerformanceNote", page, font, "按真实渲染帧采样\n\n帧率与性能信息仅在此页显示。", 866, 332, 394, 130, 21, Muted, TextAnchor.UpperLeft);
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
        static RectTransform Rect(string name, Transform parent, float width, float height, Vector2 position, bool centered = false)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = centered ? new Vector2(.5f, .5f) : new Vector2(0f, 1f);
            rect.pivot = centered ? new Vector2(.5f, .5f) : new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = centered ? position : new Vector2(position.x, -position.y);
            return rect;
        }
        static Image ImageFor(RectTransform rect, Color color, bool raycast)
        { var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image; }
        static Text Label(string name, Transform parent, Font font, string value, float x, float y, float w, float h,
            int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var rect = Rect(name, parent, w, h, new Vector2(x, y)); var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color; text.alignment = anchor;
            text.raycastTarget = false; text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate; text.lineSpacing = 1.12f;
            return text;
        }
        static Button MakeButton(string name, Transform parent, Font font, string value, float x, float y,
            float w, float h, int size, Sprite sprite, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var rect = Rect(name, parent, w, h, new Vector2(x, y));
            var image = ImageFor(rect, new Color(.055f, .075f, .085f, .9f), true);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.55f, 1.48f, 1.28f, 1f);
            colors.pressedColor = new Color(.8f, .75f, .6f, 1f); colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.55f, .55f, .55f, .5f); colors.fadeDuration = .08f; button.colors = colors;
            if (sprite != null)
            {
                var frame = Stretch("ArtworkFrame", rect); var frameImage = ImageFor(frame, new Color(.85f, .76f, .55f, .85f), false);
                frameImage.sprite = sprite; frameImage.type = Image.Type.Sliced;
            }
            else Border(rect, w, h, new Color(.38f, .37f, .30f, .8f));
            Label("Label", rect, font, value, 16, 1, w - 32, h - 2, size, White, anchor);
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            return button;
        }
        static void Line(Transform parent, string name, float x, float y, float w, float h, Color color)
        { ImageFor(Rect(name, parent, w, h, new Vector2(x, y)), color, false); }
        static void Border(Transform parent, float width, float height, Color color)
        {
            Line(parent, "BorderTop", 0, 0, width, 1, color); Line(parent, "BorderBottom", 0, height - 1, width, 1, color);
            Line(parent, "BorderLeft", 0, 0, 1, height, color); Line(parent, "BorderRight", width - 1, 0, 1, height, color);
        }
    }
}
