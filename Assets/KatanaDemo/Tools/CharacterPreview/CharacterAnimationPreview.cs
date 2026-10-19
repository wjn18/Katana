using UnityEngine;
using UnityEngine.InputSystem;

namespace OCDesign.Preview
{
    // Reusable controls owned by the permanent character preview scene.
    public sealed class CharacterAnimationPreview : MonoBehaviour
    {
        public Animator target;
        public Camera previewCamera;
        public string[] states = { "Idle", "Walk", "Run", "Attack01", "Attack03" };
        public int selected;
        public bool paused;
        public float playbackSpeed = 1f;
        private float cameraYaw = 35f;

        private void Start()
        {
            if (!previewCamera) previewCamera = Camera.main;
            Select(0);
        }

        public void Select(int index)
        {
            if (!target || index < 0 || index >= states.Length) return;
            selected = index;
            target.Play(states[index], 0, 0f);
            target.speed = paused ? 0f : playbackSpeed;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) Select(0);
            if (kb.digit2Key.wasPressedThisFrame) Select(1);
            if (kb.digit3Key.wasPressedThisFrame) Select(2);
            if (kb.digit4Key.wasPressedThisFrame) Select(3);
            if (kb.digit5Key.wasPressedThisFrame) Select(4);
            if (kb.spaceKey.wasPressedThisFrame) paused = !paused;
            if (kb.rKey.wasPressedThisFrame) Select(selected);
            if (kb.leftArrowKey.isPressed) cameraYaw -= 60f * Time.deltaTime;
            if (kb.rightArrowKey.isPressed) cameraYaw += 60f * Time.deltaTime;
        }

        private void LateUpdate()
        {
            if (target) target.speed = paused ? 0f : playbackSpeed;
            if (!previewCamera) return;
            bool jumpAttack = selected == 4;
            var focus = transform.position + Vector3.up * (jumpAttack ? 1.3f : 0.85f);
            var offset = jumpAttack ? new Vector3(0f, 1.1f, 5.4f) : new Vector3(0f, 0.65f, 4f);
            previewCamera.transform.position = focus + Quaternion.Euler(0f, cameraYaw, 0f) * offset;
            previewCamera.transform.LookAt(focus);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(14, 14, 470, 190), GUI.skin.box);
            GUILayout.Label("Humanoid / Naginata motions");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < states.Length; i++)
                if (GUILayout.Button((i + 1) + " " + states[i])) Select(i);
            GUILayout.EndHorizontal();
            GUILayout.Label("Current: " + states[selected] + " | Space: pause | R: restart");
            paused = GUILayout.Toggle(paused, "Pause");
            GUILayout.Label("Speed: " + playbackSpeed.ToString("F2") + "x");
            playbackSpeed = GUILayout.HorizontalSlider(playbackSpeed, 0.1f, 1.5f);
            GUILayout.Label("Left / Right arrows: orbit camera. Root motion OFF.");
            GUILayout.Label("Two-palm weapon alignment; no finger or cloth correction.");
            GUILayout.EndArea();
        }
    }
}
