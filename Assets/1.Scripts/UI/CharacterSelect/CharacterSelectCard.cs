using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 선택 카드 하나(할 일 48.2) — 로스터 엔트리 하나의 초상화·이름. <see cref="CharacterSelectPanel"/> 이 로스터 수만큼 복제한다.
/// 자리(위·가운데·아래)·크기·이동은 패널이 정하고, 여기는 겉모습만 바꾼다.
/// - 가운데(포커스) = 초록 큰 카드(card_selected_gap + fill_tint), 위·아래 = card_unselected_top/bottom.
/// - 선택 테두리(card_selected — 카드 뒤에 깔려 바깥 흰 띠만 보인다, 할 일 162) = "가운데 자리 강조"(할 일 60) — 알파는 패널이 이동 연출에 맞춰 준다(<see cref="SetRingAlpha"/>).
/// - 지금 내 캐릭터 = "현재" 표식만.
/// - 고를 수 없음 = 회색 + "선택 불가".
/// 클릭은 Button 이 아니라 <see cref="IPointerClickHandler"/> — EventSystem 선택이 남아 Enter 가 두 번 먹는 일을 피한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterSelectCard : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private GameObject focusedVisual;
    [SerializeField] private Image focusedFill;
    [SerializeField] private Image sideBackground;
    [SerializeField] private Sprite sideTopSprite;
    [SerializeField] private Sprite sideBottomSprite;
    [Tooltip("가운데 자리 강조 선택 테두리(Image). 크기 = RectTransform, 색·스프라이트 = Image 칸 그대로 쓴다(코드는 덮어쓰지 않음). " +
             "켜고 끄기·알파(Image 알파에 곱함)는 SetRingAlpha. 고칠 곳 = 패널 프리팹 Content/Cards/Card_Template/Ring_Focus.")]
    [FormerlySerializedAs("currentMarker")]
    [SerializeField] private GameObject focusRing;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text nameText;
    [Tooltip("\"현재\" 표식(배경 알약째 켜고 끈다). 글자는 currentText.")]
    [SerializeField] private GameObject currentTag;
    [SerializeField] private TMP_Text currentText;
    [Tooltip("\"선택 불가\" 표식(배경 알약째 켜고 끈다). 글자는 unavailableText.")]
    [SerializeField] private GameObject unavailableTag;
    [SerializeField] private TMP_Text unavailableText;

    [Header("Colors")]
    [SerializeField] private Color focusedFillColor = new Color(0.478f, 0.733f, 0.333f, 1f);   // 목업 초록(122,187,85)
    [SerializeField] private Color lockedFillColor = new Color(0.36f, 0.45f, 0.32f, 1f);       // 준비 중 — 바랜 초록
    [SerializeField] private Color unavailableFillColor = new Color(0.42f, 0.42f, 0.42f, 1f);
    [SerializeField] private Color sideColor = Color.white;
    [SerializeField] private Color unavailableSideColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    [SerializeField] private Color focusedNameColor = new Color(0.08f, 0.08f, 0.08f, 1f);
    [SerializeField] private Color sideNameColor = Color.white;
    [SerializeField] private Color unavailablePortraitColor = new Color(0.35f, 0.35f, 0.35f, 1f);

    private Image _ringImage;
    private float _ringBaseAlpha = 1f;
    private bool _ringCached;

    /// <summary>클릭됐다(카드 index).</summary>
    public event Action<int> Clicked;

    public int Index { get; private set; } = -1;
    public bool Available { get; private set; }

    /// <summary>로스터 엔트리로 채운다. 문구(현재·선택 불가)는 현재 언어로 다시 쓴다.</summary>
    public void Bind(int index, Sprite portrait, string displayName, bool available)
    {
        Index = index;
        Available = available;
        gameObject.name = $"Card_{index}";

        if (portraitImage != null)
        {
            portraitImage.sprite = portrait;
            portraitImage.enabled = portrait != null;
            portraitImage.color = available ? Color.white : unavailablePortraitColor;
        }

        if (nameText != null)
            nameText.text = displayName ?? string.Empty;
        if (currentText != null)
            currentText.text = CharacterSelectUiStrings.CurrentTag.TextOr(currentText.text);
        if (unavailableText != null)
            unavailableText.text = CharacterSelectUiStrings.Unavailable.TextOr(unavailableText.text);
        if (unavailableTag != null)
            unavailableTag.SetActive(!available);
    }

    /// <summary>
    /// 겉모습. <paramref name="slotOffset"/> = 0 가운데(포커스) · -1 위 · +1 아래(그 밖은 패널이 끈다).
    /// 화면 밖(±2 이상)은 나가는 중인 카드 — 옆 카드 모양. <paramref name="isCurrent"/> = 지금 내 캐릭터("현재" 표식),
    /// <paramref name="locked"/> = 준비 중(포커스 카드를 바래게). 선택 테두리는 여기서 건드리지 않는다(<see cref="SetRingAlpha"/>).
    /// </summary>
    public void SetState(int slotOffset, bool isCurrent, bool locked)
    {
        bool focused = slotOffset == 0;

        if (focusedVisual != null)
            focusedVisual.SetActive(focused);
        if (focusedFill != null)
            focusedFill.color = !Available ? unavailableFillColor : locked ? lockedFillColor : focusedFillColor;

        if (sideBackground != null)
        {
            sideBackground.gameObject.SetActive(!focused);
            Sprite side = slotOffset < 0 ? sideTopSprite : sideBottomSprite;
            if (side != null)
                sideBackground.sprite = side;
            sideBackground.color = Available ? sideColor : unavailableSideColor;
        }

        if (currentTag != null)
            currentTag.SetActive(isCurrent);

        if (nameText != null)
            nameText.color = focused && Available ? focusedNameColor : sideNameColor;
    }

    /// <summary>가운데 자리 강조 테두리 알파(0 = 끔). 프리팹 Image 의 원래 알파에 곱한다.</summary>
    public void SetRingAlpha(float alpha)
    {
        if (focusRing == null)
            return;

        bool show = alpha > 0f;
        if (focusRing.activeSelf != show)
            focusRing.SetActive(show);

        if (!_ringCached)
        {
            _ringCached = true;
            _ringImage = focusRing.GetComponent<Image>();
            if (_ringImage != null)
                _ringBaseAlpha = _ringImage.color.a;
        }

        if (_ringImage != null)
        {
            Color color = _ringImage.color;
            color.a = _ringBaseAlpha * Mathf.Clamp01(alpha);
            _ringImage.color = color;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            Clicked?.Invoke(Index);
    }
}
