using BaseNetCode;
using UnityEngine;

/// <summary>
/// 🔸 임시 과열 게이지(D14) — 오너 화면에만 그린다. 정식 HUD 아트가 오면 CombatHUD 쪽으로 옮기고 이 컴포넌트는 지운다.
/// 단계별 색(기본 흰 → 1 노랑 → 2 주황 → 3 빨강), 과열 시 깜빡임.
/// </summary>
[RequireComponent(typeof(GunnerHeat))]
public class GunnerHeatHUD : BaseNetworkBehaviour
{
    [SerializeField] private Vector2 size = new Vector2(320f, 14f);
    [Tooltip("화면 아래에서 띄울 거리(px).")]
    [SerializeField] private float bottomOffset = 170f;

    private static readonly Color[] StageColors =
    {
        new Color(0.95f, 0.95f, 0.95f),
        new Color(1f, 0.9f, 0.2f),
        new Color(1f, 0.55f, 0.1f),
        new Color(1f, 0.15f, 0.1f),
    };

    private GunnerHeat heat;

    private void Awake() => heat = GetComponent<GunnerHeat>();

    private void OnGUI()
    {
        if (heat == null || heat.Data == null || (IsNetworkActive && !IsOwner))
            return;

        var back = new Rect((Screen.width - size.x) * 0.5f, Screen.height - bottomOffset, size.x, size.y);
        var fill = new Rect(back.x, back.y, back.width * heat.Normalized, back.height);

        Color color = StageColors[Mathf.Clamp(heat.CurrentStage, 0, StageColors.Length - 1)];
        if (heat.IsOverheated && Mathf.Repeat(Time.unscaledTime, 0.4f) < 0.2f)
            color = Color.white;

        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        GUI.color = previous;

        string label = heat.IsOverheated ? "과열!" : $"과열 {heat.CurrentStage}단계";
        GUI.Label(new Rect(back.x, back.y - 18f, back.width, 18f), label);
    }
}
