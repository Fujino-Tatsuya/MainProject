// ----------------------------------------------------------------------------
//  LookToggle.cs — 룩 A/B 비교 토글 (개발용, 릴리스 빌드에서 컴파일 제외)
//
//  목적: 두 룩을 키 하나로 왕복해 팀장이 육안으로 고르게 한다.
//    A = 채도 살림 (디밍·차폐 없음)
//    B = FogManager 디밍 + 저채도 + 시야 차폐
//
//  마스크 블러와 픽셀레이트는 **양쪽 공통**이라 이 클래스가 건드리지 않는다(2026-08-06 확정).
//  둘은 MaskBlurSettings 애셋이 단독으로 결정한다 — 이 토글이 가르는 것은 디밍·차폐뿐이다.
//
//  ⚠️ 이 컴포넌트는 결과물이 아니라 판정 도구다. 룩이 확정되면
//     승자 값을 기본값으로 커밋하고 이 스크립트와 씬 컴포넌트를 삭제하는 것이 마무리다.
//     (ProfilerHUD 와 같은 부류 — #if 로 감싸져 있어 릴리스 빌드에서는 클래스가 없고,
//      씬에 남겨두면 missing script 경고가 된다.)
//
//  🔴 이 클래스가 지키는 불변식: ScriptableObject 애셋에 쓰지 않는다.
//     SO 에 쓰면 Play 를 끝내도 애셋이 수정된 채로 남는다. 이 토글이 만지는 것은
//     씬 컴포넌트(FogManager)와 애셋 '참조'뿐이며, 둘 다 Play 종료 시 되돌아간다.
//
//  ⚠️ 룩 B 는 시야 차폐(LoS)까지 켠다 — 팀장 지시(2026-08-06).
//     계획서 §8.2 에는 "losEnabled 를 절대 켜지 않는다"고 적었지만 뒤집혔다.
//     "플레이어 주변을 뺀 나머지가 매우 어두워지는 것"이 룩 B 의 일부이기 때문이다.
//     그래서 룩 B 에서는 먼 거리의 적이 보이지 않는다 — 이건 결함이 아니라 의도다.
//     대신 룩을 되돌릴 때 반드시 원래 값으로 복구해야 게임플레이가 안 새어나간다
//     (RestoreSnapshot 이 담당).
// ----------------------------------------------------------------------------
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;   // 신 Input System (이 프로젝트: Active Input Handling = New)
#endif

[DisallowMultipleComponent]
public sealed class LookToggle : MonoBehaviour
{
    public enum Look
    {
        // 정수로 직렬화되므로 이름을 바꿔도 씬 값(startLook)은 안 깨진다.
        A_Current = 0,      // 디밍·차폐 없음
        B_DimOccluded = 1,  // 디밍 + 저채도 + 시야 차폐
    }

    [Header("토글")]
#if ENABLE_INPUT_SYSTEM
    // F8 = ProfilerHUD, F10 = 디버그 부활, M = 맵 오버뷰, [ ] = 카메라 타겟 전환.
    // F9 가 비어 있어 기본값으로 쓴다.
    [Tooltip("룩 전환 키. F8(ProfilerHUD)·F10(디버그 부활)·M·[·] 는 이미 쓰인다.")]
    [SerializeField] private Key toggleKey = Key.F9;
#endif

    [Tooltip("Play 시작 시의 룩. 기본은 A — 토글을 넣었다고 화면이 바뀌면 안 된다.")]
    [SerializeField] private Look startLook = Look.A_Current;

    [Header("배선 (비워두면 씬에서 자동 탐색)")]
    [Tooltip("룩 B 의 디밍·저채도를 담당한다. 비활성 상태로 씬에 있는 것이 정상이다.")]
    [SerializeField] private FogManager fogManager;

    [Tooltip("볼륨 프로파일도 룩 축에 넣고 싶을 때만 쓴다. 아래 profileB 가 비어 있으면 무시된다.")]
    [SerializeField] private Volume globalVolume;

    [Tooltip("룩 B 전용 볼륨 프로파일(선택). 비워두면 프로파일은 건드리지 않는다.\n\n" +
             "값이 아니라 애셋 참조만 교체하므로 애셋이 영구 수정되지 않는다 — " +
             "sharedProfile 의 '값'을 런타임에 쓰는 것이 위험한 것이고, 참조 교체는 " +
             "씬 컴포넌트 변경이라 Play 종료 시 되돌아간다.")]
    [SerializeField] private VolumeProfile profileB;

    [Header("화면 표시")]
    [Tooltip("전환 후 현재 룩을 몇 초간 표시할지. 0 이면 표시하지 않는다.\n" +
             "어느 룩을 보고 있는지 모르면 비교가 성립하지 않으므로 켜 두는 것을 권한다.")]
    [Range(0f, 10f)][SerializeField] private float toastSeconds = 2.5f;

    private Look _current;
    private float _toastUntil;

    // 원복용 스냅샷. Play 중 변경은 어차피 폐기되지만, 토글을 Play 중에 끄는 경우까지
    // 정합을 지키려면 명시적으로 되돌리는 편이 안전하다.
    private bool _snapTaken;
    private bool _snapFogComponentEnabled;
    private bool _snapFogEnabled;
    private bool _snapDimEnabled;
    private bool _snapLosEnabled;
    private VolumeProfile _snapProfile;

    private bool _started;

    // ⚠️ 초기화를 OnEnable 이 아니라 Start 에서 한다. MapScene 은 additive 로 로드되고
    //    참조 자동 탐색(FindAnyObjectByType)은 씬의 모든 오브젝트가 활성화된 뒤여야
    //    안전하다. OnEnable 은 로드 도중에 불릴 수 있어 null 을 집을 수 있다.
    private void Start()
    {
        ResolveReferences();
        TakeSnapshot();
        _started = true;
        Apply(startLook, announce: toastSeconds > 0f);
    }

    private void OnEnable()
    {
        // Start 이전이면 아무것도 하지 않는다. 이후의 재활성화만 현재 룩을 복원한다.
        if (_started)
            Apply(_current, announce: false);
    }

    private void OnDisable() => RestoreSnapshot();

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        // Keyboard.current 는 키보드가 없거나 아직 초기화되지 않으면 null 이다.
        if (Keyboard.current == null)
            return;

        if (Keyboard.current[toggleKey].wasPressedThisFrame)
            Toggle();
#endif
    }

    public void Toggle() =>
        Apply(_current == Look.A_Current ? Look.B_DimOccluded : Look.A_Current, announce: true);

    // ------------------------------------------------------------------------
    private void Apply(Look look, bool announce)
    {
        _current = look;
        bool isB = look == Look.B_DimOccluded;

        // 픽셀레이트는 여기서 다루지 않는다 — 룩 A·B 공통이라 MaskBlurSettings 가 단독으로
        // 결정한다(2026-08-06). A/B 를 가르는 것은 디밍·저채도·차폐뿐이다.
        ApplyDim(isB);
        ApplyVolumeProfile(isB);

        if (!announce)
            return;

        if (toastSeconds > 0f)
            _toastUntil = Time.unscaledTime + toastSeconds;

        // 콘솔에도 남긴다. Game View 를 안 띄운 채 검증할 때는 화면 토스트를 볼 수 없고,
        // 나중에 "F9 를 눌렀는데 안 바뀐다"를 판정할 때 입력이 도달했는지부터 갈라야 한다.
        Debug.Log($"[LookToggle] {Describe(_current)}", this);
    }

    // 픽셀레이트·마스크 블러는 양쪽 공통이므로 라벨에 넣지 않는다 —
    // 공통 항목을 적으면 "B 에만 픽셀이 있다"로 읽혀 비교 판단을 흐린다.
    private static string Describe(Look look) =>
        look == Look.A_Current
            ? "LOOK A — 채도 살림 (디밍·차폐 없음)"
            : "LOOK B — 디밍 + 저채도 + 시야 차폐";

    // 디밍·저채도. 새로 만들지 않고 FogManager 의 dim 을 되살린다 —
    // 원래 룩의 정체가 이것이고, FogProfile 에 튜닝값이 그대로 남아 있다.
    private void ApplyDim(bool on)
    {
        if (fogManager == null)
            return;

        if (on)
        {
            // 포그는 원래도 꺼져 있었다(씬 값 fogEnabled: 0). 어둡게 만든 것은 dim 이다.
            fogManager.fogEnabled = false;
            fogManager.dimEnabled = true;

            // 차폐까지 켠다 — 플레이어 주변을 뺀 나머지가 매우 어두워지는 것이 룩 B 다.
            // ⚠️ PushLos 는 dimEnabled 안에서만 불린다. dim 을 끄면 차폐도 함께 죽는다.
            fogManager.losEnabled = true;
        }

        // 컴포넌트 on/off 가 실제 게이트다. FogRendererFeature 가
        // FogManager.HasActiveInstance 를 보고 패스를 큐잉하므로, 끄면 비용이 0 이고
        // OnDisable 이 _DimEnabled·_LosEnabled 전역을 0 으로 되돌려 잔상도 안 남는다.
        fogManager.enabled = on;
    }

    private void ApplyVolumeProfile(bool on)
    {
        if (globalVolume == null || profileB == null)
            return;

        globalVolume.sharedProfile = on ? profileB : _snapProfile;
    }

    // ------------------------------------------------------------------------
    private void ResolveReferences()
    {
        // ⚠️ FindObjectsInactive.Include 가 필요하다. FogManager 는 비활성 컴포넌트로
        //    씬에 있는 것이 정상이고, 기본 탐색은 비활성을 건너뛰어 null 을 돌려준다.
        if (fogManager == null)
            fogManager = FindAnyObjectByType<FogManager>(FindObjectsInactive.Include);

        if (globalVolume == null)
            globalVolume = FindAnyObjectByType<Volume>(FindObjectsInactive.Include);

        if (fogManager == null)
        {
            Debug.LogWarning(
                "[LookToggle] FogManager 를 못 찾아 룩 B 의 디밍이 빠진다. " +
                "픽셀레이트만 토글된다.",
                this);
        }
    }

    private void TakeSnapshot()
    {
        if (_snapTaken)
            return;

        if (fogManager != null)
        {
            _snapFogComponentEnabled = fogManager.enabled;
            _snapFogEnabled = fogManager.fogEnabled;
            _snapDimEnabled = fogManager.dimEnabled;
            _snapLosEnabled = fogManager.losEnabled;
        }

        if (globalVolume != null)
            _snapProfile = globalVolume.sharedProfile;

        _snapTaken = true;
    }

    private void RestoreSnapshot()
    {
        if (!_snapTaken)
            return;

        if (fogManager != null)
        {
            fogManager.fogEnabled = _snapFogEnabled;
            fogManager.dimEnabled = _snapDimEnabled;
            fogManager.losEnabled = _snapLosEnabled;
            fogManager.enabled = _snapFogComponentEnabled;
        }

        if (globalVolume != null && _snapProfile != null)
            globalVolume.sharedProfile = _snapProfile;
    }

    // ------------------------------------------------------------------------
    // 어느 룩을 보고 있는지 화면에 알린다. 이게 없으면 왕복 비교가 기억에 의존한다.
    private void OnGUI()
    {
        if (toastSeconds <= 0f || Time.unscaledTime > _toastUntil)
            return;

        string label = Describe(_current);

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };

        const float width = 560f;
        const float height = 40f;
        var rect = new Rect((Screen.width - width) * 0.5f, 24f, width, height);

        // 배경을 깔지 않으면 밝은 화면에서 흰 글자가 안 보인다.
        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.Label(rect, label, style);
        GUI.color = prev;
    }
}
#endif
