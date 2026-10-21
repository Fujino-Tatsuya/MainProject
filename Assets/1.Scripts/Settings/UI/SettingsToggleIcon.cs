using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 토글을 체크(켬) / X(끔) 아이콘으로 보여 준다(PLAN-settings-art A3 — 전용 토글 아트가 없어 납품 아이콘을 쓴다).
/// 🔴 <c>onValueChanged</c> 로 받지 않고 매 프레임 비교한다 — 컨트롤러들이 저장값을 넣을 때 <c>SetIsOnWithoutNotify</c> 를 써서
///    이벤트가 오지 않는다. 토글의 기본 체크 표시(<c>Toggle.graphic</c>)는 꺼짐에서 숨겨지므로 저작 메뉴가 비워 둔다.
/// </summary>
[RequireComponent(typeof(Toggle))]
public sealed class SettingsToggleIcon : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Sprite offSprite;
    [SerializeField] private Vector2 onSize = new Vector2(38f, 27f);
    [SerializeField] private Vector2 offSize = new Vector2(30f, 30f);

    private Toggle _toggle;
    private bool _shown;
    private bool _initialized;

    private void Awake() => _toggle = GetComponent<Toggle>();

    private void OnEnable() => Apply(_toggle.isOn);

    private void LateUpdate()
    {
        if (!_initialized || _toggle.isOn != _shown) Apply(_toggle.isOn);
    }

    private void Apply(bool on)
    {
        _shown = on;
        _initialized = true;
        if (icon == null) return;
        icon.sprite = on ? onSprite : offSprite;
        icon.rectTransform.sizeDelta = on ? onSize : offSize;
    }
}
