using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스 제한시간 게이지(표시 전용). 네트워크를 직접 만지지 않고
/// <see cref="BossTimerManager"/> 의 복제된 상태만 읽는다 — 값의 주인은 서버다.
///
/// 아트 3종(<c>slot_timer</c> 틀 / <c>gauge_timer</c> 채움 / <c>icon_timer_boss</c> 해골 캡)은
/// <c>Tools/UI/Authoring/CombatHUD — 미니맵·보스타이머 슬롯 생성</c> 이 배선한다.
///
/// 🔴 게이지 방향은 <see cref="BossTimerManager"/> 의 인스펙터 토글이 정한다(기획 미확정).
///    여기서는 <c>Fill01</c> 을 그대로 그린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossTimerHUD : MonoBehaviour
{
    [Tooltip("채움 이미지. Image Type = Filled / Horizontal 이어야 한다.")]
    [SerializeField] private Image fillImage;

    [Tooltip("타이머 위젯 전체의 표시를 제어한다. 보스 타이머가 없는 씬에서는 투명하게 둔다. " +
             "🔴 GameObject 를 끄지 않는다 — 자기 자신을 끄면 Update 가 멈춰 되살아날 수 없다.")]
    [SerializeField] private CanvasGroup group;

    [Tooltip("표시 갱신 주기(초). 게이지는 1초에 몇 번만 갱신해도 충분하다.")]
    [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;

    private float _timer;

    private void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        if (fillImage == null) fillImage = GetComponentInChildren<Image>(true);
        Apply();
    }

    private void OnEnable()
    {
        _timer = 0f;
        Apply();
    }

    private void Update()
    {
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;

        _timer = refreshInterval;
        Apply();
    }

    private void Apply()
    {
        BossTimerManager timer = BossTimerManager.Instance;

        // 보스가 없는 씬(로비·테스트 씬)에서는 위젯을 감춘다 — 빈 틀만 떠 있으면 버그로 읽힌다.
        //
        // 🔴 예전에는 `root.SetActive(false)` 였는데, 저작 스크립트가 `root` 를 **이 컴포넌트 자신의
        //    GameObject** 로 배선한다. 초기화 순서상 Awake 시점에 매니저가 아직 없으면 스스로를 꺼버리고,
        //    그러면 Update 가 돌지 않아 **매니저가 나중에 생겨도 영원히 되살아나지 못했다.**
        //    알파로 감추면 컴포넌트는 계속 살아 있어 복구된다.
        bool available = timer != null && timer.isActiveAndEnabled;
        if (group != null)
        {
            group.alpha = available ? 1f : 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        if (!available || fillImage == null)
            return;

        fillImage.fillAmount = Mathf.Clamp01(timer.Fill01);
    }
}
