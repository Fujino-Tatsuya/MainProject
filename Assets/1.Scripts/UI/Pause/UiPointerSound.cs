using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 게임 전체 UI 버튼의 마우스 호버(<c>UI_Hover</c>)·클릭(<c>UI_Click</c>) 소리 — 버튼마다 붙이지 않고 한 곳에서 낸다(경석 11-16).
/// <see cref="AudioManager"/> 가 자기 오브젝트(DDOL)에 붙인다.
/// </summary>
/// <remarks>
/// 대상 = EventSystem 이 그 자리에서 클릭을 보낼 오브젝트(<see cref="ExecuteEvents.GetEventHandler{T}"/>) —
/// Button·Toggle·드롭다운(누를 수 있을 때만)과 Selectable 이 없는 클릭 처리기(캐릭터 선택 카드 등). 스킬 칸처럼 클릭 처리기가 아닌 HUD 는 저절로 빠진다.
/// 호버 = 마우스가 <b>움직여서</b> 새 대상에 들어간 순간 1번(패널이 멈춘 커서 밑에 열릴 때는 무음). 키보드·패드 선택은 무음.
/// 클릭 = 같은 대상 위에서 누르고 뗐을 때. 누를 수 있는지는 <b>누른 순간</b> 본다(적용 버튼처럼 자기 동작 중 꺼지는 것).
/// 🔴 이미 자기 클릭 소리를 내는 곳은 건너뛴다 — <see cref="UiClickSound"/>(ESC·설정창)·<see cref="KeyRebindRow"/>·<see cref="SettingsDropdown"/>.
/// 특정 버튼만 빼려면 <see cref="UiPointerSoundMute"/> 를 붙인다.
/// EventSystem 보다 먼저 돈다 — 클릭으로 패널이 꺼지기 전에 대상을 잡아야 한다.
/// </remarks>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class UiPointerSound : MonoBehaviour
{
    public const string HoverKey = "UI_Hover";
    public const string ClickKey = UiClickSound.SoundKey;

    private readonly List<RaycastResult> _hits = new List<RaycastResult>();
    private PointerEventData _pointer;
    private EventSystem _pointerOwner;

    private GameObject _hovered;
    private GameObject _pressed;
    private Vector2 _lastPosition;

    private void Update()
    {
        Mouse mouse = Mouse.current;
        EventSystem system = EventSystem.current;
        if (mouse == null || system == null)
        {
            _hovered = null;
            _pressed = null;
            return;
        }

        Vector2 position = mouse.position.ReadValue();
        bool moved = (position - _lastPosition).sqrMagnitude > 0.01f;
        _lastPosition = position;

        GameObject target = TargetAt(system, position);

        if (target != _hovered)
        {
            if (target != null && moved && !mouse.leftButton.isPressed && IsInteractable(target)) Play(HoverKey);
            _hovered = target;
        }

        if (mouse.leftButton.wasPressedThisFrame)
            _pressed = target != null && IsInteractable(target) && !HasOwnClickSound(target) ? target : null;

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (_pressed != null && _pressed == target) Play(ClickKey);
            _pressed = null;
        }
    }

    private GameObject TargetAt(EventSystem system, Vector2 position)
    {
        if (_pointer == null || _pointerOwner != system)
        {
            _pointer = new PointerEventData(system);
            _pointerOwner = system;
        }
        _pointer.position = position;
        _hits.Clear();
        system.RaycastAll(_pointer, _hits);
        if (_hits.Count == 0) return null;

        GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hits[0].gameObject);
        if (handler == null || handler.GetComponentInParent<UiPointerSoundMute>() != null) return null;

        // Selectable 이면 버튼류만 — 슬라이더·스크롤바·입력칸은 무음.
        if (handler.TryGetComponent(out Selectable selectable)
            && !(selectable is Button || selectable is Toggle || selectable is TMPro.TMP_Dropdown))
            return null;
        return handler;
    }

    private static bool IsInteractable(GameObject target)
    {
        return !target.TryGetComponent(out Selectable selectable) || selectable.IsInteractable();
    }

    private static bool HasOwnClickSound(GameObject target)
    {
        return target.GetComponent<UiClickSound>() != null
            || target.GetComponent<SettingsDropdown>() != null
            || target.GetComponentInParent<KeyRebindRow>() != null;
    }

    private static void Play(string key)
    {
        AudioManager audio = AudioManager.Instance;
        // 시트 동시 재생 수·다시 나기 간격에 막히면 Play 가 null 을 준다 — 조용히 씹히지 않게 남긴다(11-16 호버 0.78초 클립 × 동시 1 로 씹혔다).
        if (audio != null && audio.Play(key) == null)
            Edit.Log($"[UiPointerSound] '{key}' 재생 안 됨 — 시트 동시 재생 수·다시 나기 간격 또는 키 없음");
    }
}
