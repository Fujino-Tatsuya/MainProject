using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 모니터 RT 안의 슬라이더. uGUI <see cref="Slider"/> 는 드래그 때 <c>eventData.position</c>(실제 화면 좌표)을
/// 다시 읽어 <c>pressEventCamera</c>(= UI 카메라) 로 변환하므로, 그대로 두면 값이 튄다(Codex 검토 09-23).
/// 누르기·드래그 동안만 좌표를 RT 픽셀로 바꿔 base 를 부르고 되돌린다.
/// </summary>
/// <remarks>
/// 직렬화 필드를 추가하지 않는다 — 씬의 Slider 컴포넌트는 <c>m_Script</c> 만 이 타입으로 바꿔 끼운다
/// (<c>VolumeSlider</c> 가 RequireComponent(Slider) 라 지웠다 다시 붙일 수 없다. 파생형이라 GetComponent&lt;Slider&gt; 는 그대로 된다).
/// 화면 밖으로 끌면 마지막 유효 좌표를 쓴다 — 놓기(PointerUp)는 EventSystem 이 그대로 전달한다.
/// </remarks>
public sealed class TitleScreenSlider : Slider
{
    private Vector2 _lastPixel;
    private bool _hasPixel;

    public override void OnPointerDown(PointerEventData eventData)
    {
        _hasPixel = false;
        WithRemapped(eventData, () => base.OnPointerDown(eventData));
    }

    public override void OnDrag(PointerEventData eventData) =>
        WithRemapped(eventData, () => base.OnDrag(eventData));

    private void WithRemapped(PointerEventData eventData, System.Action call)
    {
        var display = TitleMonitorDisplay.Active;
        if (display == null)
        {
            call();
            return;
        }

        if (display.TryScreenToUIPixel(eventData.position, out Vector2 pixel))
        {
            _lastPixel = pixel;
            _hasPixel = true;
        }
        else if (!_hasPixel)
        {
            return; // 화면 밖에서 시작한 입력은 무시
        }

        Vector2 original = eventData.position;
        eventData.position = _lastPixel;
        try { call(); }
        finally { eventData.position = original; }
    }
}
