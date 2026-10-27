using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 설정창 드롭다운 — 펼친 목록을 드롭다운 자신의 레이어로 맞추고, 목록이 열린 동안 머리를 고른 상태로 고정한다.
/// (10-26 타이틀 모니터 RT 설정창이 없어져 차단막 레이캐스터 교체는 지웠다.)
/// </summary>
public sealed class SettingsDropdown : TMP_Dropdown
{
    // 🔴 목록은 별도 캔버스라 **자기 레이어로 컬링**된다. 목록 레이어가 Default 면 UI 레이어만 찍는 카메라에서
    //    클릭(차단막은 루트 레이어)만 먹고 목록이 안 보였다(팀장 10-12 Play). 드롭다운 자신의 레이어로 맞춘다.
    // 클릭 소리도 여기서 — 목록이 실제로 새로 만들어질 때 1회(마우스·Submit 공통, Codex 회의 10-26).
    protected override GameObject CreateDropdownList(GameObject template)
    {
        GameObject list = base.CreateDropdownList(template);
        SetLayerRecursively(list.transform, gameObject.layer);
        UiClickSound.Play();
        return list;
    }

    // 목록이 열린 동안 머리 고정(×0.55·회색 값) — 선택이 항목으로 옮겨가도 머리가 일반 상태로 돌아오지 않게(경석 10-26 6차).
    // 차단막은 목록과 함께 생기고 사라진다(닫힘·비활성 모두 DestroyBlocker 를 거친다).
    protected override GameObject CreateBlocker(Canvas rootCanvas)
    {
        GameObject blocker = base.CreateBlocker(rootCanvas);
        if (TryGetComponent(out ButtonLabelStateColor state)) state.PinWithGraphic(true);
        return blocker;
    }

    protected override void DestroyBlocker(GameObject blocker)
    {
        if (TryGetComponent(out ButtonLabelStateColor state)) state.PinWithGraphic(false);
        base.DestroyBlocker(blocker);
        // TMP Hide() 는 차단막을 지운 **다음에** 머리를 다시 Select() 한다 → 마우스로 닫았으면 이번 프레임 끝에 푼다
        // (안 풀면 다른 곳을 누를 때까지 머리가 어둡게 남았다 — 경석 10-26 8차). Esc·Enter 로 닫으면 키보드 사용자라 남긴다.
        Mouse mouse = Mouse.current;
        _releaseSelection = mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.leftButton.wasReleasedThisFrame);
    }

    private bool _releaseSelection;

    private void LateUpdate()
    {
        if (!_releaseSelection) return;
        _releaseSelection = false;
        EventSystem es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject == gameObject) es.SetSelectedGameObject(null);
    }

    static void SetLayerRecursively(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRecursively(t.GetChild(i), layer);
    }
}
