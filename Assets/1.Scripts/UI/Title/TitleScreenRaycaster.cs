using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 모니터 RT 캔버스(Screen Space – Camera, <see cref="TitleMonitorDisplay.UICamera"/>)용 레이캐스터.
/// 실제 화면 좌표를 중앙 화면 메시 UV → RT 픽셀로 바꿔 기본 GraphicRaycaster 에 넘긴다.
/// </summary>
/// <remarks>
/// 🔴 <c>eventData.position</c> 은 **호출 동안만** 바꾸고 반드시 되돌린다 — 다른 레이캐스터와 다음 프레임 delta 가 오염된다.
/// 🔴 화면 메시에 안 맞으면 아무것도 반환하지 않는다. 이게 "모니터 아무 데나 누르면 Start" 를 막는다
///    (예전 월드 캔버스는 3D 가림을 몰라 모니터 뒤의 보이지 않는 버튼이 클릭을 받았다, 2026-09-23).
/// 결과의 <c>screenPosition</c> 은 RT 픽셀 좌표계다 — 슬라이더는 <see cref="TitleScreenSlider"/> 가 같은 좌표계로 맞춘다.
/// </remarks>
[RequireComponent(typeof(Canvas))]
public sealed class TitleScreenRaycaster : GraphicRaycaster
{
    public override Camera eventCamera
    {
        get
        {
            var display = TitleMonitorDisplay.Active;
            return display != null ? display.UICamera : base.eventCamera;
        }
    }

    public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
    {
        var display = TitleMonitorDisplay.Active;
        if (display == null || !display.TryScreenToUIPixel(eventData.position, out Vector2 pixel))
            return;

        Vector2 original = eventData.position;
        eventData.position = pixel;
        try
        {
            base.Raycast(eventData, resultAppendList);
        }
        finally
        {
            eventData.position = original;
        }
    }
}
