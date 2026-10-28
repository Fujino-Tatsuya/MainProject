using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 🔸 거너 임시 과열 게이지(D14) — 거너 고유 UI 프리팹(Gunner/GunnerHeatGauge.prefab)으로 Player_Gunner 에 중첩된다.
/// 공용 CombatHUD 와 분리해 둔 것은 의도다(캐릭터 고유 자원 UI). 정식 아트가 오면 이 프리팹만 교체한다.
/// 오너 화면에서만 보인다. 단계별 색(기본 흰 → 1 노랑 → 2 주황 → 3 빨강), 과열 시 깜빡임.
/// </summary>
public class GunnerHeatGauge : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [Tooltip("채움 막대 — anchorMax.x 를 과열 비율로 바꾼다(스프라이트 없이 동작).")]
    [SerializeField] private RectTransform fill;
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text label;

    [SerializeField] private Color[] stageColors =
    {
        new Color(0.95f, 0.95f, 0.95f),
        new Color(1f, 0.9f, 0.2f),
        new Color(1f, 0.55f, 0.1f),
        new Color(1f, 0.15f, 0.1f),
    };
    [SerializeField] private Color overheatBlinkColor = Color.white;
    [SerializeField, Min(0.05f)] private float overheatBlinkPeriod = 0.4f;

    private GunnerHeat heat;
    private Player player;
    private int shownStage = int.MinValue; // -1 = 과열. 같은 표시면 문구를 다시 만들지 않는다

    private void Awake()
    {
        heat = GetComponentInParent<GunnerHeat>();
        player = GetComponentInParent<Player>();
        if (canvas == null)
            canvas = GetComponentInChildren<Canvas>(true);
    }

    private void LateUpdate()
    {
        // 정온 운용(할 일 127 3/4)이면 과열이 없으니 게이지를 숨긴다(캔버스만 끈다 — 프리팹·이펙트 무수정).
        bool show = heat != null && heat.Data != null && heat.HeatEnabled && (player == null || !player.IsSpawned || player.IsOwner);
        if (canvas != null && canvas.enabled != show)
            canvas.enabled = show;
        if (!show)
            return;

        if (fill != null)
            fill.anchorMax = new Vector2(heat.Normalized, fill.anchorMax.y);

        if (fillImage != null && stageColors != null && stageColors.Length > 0)
        {
            Color color = stageColors[Mathf.Clamp(heat.CurrentStage, 0, stageColors.Length - 1)];
            if (heat.IsOverheated && Mathf.Repeat(Time.time,overheatBlinkPeriod) < overheatBlinkPeriod * 0.5f)
                color = overheatBlinkColor;
            fillImage.color = color;
        }

        if (label != null)
        {
            int shown = heat.IsOverheated ? -1 : heat.CurrentStage;
            if (shown != shownStage)
            {
                shownStage = shown;
                label.text = LabelText(heat.CurrentStage, heat.IsOverheated);
            }
        }
    }

    private void OnEnable() => Loc.Changed += ForceRelabel;
    private void OnDisable() => Loc.Changed -= ForceRelabel;
    private void ForceRelabel() => shownStage = int.MinValue;

    /// <summary>라벨 문구 — 과열이면 <see cref="UiStrings.GunnerHeatOverheated"/>, 아니면 <see cref="UiStrings.GunnerHeatStage"/>({stage}).</summary>
    public static string LabelText(int stage, bool overheated) =>
        overheated
            ? UiStrings.GunnerHeatOverheated.Text
            : UiStrings.GunnerHeatStage.Format(new Dictionary<string, string> { ["stage"] = stage.ToString(CultureInfo.InvariantCulture) });
}
