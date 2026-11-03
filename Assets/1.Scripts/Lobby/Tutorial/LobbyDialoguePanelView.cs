using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 감독관 대사 패널(할 일 158, 명세 3.3) — TextBox 배경 + NameTag 이름표 + 오른쪽 Operator 인물화, 뒤에 전면 삽화(명세 23·24).
/// 입력 버튼·진행 아이콘 없음. 포인터 입력을 막지 않는다. 대사 진행 시각은 서버(157)가 정하고 이 뷰는 받은 대로 보여 주기만 한다.
/// 패널은 짧게 페이드, 삽화는 <see cref="illustrationFadeSeconds"/> 동안 디졸브(삽화 ↔ 삽화 전환은 이전 그림 위로 새 그림을 덮는다).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyDialoguePanelView : MonoBehaviour
{
    [Header("대사 패널")]
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text lineText;
    [SerializeField, Min(0f)] private float panelFadeSeconds = 0.15f;

    [Header("전면 삽화")]
    [SerializeField] private CanvasGroup illustrationGroup;
    [Tooltip("아래 그림 — 디졸브 때 이전 삽화를 남겨 둔다.")]
    [SerializeField] private Image illustrationBack;
    [Tooltip("위 그림 — 새 삽화가 알파 0→1 로 덮는다.")]
    [SerializeField] private Image illustrationFront;
    [SerializeField] private Sprite factorySprite;
    [SerializeField] private Sprite rebellionSprite;
    [SerializeField, Min(0f)] private float illustrationFadeSeconds = 0.4f;

    private string _speakerKey;
    private string _lineKey;
    private LobbyTutorialBackground _background;
    private float _panelTarget;
    private float _illustrationTarget;
    private float _frontAlpha = 1f;

    public bool IsShowing => _panelTarget > 0f;
    public LobbyTutorialBackground Background => _background;

    private void Awake() => Hide(immediate: true);

    /// <summary>한 줄 표시. 키 = Strings 키. 같은 패널에서 다음 줄로 바뀔 때도 이걸 다시 부른다.</summary>
    public void Show(string speakerKey, string lineKey, LobbyTutorialBackground background)
    {
        _speakerKey = speakerKey;
        _lineKey = lineKey;
        RefreshLanguage();
        _panelTarget = 1f;
        SetBackground(background);
    }

    public void Hide(bool immediate = false)
    {
        _panelTarget = 0f;
        SetBackground(LobbyTutorialBackground.None);
        if (!immediate)
            return;

        SetAlpha(panelGroup, 0f);
        SetAlpha(illustrationGroup, 0f);
    }

    /// <summary>언어가 바뀌면 같은 키로 다시 찾는다.</summary>
    public void RefreshLanguage()
    {
        if (speakerText != null)
            speakerText.text = LobbyTutorialUiStrings.Resolve(_speakerKey);
        if (lineText != null)
            lineText.text = LobbyTutorialUiStrings.Resolve(_lineKey);
    }

    private void SetBackground(LobbyTutorialBackground background)
    {
        if (background == _background)
            return;

        Sprite next = SpriteOf(background);
        if (next != null)
        {
            // 이미 삽화가 보이면 지금 그림을 아래로 내리고 새 그림을 위에서 디졸브.
            if (illustrationFront != null && illustrationBack != null && illustrationFront.sprite != null && CurrentAlpha(illustrationGroup) > 0f)
            {
                illustrationBack.sprite = illustrationFront.sprite;
                illustrationBack.enabled = true;
                _frontAlpha = 0f;
            }
            else
            {
                if (illustrationBack != null)
                    illustrationBack.enabled = false;
                _frontAlpha = 1f;
            }

            if (illustrationFront != null)
            {
                illustrationFront.sprite = next;
                SetImageAlpha(illustrationFront, _frontAlpha);
            }
        }

        _background = background;
        _illustrationTarget = next != null ? 1f : 0f;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        Step(panelGroup, _panelTarget, panelFadeSeconds, dt);
        Step(illustrationGroup, _illustrationTarget, illustrationFadeSeconds, dt);

        if (_frontAlpha < 1f && illustrationFront != null)
        {
            _frontAlpha = illustrationFadeSeconds > 0f ? Mathf.MoveTowards(_frontAlpha, 1f, dt / illustrationFadeSeconds) : 1f;
            SetImageAlpha(illustrationFront, _frontAlpha);
            if (_frontAlpha >= 1f && illustrationBack != null)
                illustrationBack.enabled = false;
        }
    }

    private Sprite SpriteOf(LobbyTutorialBackground background)
    {
        switch (background)
        {
            case LobbyTutorialBackground.Factory: return factorySprite;
            case LobbyTutorialBackground.Rebellion: return rebellionSprite;
            default: return null;
        }
    }

    private static void Step(CanvasGroup group, float target, float seconds, float dt)
    {
        if (group == null)
            return;

        float next = seconds > 0f ? Mathf.MoveTowards(group.alpha, target, dt / seconds) : target;
        SetAlpha(group, next);
    }

    private static void SetAlpha(CanvasGroup group, float alpha)
    {
        if (group == null)
            return;

        group.alpha = alpha;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private static float CurrentAlpha(CanvasGroup group) => group != null ? group.alpha : 0f;

    private static void SetImageAlpha(Image image, float alpha)
    {
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }
}
