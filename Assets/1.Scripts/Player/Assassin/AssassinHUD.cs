using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 🔸 어쌔신 임시 HUD(PLAN-assassin A12) — 어쌔신 고유 UI 프리팹(Assassin/AssassinHUD.prefab)으로 <c>Assassin_Armature/HUD</c> 에 중첩된다.
/// 공용 CombatHUD 와 분리한 것은 캐릭터 HUD 규약이다(player-prefabs.md §7 5-2). 오너 화면에서만 보이고, 유령 중엔 Armature 와 함께 숨는다.
/// 표시: 분노 게이지 가로 바(최소 변신량 눈금, 최소량 이상이면 색 변경, 변신 중엔 줄어든다) · 변신 남은 시간 바(시작 후 해제 가능 시점 눈금)
/// · 일반 E 강화 준비 · 변신 종료 대기.
/// 값은 전부 <see cref="AssassinState"/> 스냅샷에서 매 프레임 읽는다(전 피어 복제값이라 늦은 접속도 같은 화면).
/// 위치·모양은 임시 — 정식 UI 가 오면 이 프리팹만 교체한다.
/// </summary>
public sealed class AssassinHUD : MonoBehaviour
{
    [SerializeField] private Canvas canvas;

    [Header("분노 게이지")]
    [Tooltip("채움 막대 — anchorMax.x 를 게이지 비율로 바꾼다(스프라이트 없이 동작).")]
    [SerializeField] private RectTransform rageFill;
    [SerializeField] private Image rageFillImage;
    [Tooltip("최소 변신량 눈금 — 게이지 비율 축 위에 놓는다.")]
    [SerializeField] private RectTransform rageMinTick;
    [SerializeField] private TMP_Text rageLabel;
    [SerializeField] private Color rageBelowMinColor = new Color(0.55f, 0.35f, 0.7f, 1f);
    [SerializeField] private Color rageReadyColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color rageTransformedColor = new Color(0.85f, 0.5f, 1f, 1f);

    [Header("변신 남은 시간")]
    [SerializeField] private GameObject transformRoot;
    [Tooltip("채움 막대 — anchorMax.x 를 남은 비율로 바꾼다(스프라이트 없이 동작).")]
    [SerializeField] private RectTransform transformFill;
    [SerializeField] private Image transformFillImage;
    [Tooltip("해제 가능 시점(시작 후 releaseLockSeconds) 눈금 — 남은 비율 축 위에 놓는다.")]
    [SerializeField] private RectTransform releaseTick;
    [SerializeField] private TMP_Text transformLabel;
    [SerializeField] private Color transformLockedColor = new Color(0.6f, 0.3f, 0.9f, 1f);
    [SerializeField] private Color transformReleasableColor = new Color(0.85f, 0.5f, 1f, 1f);
    [SerializeField] private Color transformEndPendingColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Header("일반 E 강화 준비")]
    [SerializeField] private GameObject enhancedReadyRoot;

    private AssassinState state;
    private Player player;
    private int shownRage = -1;

    private void Awake()
    {
        state = GetComponentInParent<AssassinState>();
        player = GetComponentInParent<Player>();
        if (canvas == null)
            canvas = GetComponentInChildren<Canvas>(true);
    }

    private void LateUpdate()
    {
        bool show = state != null && (player == null || !player.IsSpawned || player.IsOwner);
        if (canvas != null && canvas.enabled != show)
            canvas.enabled = show;
        if (!show)
            return;

        RefreshRage();
        RefreshTransform();

        if (enhancedReadyRoot != null && enhancedReadyRoot.activeSelf != state.IsEnhancedReady)
            enhancedReadyRoot.SetActive(state.IsEnhancedReady);
    }

    private void RefreshRage()
    {
        float max = state.MaxRage;
        float rage = state.Rage;
        bool transformed = state.IsTransformed;

        if (rageFill != null)
        {
            float ratio = max > 0f ? Mathf.Clamp01(rage / max) : 0f;
            rageFill.anchorMax = new Vector2(ratio, rageFill.anchorMax.y);
        }

        // 최소량 이상 색은 "R 로 변신할 수 있는 게이지" 표시다 — R 쿨타임은 공용 스킬 칸이 보여 준다.
        if (rageFillImage != null)
            rageFillImage.color = transformed ? rageTransformedColor
                : state.CanBeginTransform ? rageReadyColor
                : rageBelowMinColor;

        if (rageMinTick != null)
        {
            float tick = max > 0f ? Mathf.Clamp01(state.MinTransformRage / max) : 0f;
            rageMinTick.anchorMin = new Vector2(tick, rageMinTick.anchorMin.y);
            rageMinTick.anchorMax = new Vector2(tick, rageMinTick.anchorMax.y);
        }

        int shown = Mathf.CeilToInt(rage);
        if (rageLabel != null && shown != shownRage)
        {
            shownRage = shown;
            rageLabel.text = shown.ToString();
        }
    }

    private void RefreshTransform()
    {
        bool transformed = state.IsTransformed;
        if (transformRoot != null && transformRoot.activeSelf != transformed)
            transformRoot.SetActive(transformed);
        if (!transformed)
            return;

        float duration = state.TransformDuration;
        float remaining = state.TransformRemaining;
        bool endPending = state.IsTransformEndPending;
        bool releasable = state.CanRequestRelease;

        if (transformFill != null)
        {
            float ratio = duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;
            transformFill.anchorMax = new Vector2(ratio, transformFill.anchorMax.y);
        }

        if (transformFillImage != null)
            transformFillImage.color = endPending ? transformEndPendingColor
                : releasable ? transformReleasableColor
                : transformLockedColor;

        if (releaseTick != null)
        {
            // 바는 남은 시간이라 오른쪽 끝(시작)에서 줄어든다 — 해제 가능 시점 = 남은 비율 (지속 − 잠금) / 지속.
            float tick = duration > 0f ? Mathf.Clamp01((duration - state.ReleaseLockSeconds) / duration) : 0f;
            releaseTick.anchorMin = new Vector2(tick, releaseTick.anchorMin.y);
            releaseTick.anchorMax = new Vector2(tick, releaseTick.anchorMax.y);
        }

        if (transformLabel != null)
            transformLabel.text = endPending
                ? "변신 종료 대기"
                : releasable
                    ? $"변신 {remaining:0.0}초 · R 해제 가능"
                    : $"변신 {remaining:0.0}초";
    }
}
