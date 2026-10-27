using UnityEditor;

/// <summary>포드 강하 설정 인스펙터 — 기본 필드 아래에 읽기 전용 단계 시각표(<see cref="PodDropScheduleView"/>, 할 일 100 C2d).</summary>
[CustomEditor(typeof(PodDropSettings))]
public sealed class PodDropSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        PodDropScheduleView.Draw((PodDropSettings)target);
    }
}
