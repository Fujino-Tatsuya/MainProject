using UnityEngine;

/// <summary>
/// 상호작용 키 프롬프트의 외형. <c>Resources/InteractPromptSettings</c> 에서 읽는다 — 없으면 기본값 + 텍스트 "F".
/// </summary>
[CreateAssetMenu(menuName = "UI/Interact Prompt Settings", fileName = "InteractPromptSettings")]
public sealed class InteractPromptSettings : ScriptableObject
{
    public const string ResourcePath = "InteractPromptSettings";

    [Tooltip("키 아이콘. 비어 있으면 텍스트 \"F\" 로 대신 그린다.")]
    [SerializeField] private Sprite icon;

    [Tooltip("아이콘 크기(Canvas 단위 = 세로 1080 기준 픽셀). 카메라 거리와 무관하게 일정하다.")]
    [SerializeField] private Vector2 iconSize = new Vector2(56f, 56f);

    [Tooltip("월드 기준점에서 화면상 추가 오프셋(Canvas 단위, 위 = +y).")]
    [SerializeField] private Vector2 screenOffset = Vector2.zero;

    public Sprite Icon => icon;
    public Vector2 IconSize => iconSize;
    public Vector2 ScreenOffset => screenOffset;
}
