using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OCDesign.LookDev
{
    /// <summary>Local camera and sunlight controls for the independent comic lighting sample.</summary>
    [DisallowMultipleComponent]
    public sealed class ComicLightingViewer : MonoBehaviour
    {
        public Camera viewCamera;
        public Transform sun;
        public Transform focusTarget;
        public Material[] paletteMaterials;
        public Vector3 focusPoint = new Vector3(0f, 1.7f, 2f);
        public Vector3 closePosition = new Vector3(4.5f, 2.9f, -5f);
        public Vector3 closeFocus = new Vector3(0f, 1.3f, 1f);
        public Vector3 overviewPosition = new Vector3(11f, 14f, -12f);
        public Vector3 overviewFocus = new Vector3(0f, 0.8f, 3f);
        [Min(0.01f)] public float orbitSensitivity = 0.18f;
        [Min(0.1f)] public float moveSpeed = 4f;
        public bool showHelp = true;

        Vector3 pivot, initialPosition, initialPivot;
        Quaternion initialRotation, initialSunRotation;
        bool initialized, rotateSun;
        string viewName = "庭院全景";
        GUIStyle titleStyle, helpStyle;

        void Start() => Initialize();

        void Initialize()
        {
            if (initialized) return;
            if (!viewCamera) viewCamera = GetComponent<Camera>();
            if (!viewCamera) viewCamera = Camera.main;
            if (!viewCamera) return;
            pivot = focusTarget ? focusTarget.position : focusPoint;
            initialPivot = pivot;
            initialPosition = viewCamera.transform.position;
            initialRotation = viewCamera.transform.rotation;
            if (sun) initialSunRotation = sun.rotation;
            initialized = true;
        }

        void Update()
        {
            Initialize();
            if (!initialized) return;
            Vector2 drag = Vector2.zero;
            Vector3 movement = Vector3.zero;
            float scroll = 0f;
            bool orbit = false, fast = false;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null)
            {
                movement.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                movement.z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                movement.y = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
                fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                if (keyboard.digit1Key.wasPressedThisFrame) SetView(1);
                if (keyboard.digit2Key.wasPressedThisFrame) SetView(2);
                if (keyboard.digit3Key.wasPressedThisFrame) SetView(3);
                if (keyboard.rKey.wasPressedThisFrame) Restore();
                if (keyboard.lKey.wasPressedThisFrame) rotateSun = !rotateSun;
                if (keyboard.hKey.wasPressedThisFrame) showHelp = !showHelp;
            }
            if (mouse != null)
            {
                orbit = mouse.rightButton.isPressed;
                drag = mouse.delta.ReadValue();
                scroll = mouse.scroll.ReadValue().y / 120f;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            movement = new Vector3(Input.GetAxisRaw("Horizontal"),
                (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f), Input.GetAxisRaw("Vertical"));
            fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            orbit = Input.GetMouseButton(1);
            drag = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
            scroll = Input.mouseScrollDelta.y;
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetView(1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetView(2);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetView(3);
            if (Input.GetKeyDown(KeyCode.R)) Restore();
            if (Input.GetKeyDown(KeyCode.L)) rotateSun = !rotateSun;
            if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp;
#endif
            var cameraTransform = viewCamera.transform;
            if (movement.sqrMagnitude > 0f)
            {
                var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                var right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
                var offset = (right * movement.x + forward * movement.z + Vector3.up * movement.y).normalized;
                offset *= moveSpeed * (fast ? 3f : 1f) * Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                pivot += offset;
                cameraTransform.position += offset;
            }
            if (orbit && drag.sqrMagnitude > 0f)
            {
                Vector3 offset = cameraTransform.position - pivot;
                float distance = Mathf.Max(offset.magnitude, 0.5f);
                float yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg + drag.x * orbitSensitivity;
                float elevation = Mathf.Asin(Mathf.Clamp(offset.y / distance, -1f, 1f)) * Mathf.Rad2Deg;
                elevation = Mathf.Clamp(elevation - drag.y * orbitSensitivity, -10f, 85f);
                float radius = distance * Mathf.Cos(elevation * Mathf.Deg2Rad);
                cameraTransform.position = pivot + new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad) * radius,
                    Mathf.Sin(elevation * Mathf.Deg2Rad) * distance, Mathf.Cos(yaw * Mathf.Deg2Rad) * radius);
                cameraTransform.LookAt(pivot);
            }
            if (Mathf.Abs(scroll) > 0.001f)
            {
                Vector3 offset = cameraTransform.position - pivot;
                cameraTransform.position = pivot + offset.normalized * Mathf.Clamp(offset.magnitude * Mathf.Pow(0.88f, scroll), 1.2f, 50f);
            }
            if (rotateSun && sun) sun.Rotate(Vector3.up, 8f * Mathf.Min(Time.unscaledDeltaTime, 0.1f), Space.World);
        }

        /// <summary>1 = saved opening view, 2 = character close view, 3 = overhead light study.</summary>
        public void SetView(int index)
        {
            Initialize();
            if (!initialized) return;
            if (index == 1)
            {
                pivot = initialPivot;
                viewCamera.transform.SetPositionAndRotation(initialPosition, initialRotation);
                viewName = "庭院全景";
                return;
            }
            if (index != 2 && index != 3) return;
            pivot = index == 2 ? closeFocus : overviewFocus;
            viewCamera.transform.position = index == 2 ? closePosition : overviewPosition;
            viewCamera.transform.LookAt(pivot);
            viewName = index == 2 ? "角色近景" : "光影俯览";
        }

        public void Restore()
        {
            SetView(1);
            rotateSun = false;
            if (sun) sun.rotation = initialSunRotation;
        }

        void OnGUI()
        {
            if (!showHelp) return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
                titleStyle.normal.textColor = new Color(0.96f, 0.90f, 0.79f);
                helpStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                helpStyle.normal.textColor = new Color(0.86f, 0.88f, 0.91f);
            }
            float width = Mathf.Min(760f, Screen.width - 32f);
            Rect box = new Rect(16f, Screen.height - 66f, width, 50f);
            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.11f, 0.15f, 0.82f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(box.x + 12f, box.y + 3f, width - 24f, 22f), "漫画光影  /  " + viewName + (rotateSun ? "  ·  日光旋转中" : ""), titleStyle);
            GUI.Label(new Rect(box.x + 12f, box.y + 25f, width - 24f, 22f), "右键拖动环绕 · 滚轮远近 · WASD 平移 · Q/E 升降 · 1/2/3 视角 · L 日光 · R 复位 · H 隐藏", helpStyle);
        }
    }
}
