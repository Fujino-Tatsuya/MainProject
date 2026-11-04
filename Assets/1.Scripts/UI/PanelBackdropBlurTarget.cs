using UnityEngine;

/// <summary>
/// 패널 배경 블러(할 일 201) — 패널 콘텐츠 루트(켜고 끄는 GameObject)에 하나 붙인다. 켜져 있는 동안 <see cref="PanelBackdropBlur"/> 를 잡아
/// 패널 뒤(월드 + 패널보다 아래 UI)를 흐린다. <see cref="UiModalBlocker"/> 와 같은 수명 방식(OnEnable/OnDisable).
/// 붙은 곳: GameMenu <c>Content</c>(일시정지·설정·종료 확인) · LobbyResultPanel <c>content</c> · TraitUI <c>Window</c>. 캐릭터 선택 패널은 제외.
/// </summary>
[DisallowMultipleComponent]
public sealed class PanelBackdropBlurTarget : MonoBehaviour
{
    private void OnEnable()
    {
        PanelBackdropBlur.Acquire(this, GetComponentInParent<Canvas>(true));
    }

    private void OnDisable()
    {
        PanelBackdropBlur.Release(this);
    }
}
