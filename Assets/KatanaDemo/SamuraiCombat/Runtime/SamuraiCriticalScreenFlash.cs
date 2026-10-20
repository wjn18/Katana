using UnityEngine;
using UnityEngine.UI;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 玩家完美蓄力释放时的短暂屏幕裂纹。只订阅出招事件，不参与伤害或输入。
    /// Canvas和材质只在运行时生成，禁用时销毁，不写入Prefab。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SamuraiCriticalScreenFlash : MonoBehaviour
    {
        [SerializeField] SamuraiCombatant actor;
        [SerializeField] Material flashMaterial;
        [SerializeField, Range(.2f, .8f)] float duration = .5f;
        [SerializeField, Range(0f, 1.5f)] float strength = 1f;
        [SerializeField, Range(0f, .15f)] float edgeDarkening = .065f;
        [SerializeField] int canvasSortingOrder = -100;

        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int StrengthId = Shader.PropertyToID("_Strength");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int EdgeDarkeningId = Shader.PropertyToID("_EdgeDarkening");

        SamuraiCombatant subscribedActor;
        GameObject canvasObject;
        Canvas canvas;
        RawImage overlay;
        Material runtimeMaterial;
        float elapsed;
        int flashSerial = -1;
        int lastStartedSerial = -1;
        bool playing;

        public bool IsVisible => playing && overlay != null && overlay.enabled
            && overlay.gameObject.activeInHierarchy;
        public float FlashProgress => playing ? Mathf.Clamp01(elapsed / SafeDuration) : 0f;
        float SafeDuration => Finite(duration) ? Mathf.Clamp(duration, .2f, .8f) : .5f;

        public void Configure(SamuraiCombatant targetActor, Material material)
        {
            Unsubscribe();
            ReleaseRuntimeObjects();
            actor = targetActor;
            flashMaterial = material;
            lastStartedSerial = -1;
            if (isActiveAndEnabled) Subscribe();
        }

        void OnEnable() { ClearFlash(); lastStartedSerial = -1; Subscribe(); }
        void OnDisable() { Unsubscribe(); ReleaseRuntimeObjects(); }
        void OnDestroy() { Unsubscribe(); ReleaseRuntimeObjects(); }
        void Update() => SampleNow(Time.deltaTime);

        void Subscribe()
        {
            if (subscribedActor == actor) return;
            Unsubscribe();
            subscribedActor = actor;
            if (subscribedActor == null) return;
            subscribedActor.CriticalAttackStarted += PlayFlash;
            subscribedActor.StateChanged += OnStateChanged;
        }

        void Unsubscribe()
        {
            if (subscribedActor != null)
            {
                subscribedActor.CriticalAttackStarted -= PlayFlash;
                subscribedActor.StateChanged -= OnStateChanged;
            }
            subscribedActor = null;
        }

        void OnStateChanged(string state)
        {
            // CommitAttack先进入Charged状态，再锁定本次暴击并发事件。
            // 不按状态名称清除刚开始的特效；实际中断由状态/序号判断。
            if (playing && (actor == null || !actor.IsAttacking
                || actor.Actions == null || actor.Actions.AttackSerial != flashSerial))
                ClearFlash();
        }

        /// <summary>只接受已成功提交的暴击攻击，同一出招序号最多播放一次。</summary>
        public void PlayFlash()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || actor == null
                || !actor.isActiveAndEnabled || !actor.IsAttacking || !actor.IsCriticalAttack
                || actor.Actions == null || flashMaterial == null
                || actor.Actions.AttackSerial == lastStartedSerial)
                return;
            if (!EnsureOverlay()) return;
            flashSerial = lastStartedSerial = actor.Actions.AttackSerial;
            elapsed = 0f;
            playing = true;
            overlay.enabled = true;
            runtimeMaterial.SetFloat(SeedId, (flashSerial % 127) * 1.379f + 4.17f);
            UpdateMaterial();
        }

        /// <summary>与Update相同的缩放时间推进；暂停时传0，不推进也不闪烁。</summary>
        public void SampleNow(float deltaSeconds)
        {
            if (subscribedActor != actor)
            {
                ClearFlash();
                lastStartedSerial = -1;
                if (isActiveAndEnabled) Subscribe();
            }
            if (!playing) return;
            if (!isActiveAndEnabled || actor == null || !actor.isActiveAndEnabled
                || !actor.IsAttacking || !actor.IsCriticalAttack || actor.Actions == null
                || actor.Actions.AttackSerial != flashSerial || flashMaterial == null
                || !Finite(deltaSeconds) || deltaSeconds < 0f)
            { ClearFlash(); return; }
            elapsed += deltaSeconds;
            if (elapsed >= SafeDuration) { ClearFlash(); return; }
            UpdateMaterial();
        }

        public void ClearFlash()
        {
            playing = false;
            elapsed = 0f;
            flashSerial = -1;
            if (overlay != null) overlay.enabled = false;
            if (runtimeMaterial != null) runtimeMaterial.SetFloat(StrengthId, 0f);
        }

        bool EnsureOverlay()
        {
            if (overlay != null && runtimeMaterial != null) return true;
            ReleaseRuntimeObjects();
            if (flashMaterial == null || flashMaterial.shader == null || !flashMaterial.shader.isSupported)
                return false;
            runtimeMaterial = new Material(flashMaterial)
            {
                name = "Samurai critical screen (runtime)",
                hideFlags = HideFlags.DontSave
            };
            canvasObject = new GameObject("Samurai critical screen (runtime)", typeof(RectTransform), typeof(Canvas));
            canvasObject.hideFlags = HideFlags.DontSave;
            canvasObject.layer = 5;
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = canvasSortingOrder;
            canvas.pixelPerfect = false;
            var imageObject = new GameObject("Lightning cracks", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imageObject.hideFlags = HideFlags.DontSave;
            imageObject.layer = 5;
            imageObject.transform.SetParent(canvasObject.transform, false);
            overlay = imageObject.GetComponent<RawImage>();
            overlay.texture = Texture2D.whiteTexture;
            overlay.material = runtimeMaterial;
            overlay.color = Color.white;
            overlay.raycastTarget = false;
            var rect = overlay.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            overlay.enabled = false;
            // 不添加GraphicRaycaster或EventSystem，屏幕效果不接收任何输入。
            return true;
        }

        void UpdateMaterial()
        {
            if (runtimeMaterial == null) return;
            runtimeMaterial.SetFloat(ProgressId, FlashProgress);
            runtimeMaterial.SetFloat(StrengthId, Finite(strength) ? Mathf.Clamp(strength, 0f, 1.5f) : 1f);
            runtimeMaterial.SetFloat(AspectId, Mathf.Max(.25f, Screen.width / (float)Mathf.Max(1, Screen.height)));
            runtimeMaterial.SetFloat(EdgeDarkeningId,
                Finite(edgeDarkening) ? Mathf.Clamp(edgeDarkening, 0f, .15f) : .065f);
            if (canvas != null) canvas.sortingOrder = canvasSortingOrder;
        }

        void ReleaseRuntimeObjects()
        {
            ClearFlash();
            // 先停用，避免Destroy延后至帧尾时与新配置出现两层重叠。
            if (canvasObject != null) canvasObject.SetActive(false);
            if (canvasObject != null) DestroyOwned(canvasObject);
            if (runtimeMaterial != null) DestroyOwned(runtimeMaterial);
            canvasObject = null;
            canvas = null;
            overlay = null;
            runtimeMaterial = null;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
