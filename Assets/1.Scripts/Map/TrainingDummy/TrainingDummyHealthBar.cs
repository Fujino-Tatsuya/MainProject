using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 허수아비 머리 위 체력바. 항상 보이고 게이지와 숫자를 같이 띄운다.
/// 기본: 월드스페이스 캔버스 자식에 부착하고, 카메라 회전을 그대로 따라가는 화면 정렬 빌보드로 돈다.
/// <see cref="screenFootBar"/> 를 켜면(로비 튜토리얼 월봇 허수아비 Variant 전용, 할 일 274) 몬스터 바와 같은 자리 —
/// 공용 오버레이 캔버스(<see cref="OverheadHealthBarCanvas"/>)로 옮겨 화면 공간에 그리고, 위치는 몬스터 바와 같은 계산
/// (<see cref="OverheadHealthBarScreenPlacement"/>)으로 주인 루트(발)에서 루트의 <see cref="MonsterOverheadHealthBarOffset"/>
/// footScreenOffset px 만큼 화면 아래(발밑). 기즈모·Scene 핸들 끌기·Play 중 값 변경은 그 컴포넌트를 매 프레임 읽어 그대로 반영된다.
/// 게이지·숫자 표기는 두 모드가 같다.
///
/// 기존 UnitOverheadHealthBar 를 쓰지 않는 이유 — 그쪽은 GetComponentInParent&lt;Player&gt;() 와
/// "!IsOwner 일 때만 표시"가 박혀 있는 플레이어 전용 컴포넌트다. 범용화하면 원격 플레이어
/// 체력바 표시 규칙에 회귀 위험이 생긴다. (지금은 몬스터 발밑 바로도 쓰이지만 체력 숫자 표기가 없다.)
/// </summary>
[DisallowMultipleComponent]
public sealed class TrainingDummyHealthBar : MonoBehaviour
{
    [Tooltip("비워두면 부모에서 찾는다.")]
    [SerializeField] Unit owner;
    [SerializeField] Image hpFill;
    [SerializeField] TMP_Text hpText;
    [SerializeField] string textFormat = "{0} / {1}";

    [Header("화면 발밑 바 (Variant 전용)")]
    [Tooltip("켜면 몬스터 바처럼 화면 공간 발밑에 띄운다 — 위치 = 주인 루트(발)에서 루트 MonsterOverheadHealthBarOffset.footScreenOffset px 아래. 끄면 월드 캔버스 그대로(본편 허수아비).")]
    [SerializeField] bool screenFootBar;
    [Tooltip("화면 발밑 바 배율. 1 = 이 RectTransform 크기(px)를 1080p 기준 그대로.")]
    [SerializeField, Min(0.01f)] float screenScale = 0.6f;

    // 매 프레임 string.Format 을 돌리지 않으려고 마지막으로 그린 값을 기억한다.
    int _shownHp = -1;
    int _shownMaxHp = -1;

    // 화면 발밑 바 — 오버레이 캔버스로 옮긴 뒤의 상태.
    RectTransform _screenRect;
    Canvas _screenCanvas;
    CanvasGroup _screenGroup;
    MonsterOverheadHealthBarOffset _offset;

    public bool ScreenFootBar => screenFootBar;

    void Awake()
    {
        if (owner == null)
            owner = GetComponentInParent<Unit>();
        if (screenFootBar)
            MoveToScreenCanvas();
    }

    // 이 오브젝트(캔버스 루트)째로 오버레이 캔버스 아래로 옮긴다 — 주인 밖으로 나가므로 주인이 사라지면 스스로 파괴한다.
    void MoveToScreenCanvas()
    {
        if (owner == null || !(transform is RectTransform rect))
            return;
        _offset = owner.GetComponent<MonsterOverheadHealthBarOffset>();
        Vector2 size = rect.rect.size;
        _screenCanvas = OverheadHealthBarCanvas.Instance;
        rect.SetParent(_screenCanvas.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * screenScale;
        name = $"DummyScreenBar ({owner.name})";
        _screenRect = rect;
        if (!TryGetComponent(out _screenGroup))
            _screenGroup = gameObject.AddComponent<CanvasGroup>();
        _screenGroup.blocksRaycasts = false;
        _screenGroup.interactable = false;
        _screenGroup.alpha = 0f;
        Canvas.willRenderCanvases += ApplyScreenPosition;
    }

    void OnDestroy()
    {
        if (_screenRect != null)
            Canvas.willRenderCanvases -= ApplyScreenPosition;
    }

    void LateUpdate()
    {
        if (owner == null)
        {
            if (_screenRect != null)
                Destroy(gameObject);
            return;
        }

        if (_screenRect == null)
        {
            Camera cam = Camera.main;
            if (cam != null)
                transform.rotation = cam.transform.rotation;
        }

        int maxHp = owner.FinalMaxHp;
        int currentHp = owner.CurrentHealth;

        if (hpFill != null)
            hpFill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0f;

        if (hpText != null && (currentHp != _shownHp || maxHp != _shownMaxHp))
        {
            _shownHp = currentHp;
            _shownMaxHp = maxHp;
            hpText.text = HpText(currentHp, maxHp, textFormat);
        }
    }

    // 몬스터 바(UnitOverheadHealthBar.ApplyScreenPosition)와 같은 시점·같은 계산. 주인 비활성·간파 암전·화면 밖은 투명.
    void ApplyScreenPosition()
    {
        if (_screenRect == null || owner == null || _screenCanvas == null)
            return;

        Camera cam = Camera.main;
        bool visible = false;
        if (cam != null && owner.gameObject.activeInHierarchy && !UnitOverheadHealthBar.HiddenByInterrupt(InterruptProductionDirector.CombatUiHidden))
        {
            Vector3 anchor = owner.transform.position;
            float footPx = _offset != null ? _offset.FootScreenOffset : 0f;
            Vector2 halfExtent = _screenRect.sizeDelta * (screenScale * 0.5f);
            visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
                cam.WorldToScreenPoint(anchor), new Vector2(Screen.width, Screen.height), _screenCanvas.scaleFactor,
                MonsterOverheadHealthBarVisibility.FootScreenOffset(footPx), halfExtent, out Vector2 position);
            if (visible)
            {
                _screenRect.anchoredPosition = position;
                _screenRect.localScale = Vector3.one * screenScale;
            }
        }

        _screenGroup.alpha = visible ? 1f : 0f;
    }

    void OnEnable() => Loc.Changed += ForceRelabel;
    void OnDisable() => Loc.Changed -= ForceRelabel;
    void ForceRelabel() => _shownHp = -1;

    /// <summary>
    /// 체력 글자 — <c>UI.TrainingDummy.Hp</c>({hp}·{max}). 시트에 행이 없으면 직렬화 <c>textFormat</c>(string.Format 형식 {0}/{1})
    /// — 빈 값이면 키의 ko.
    /// </summary>
    public static string HpText(int currentHp, int maxHp, string serializedFormat)
    {
        string fallback = string.IsNullOrEmpty(serializedFormat)
            ? UiStrings.TrainingDummyHp.Ko
            : string.Format(serializedFormat, currentHp, maxHp);
        return UiStrings.TrainingDummyHp.FormatOr(fallback, new Dictionary<string, string>
        {
            ["hp"] = currentHp.ToString(CultureInfo.InvariantCulture),
            ["max"] = maxHp.ToString(CultureInfo.InvariantCulture),
        });
    }
}
