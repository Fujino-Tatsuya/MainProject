using UnityEditor;
using UnityEngine;

// Animation 창은 씬에서 선택된 오브젝트 하나만 스크럽 프리뷰할 수 있어서(전역 AnimationMode 단일 컨텍스트),
// 서로 다른 두 오브젝트의 서로 다른 클립을 동시에 특정 프레임으로 정지시켜 비교할 방법이 없었다.
// AnimationMode.SampleAnimationClip은 같은 샘플링 구간 안에서 여러 오브젝트를 각각 샘플링할 수 있어
// 이 제약을 우회한다.
public class DualAnimationFramePreviewWindow : EditorWindow
{
    [SerializeField] private GameObject targetA;
    [SerializeField] private AnimationClip clipA;
    [SerializeField] private int frameA;

    [SerializeField] private GameObject targetB;
    [SerializeField] private AnimationClip clipB;
    [SerializeField] private int frameB;

    [MenuItem("Tools/Animation/Dual Frame Compare")]
    private static void Open()
    {
        GetWindow<DualAnimationFramePreviewWindow>("Dual Frame Compare");
    }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        DrawSide("A", ref targetA, ref clipA, ref frameA);
        EditorGUILayout.Space();
        DrawSide("B", ref targetB, ref clipB, ref frameB);

        if (EditorGUI.EndChangeCheck())
            Sample();
    }

    private static void DrawSide(string label, ref GameObject target, ref AnimationClip clip, ref int frame)
    {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        target = (GameObject)EditorGUILayout.ObjectField("Target (커브 기준 오브젝트)", target, typeof(GameObject), true);
        clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", clip, typeof(AnimationClip), false);

        if (clip == null)
            return;

        int maxFrame = Mathf.Max(0, Mathf.RoundToInt(clip.length * clip.frameRate));
        frame = EditorGUILayout.IntSlider($"Frame (/{maxFrame}, {clip.frameRate:0}fps)", frame, 0, maxFrame);
    }

    private void Sample()
    {
        if (!AnimationMode.InAnimationMode())
            AnimationMode.StartAnimationMode();

        AnimationMode.BeginSampling();

        if (targetA != null && clipA != null)
            AnimationMode.SampleAnimationClip(targetA, clipA, frameA / clipA.frameRate);

        if (targetB != null && clipB != null)
            AnimationMode.SampleAnimationClip(targetB, clipB, frameB / clipB.frameRate);

        AnimationMode.EndSampling();

        SceneView.RepaintAll();
    }

    private void OnDisable()
    {
        if (AnimationMode.InAnimationMode())
            AnimationMode.StopAnimationMode();
    }
}
