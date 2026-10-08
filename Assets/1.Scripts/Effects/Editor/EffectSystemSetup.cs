using UnityEditor;

/// <summary>
/// Effect System 에디터 진입점 — 모든 <see cref="EffectEntry"/> 의 수명을 다시 계산해 저장한다.
///
/// 2026-10-07 — 쓸모가 없어진 메뉴 둘을 지웠다(민경).
/// <list type="bullet">
/// <item><c>v1 기본 에셋 생성</c>: 원본 폴더 <c>Assets/5.VFX</c> 가 SVN 이관(b1980d3)으로 사라져 에러만 냈다</item>
/// <item><c>스모크 테스트 (Play Mode)</c>: 러너 호출이 주석이라 매니저만 띄우고 아무것도 검증하지 않았다</item>
/// </list>
/// </summary>
public static class EffectSystemSetup
{
    [MenuItem("Tools/Effects/모든 엔트리 수명 재계산")]
    public static void RecomputeAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:EffectEntry");

        for (int i = 0; i < guids.Length; i++)
        {
            var entry = AssetDatabase.LoadAssetAtPath<EffectEntry>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (entry == null) continue;

            entry.RecomputeLifetimes();

            // 값이 안 바뀌었어도 항상 dirty로 둔다. OnValidate가 임포트 중에 이미 계산해 둔 값은
            // 메모리에만 있고 에셋 파일에는 없을 수 있는데, 그러면 빌드가 0을 들고 나간다.
            EditorUtility.SetDirty(entry);

            UnityEngine.Debug.Log($"[Effect] '{entry.name}' → duration {entry.ResolvedDuration:F2}s " +
                                  $"(계산 {entry.ComputedDuration:F2}s / 오버라이드 {entry.duration:F2}s)", entry);
        }

        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log($"[Effect] 엔트리 {guids.Length}개 갱신·저장.");
    }
}
