using UnityEngine;

/// <summary>
/// 에디터에서 배치 확인용으로만 보이는 오브젝트. Play 가 시작되면 스스로 꺼진다.
/// 오브젝트 태그를 <c>EditorOnly</c> 로 두면 빌드에서는 아예 빠진다(저작 메뉴가 같이 설정한다).
///
/// 용도: 결과 화면 <c>PlayerRows</c> 아래의 행 미리보기(RowPreview_P*) — 런타임 행은 ResultStatsView 가
/// 복제하므로 씬에는 행이 없어 위치를 잡기 어렵다. 꺼진 자식은 레이아웃 그룹이 무시한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class EditorPreviewOnly : MonoBehaviour
{
    private void Awake()
    {
        if (Application.isPlaying)
            gameObject.SetActive(false);
    }
}
