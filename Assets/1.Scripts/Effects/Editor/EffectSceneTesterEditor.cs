using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="EffectSceneTester"/>의 6케이스를 인스펙터 버튼으로 노출한다.
/// (컨텍스트 메뉴로도 같은 메서드를 부를 수 있다 — 버튼은 검증 순서를 눈에 보이게 하려는 것.)
/// </summary>
[CustomEditor(typeof(EffectSceneTester))]
public class EffectSceneTesterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 모드에서 각 케이스를 실행한다.", MessageType.Info);
            return;
        }

        var tester = (EffectSceneTester)target;

        Section("1 · 원샷 타격 — duration 후 자동 반납되는가");
        if (GUILayout.Button("원샷 재생")) tester.Case1OneShot();

        Section("2·3 · 컴포지트 3막 + 사운드 파트 — delay 간격으로 순차 발화하는가");
        if (GUILayout.Button("컴포지트 재생")) tester.Case2Composite();

        Section("4 · 루프 3분할 + 핸들 — Release()가 뚝 끊지 않는가");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("루프 시작 (월드 고정)")) tester.Case4PlayLoop();
            if (GUILayout.Button("Release()")) tester.CaseReleaseLoop();
        }

        Section("5 · 부착 추종 + 대상 소멸 — 풀에 null이 남지 않는가");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("루프 시작 (대상 추종)")) tester.Case5PlayAttachedLoop();
            if (GUILayout.Button("추종 대상 파괴")) tester.Case5DestroyTarget();
        }

        Section("6 · 풀 재사용 + 히트스톱 — 인스턴스 수가 안 늘고 이펙트만 멈추는가");
        if (GUILayout.Button("연속 발화")) tester.Case6Burst();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("대상 정지 (rate 0)")) tester.Case6FreezeTarget();
            if (GUILayout.Button("대상 재개 (rate 1)")) tester.Case6ResumeTarget();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("풀 통계 출력")) tester.LogPoolStats();
    }

    private static void Section(string title)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }
}
