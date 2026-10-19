using UnityEngine;

namespace OCDesign
{
    [DisallowMultipleComponent]
    public sealed class PolearmGripSettings : MonoBehaviour
    {
        [Tooltip("杆身坐标所在物体，握持轴随其模型 Transform 变化。")]
        public Transform shaftSpace;
        public Vector3 shaftOrigin = Vector3.zero;
        public Vector3 shaftAxis = Vector3.up;
        [Tooltip("本武器杆轴上允许左手滑动的范围，单位米。")]
        public Vector2 leftSlideRange = new Vector2(0.12f, 1.42f);
        public bool assistLeftHand = true;
        public bool alignLeftWrist = true;
        [Tooltip("修正薙刀动作的主手腕方向。关闭后完全沿用动画手腕姿态。")]
        public bool assistRightWrist = true;
        public float shaftLength = 2.1f;
        [Tooltip("仅供平地预览使用；不改武器 Transform，只辅助手臂和手腕。")]
        public bool keepAbovePreviewFloor = true;
        public float floorOffset = 0.02f;
        public float lateralClearance = 0.145f;
    }
}
