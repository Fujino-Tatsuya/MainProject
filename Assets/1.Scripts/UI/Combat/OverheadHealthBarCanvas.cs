using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 모든 유닛의 머리 위 체력바를 모으는 공용 Screen Space - Overlay Canvas. 처음 쓰일 때 만든다.
/// DontDestroyOnLoad — 씬을 넘어 살아남는 플레이어의 바가 씬 언로드에 같이 파괴되지 않고, 씬마다 중복되지 않는다.
/// 바는 각 UnitOverheadHealthBar 가 자기 수명에 맞춰 넣고 파괴한다.
/// </summary>
public static class OverheadHealthBarCanvas
{
    /// <summary>CombatHUD(0) 아래, 플로팅 데미지(50)보다도 아래.</summary>
    public const int SortingOrder = -10;
    /// <summary>플로팅 데미지와 같은 픽셀 기준 — 세로 1080 에 맞춘다.</summary>
    public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

    static Canvas canvas;

    public static Canvas Instance
    {
        get
        {
            if (canvas == null)
                canvas = Create();
            return canvas;
        }
    }

    static Canvas Create()
    {
        var canvasObject = new GameObject("OverheadHealthBarCanvas", typeof(RectTransform));
        Object.DontDestroyOnLoad(canvasObject);

        Canvas created = canvasObject.AddComponent<Canvas>();
        created.renderMode = RenderMode.ScreenSpaceOverlay;
        created.sortingOrder = SortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
        return created;
    }
}
