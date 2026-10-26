using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI 클릭 소리 <c>UI_Click</c> — 컨트롤마다 재생 지점을 <b>하나만</b> 쓴다(한 번 클릭에 두 번 나지 않게 — Codex 회의 10-26).
/// 버튼 = onClick(마우스·Submit 둘 다) · 토글 = onValueChanged(저장값 복원은 SetIsOnWithoutNotify 라 무음) ·
/// 슬라이더 = 왼쪽 버튼 누르는 순간(드래그·값 변경 무음) · 드롭다운 항목 = 클릭/Submit(항목 Toggle 값 변경은 생성 때도 나서 안 쓴다).
/// 드롭다운 열기·키 변경 입력 대기는 각자(<see cref="SettingsDropdown"/>·<see cref="KeyRebindRow"/>)가 <see cref="Play"/> 를 부른다.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiClickSound : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, ISubmitHandler
{
    public const string SoundKey = "UI_Click";

    public enum Trigger { ButtonClick, ToggleChanged, PointerDown, ClickOrSubmit }

    [SerializeField] private Trigger trigger;

    private Button _button;
    private Toggle _toggle;
    private Selectable _selectable;

    public void Configure(Trigger value)
    {
        bool wasEnabled = isActiveAndEnabled;
        if (wasEnabled) Unbind();
        trigger = value;
        if (wasEnabled) Bind();
    }

    /// <summary>소리 한 번. AudioManager 가 없으면(BootStrap 미경유 Play) 조용히 넘긴다.</summary>
    public static void Play()
    {
        AudioManager audio = AudioManager.Instance;
        if (audio != null) audio.Play(SoundKey);
    }

    private void OnEnable() => Bind();
    private void OnDisable() => Unbind();

    private void Bind()
    {
        TryGetComponent(out _selectable);
        if (trigger == Trigger.ButtonClick && TryGetComponent(out _button)) _button.onClick.AddListener(Play);
        if (trigger == Trigger.ToggleChanged && TryGetComponent(out _toggle)) _toggle.onValueChanged.AddListener(OnToggle);
    }

    private void Unbind()
    {
        // 🔴 명령 실행 뒤 interactable 을 다시 보지 않는다 — 적용 버튼은 자기 동작 중 꺼지고, 닫기는 패널을 끈다.
        if (_button != null) _button.onClick.RemoveListener(Play);
        if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnToggle);
    }

    private static void OnToggle(bool _) => Play();

    private bool Interactable => _selectable == null || _selectable.IsInteractable();

    public void OnPointerDown(PointerEventData eventData)
    {
        if (trigger == Trigger.PointerDown && eventData.button == PointerEventData.InputButton.Left && Interactable) Play();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (trigger == Trigger.ClickOrSubmit && eventData.button == PointerEventData.InputButton.Left && Interactable) Play();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (trigger == Trigger.ClickOrSubmit && Interactable) Play();
    }
}
