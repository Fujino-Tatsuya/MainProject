using TMPro;
using UnityEngine;

/// <summary>
/// 간파 훈련 봇 머리 우상단 "우클릭!" 표시 설정(할 일 303) — 스폰 프리팹·씬을 건드리지 않게 <c>Resources/</c> 아래 한 개만 둔다(<see cref="ResourcePath"/>).
/// 표시는 <see cref="LobbyCounterPrompt"/>, 판정은 <see cref="LobbyCounterPromptRules"/>.
/// </summary>
[CreateAssetMenu(fileName = "LobbyCounterPromptSettings", menuName = "Lobby/Counter Prompt Settings")]
public sealed class LobbyCounterPromptSettings : ScriptableObject
{
    public const string ResourcePath = "Lobby/LobbyCounterPromptSettings";

    [Header("이미지·글꼴")]
    [Tooltip("마우스 아이콘(참조만 — 50.Art/UI/TraitCard_C1/Sprites/Input_RMB).")]
    [SerializeField] Sprite mouseIcon;
    [Tooltip("오른쪽 버튼 모양 + 번짐 마스크(흰색·알파). 아이콘과 중심이 같고 Pixels Per Unit 으로 크기를 맞췄다.")]
    [SerializeField] Sprite rightButtonGlow;
    [SerializeField] TMP_FontAsset font;
    [Tooltip("글자 재질(외곽선). 비면 글꼴 기본 재질.")]
    [SerializeField] Material fontMaterial;

    [Header("위치")]
    [Tooltip("봇 발밑에서 머리까지 월드 높이(m).")]
    [SerializeField, Min(0f)] float worldHeight = 1.7f;
    [Tooltip("머리 화면 위치에서 옮길 거리. 1920×1080 기준 픽셀, 오른쪽·위 = +.")]
    [SerializeField] Vector2 screenOffset = new Vector2(56f, 28f);

    [Header("크기")]
    [Tooltip("아이콘 높이(1080p 기준 픽셀). 폭은 스프라이트 비율.")]
    [SerializeField, Min(1f)] float iconHeight = 60f;
    [Tooltip("아이콘과 글자 사이 간격(픽셀).")]
    [SerializeField, Min(0f)] float labelGap = 8f;
    [SerializeField, Min(1f)] float labelFontSize = 32f;
    [SerializeField] Color labelColor = Color.white;

    [Header("빨간 빛")]
    [SerializeField] Color glowColor = new Color(1f, 0.14f, 0.1f, 1f);
    [Tooltip("펄스가 끝난 뒤 창이 닫힐 때까지 유지하는 빛의 알파(0~1).")]
    [SerializeField, Range(0f, 1f)] float glowRestAlpha = 0.7f;
    [Tooltip("창이 열리는 순간의 펄스 길이(초, 슬로우 영향 없음).")]
    [SerializeField, Min(0f)] float pulseSeconds = 0.35f;
    [Tooltip("펄스 시작 크기 배율(끝나면 1).")]
    [SerializeField, Min(1f)] float pulseScale = 1.3f;

    public Sprite MouseIcon => mouseIcon;
    public Sprite RightButtonGlow => rightButtonGlow;
    public TMP_FontAsset Font => font;
    public Material FontMaterial => fontMaterial;
    public float WorldHeight => worldHeight;
    public Vector2 ScreenOffset => screenOffset;
    public float IconHeight => iconHeight;
    public float LabelGap => labelGap;
    public float LabelFontSize => labelFontSize;
    public Color LabelColor => labelColor;
    public Color GlowColor => glowColor;
    public float GlowRestAlpha => glowRestAlpha;
    public float PulseSeconds => pulseSeconds;
    public float PulseScale => pulseScale;
}
