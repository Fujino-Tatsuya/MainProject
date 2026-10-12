using UnityEngine;

/// <summary>
/// 튜토리얼 스피너봇 머리 위 체력바 규칙(모든 피어). 연습 중(<see cref="TutorialSpinnerPractice.IsHealthLocked"/> — 복제 모드)이면
/// 회색 + 잠금 라벨, 본 모드면 빨강·라벨 없음. 원래 SpinnerBot 에는 머리 위 바가 없다 — 튜토리얼 전용 Variant(<c>SpinnerBot_Tutorial</c>)에만 있다.
/// 시나리오 §3.5: "연습 중임을 알 수 있게 체력 바를 회색·잠금 표시" · Z3-7 "스피너봇 체력 바 활성".
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialSpinnerHealthBarRule : MonoBehaviour, IOverheadHealthBarRule
{
    [SerializeField] private TutorialSpinnerPractice practice;
    [SerializeField] private Color lockedColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    [SerializeField] private Color realColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    [SerializeField] private string lockedLabel = "연습 중 · 체력 잠금";

    private Unit _unit;

    private void Awake()
    {
        if (practice == null) practice = GetComponentInParent<TutorialSpinnerPractice>();
        _unit = GetComponentInParent<Unit>();
    }

    private bool Locked => practice != null && practice.IsSpawned && practice.IsHealthLocked;

    public bool ShouldShow => _unit != null && _unit.CurrentHealth > 0;
    public Color FillColor => Locked ? lockedColor : realColor;
    public string Label => Locked ? lockedLabel : null;
}
