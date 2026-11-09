using System;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OCDesign.EditorTools
{
    /// <summary>只补固定精英HUD及引用；不移动既有UI，不导入美术，不重建角色或自动保存场景。</summary>
    public static class SamuraiEliteHUDSetup
    {
        const string ScenePath = "Assets/KatanaDemo/SamuraiCombat/Scenes/SamuraiCombatDemo.unity";
        const string ArtPath = "Assets/KatanaDemo/SamuraiCombat/UI/Art/";

        [MenuItem("Katana/Samurai/HUD/Install Fixed Elite HUD In Current Scene")]
        public static void InstallInCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出Play。");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("请打开原SamuraiCombatDemo。");
            var view = UnityEngine.Object.FindAnyObjectByType<SamuraiHUDView>(FindObjectsInactive.Include);
            if (view == null) throw new InvalidOperationException("原场景没有SamuraiHUDView。");
            if (EnsureHud(view)) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Fixed elite HUD configured. Existing UI layout retained; review and save the scene normally.");
        }

        public static bool EnsureHud(SamuraiHUDView view)
        {
            if (view == null || view.combatRoot == null) throw new InvalidOperationException("固定精英HUD需要现有Combat根节点。");
            var canvas = view.canvas != null ? view.canvas : view.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                throw new InvalidOperationException("固定精英条需要当前Screen Space Overlay HUD。");
            var solid = view.healthFill != null && view.healthFill.sprite != null ? view.healthFill.sprite : AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "UI_Solid.png");
            var outline = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "UI_Outline.png");
            var font = view.briefFeedback != null && view.briefFeedback.font != null ? view.briefFeedback.font : AssetDatabase.LoadAssetAtPath<Font>(
                "Assets/KatanaDemo/SamuraiCombat/UI/Fonts/NotoSansSC.ttf");
            if (solid == null || outline == null || font == null) throw new InvalidOperationException("原HUD的图片或字体缺失。");
            Undo.RecordObject(view, "Configure fixed elite HUD");
            bool changed = false;
            var root = view.eliteRoot != null ? view.eliteRoot : view.combatRoot.transform.Find("EliteBossHUD") as RectTransform;
            if (root == null)
            {
                root = NewRect("EliteBossHUD", view.combatRoot.transform, 0, 0, 760, 56);
                root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
                // 左下窄条避开中央玩家；参考x28~788，捕获提示从x790开始，右下姿态保持。
                root.anchoredPosition = new Vector2(28f, 24f);
                root.SetAsLastSibling(); root.gameObject.SetActive(false); changed = true;
            }
            changed |= Assign(ref view.eliteRoot, root);
            var ink = new Color(.025f, .043f, .052f, .92f);
            var ivory = new Color(.96f, .91f, .78f, 1f);
            EnsureImage(root, "Backdrop", solid, 0, 0, 760, 56, ink, false, ref changed);
            EnsureImage(root, "Border", outline, 0, 0, 760, 56, view.goldColor, false, ref changed);
            var name = EnsureText(root, "EliteName", font, 14, 0, 380, 30, 22, ivory, TextAnchor.MiddleLeft, ref changed);
            var healthText = EnsureText(root, "EliteHealthValue", font, 402, 3, 160, 24, 16, ivory, TextAnchor.MiddleRight, ref changed);
            var hp = EnsureRect(root, "EliteHealth", 14, 30, 732, 12, ref changed);
            EnsureImage(hp, "Track", solid, 0, 0, 732, 12, new Color(.08f, .04f, .035f, 1f), false, ref changed);
            EnsureImage(hp, "Border", outline, 0, 0, 732, 12, view.goldColor, false, ref changed);
            var damage = EnsureImage(hp, "DamageTrail", solid, 2, 2, 728, 8, view.healthDamageColor, true, ref changed);
            var health = EnsureImage(hp, "HealthFill", solid, 2, 2, 728, 8, view.healthColor, true, ref changed);
            var tough = EnsureRect(root, "EliteToughness", 14, 48, 732, 4, ref changed);
            EnsureImage(tough, "Track", solid, 0, 0, 732, 4, ink, false, ref changed);
            var toughness = EnsureImage(tough, "ToughnessFill", solid, 1, 1, 730, 2, view.goldColor, true, ref changed);
            var toughnessText = EnsureText(root, "EliteToughnessValue", font, 574, 3, 168, 24, 16,
                view.goldColor, TextAnchor.MiddleRight, ref changed);
            changed |= Assign(ref view.eliteName, name); changed |= Assign(ref view.eliteHealthText, healthText);
            changed |= Assign(ref view.eliteHealthFill, health); changed |= Assign(ref view.eliteHealthDamageFill, damage);
            changed |= Assign(ref view.eliteToughnessFill, toughness); changed |= Assign(ref view.eliteToughnessText, toughnessText);
            if (changed) EditorUtility.SetDirty(view);
            return changed;
        }

        static bool Assign<T>(ref T field, T value) where T : UnityEngine.Object
        {
            if (field == value) return false; field = value; return true;
        }

        static RectTransform NewRect(string name, Transform parent, float x, float y, float width, float height)
        {
            var root = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Add fixed elite HUD element");
            var rect = root.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static RectTransform EnsureRect(Transform parent, string name, float x, float y, float width, float height, ref bool changed)
        {
            var current = parent.Find(name) as RectTransform;
            if (current != null) return current;
            changed = true; return NewRect(name, parent, x, y, width, height);
        }

        static Image EnsureImage(Transform parent, string name, Sprite sprite, float x, float y, float width,
            float height, Color color, bool fill, ref bool changed)
        {
            var rect = EnsureRect(parent, name, x, y, width, height, ref changed);
            var image = rect.GetComponent<Image>();
            if (image != null) return image;
            image = Undo.AddComponent<Image>(rect.gameObject); image.sprite = sprite;
            image.color = color; image.raycastTarget = false;
            if (fill) { image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal; image.fillOrigin = 0; }
            else if (name == "Border") image.type = Image.Type.Sliced;
            changed = true; return image;
        }

        static Text EnsureText(Transform parent, string name, Font font, float x, float y, float width,
            float height, int size, Color color, TextAnchor alignment, ref bool changed)
        {
            var rect = EnsureRect(parent, name, x, y, width, height, ref changed);
            var text = rect.GetComponent<Text>();
            if (text != null) return text;
            text = Undo.AddComponent<Text>(rect.gameObject); text.font = font; text.fontSize = size;
            text.color = color; text.alignment = alignment; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Truncate;
            changed = true; return text;
        }
    }
}
