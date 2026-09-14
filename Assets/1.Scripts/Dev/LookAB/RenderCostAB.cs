// ----------------------------------------------------------------------------
//  RenderCostAB.cs — 가림 처리 두 방식의 비용을 같은 자리에서 재기 위한 토글 (개발용)
//
//  재려는 것: 「구 투명화(디더 클립)」 vs 「신 실루엣(윤곽선)」 의 프레임 비용 차이.
//  숫자는 ProfilerHUD(F8) 가 읽는다 — 이 스크립트는 무엇을 켤지만 정한다.
//
//  🔴 왜 한 번의 Play 로 둘을 왕복할 수 없는가(이게 이 파일의 존재 이유다):
//     구 시스템은 렌더 패스가 아니라 **머티리얼 교체**다. WallOcclusionDriver 가 OnEnable 에서
//     벽 머티리얼을 오클루전 변종으로 바꾸는데, **되돌리는 코드가 없다.**
//     그래서 Play 중에 껐다 켜면 "껐다"가 원래 상태로 돌아가지 않아 비교가 오염된다.
//     구 시스템은 반드시 **Play 시작 시점에** 켜거나 꺼야 한다 → 별도 Play 가 필요하다.
//
//  측정 순서 (ProfilerHUD 를 F8 로 켜고, 같은 자리·같은 시야에서 읽을 것)
//    Play 1 : startWithWallOcclusion = false
//             F7 로 실루엣 ON/OFF 왕복        → 차이 = 신 실루엣 비용
//    Play 2 : startWithWallOcclusion = true
//             F7 로 실루엣을 꺼 둔 채로 읽음   → 차이(Play1 의 실루엣 OFF 대비) = 구 투명화 비용
//
//  ⚠️ 이 컴포넌트는 결과물이 아니라 판정 도구다(LookToggle 과 같은 부류).
//     숫자가 나오면 이 스크립트와 씬 오브젝트를 지우는 것이 마무리다.
// ----------------------------------------------------------------------------
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class RenderCostAB : MonoBehaviour
{
#if ENABLE_INPUT_SYSTEM
    // F8 = ProfilerHUD, F9 = LookToggle, F10 = 디버그 부활. F7 이 비어 있다.
    [Tooltip("실루엣 ON/OFF 토글 키.")]
    [SerializeField] private Key toggleKey = Key.F7;
#endif

    [Header("시작 상태")]
    [Tooltip("Play 시작 시 실루엣을 켤지. 측정은 보통 켠 채로 시작해 F7 로 끄며 비교한다.")]
    [SerializeField] private bool startWithSilhouette = true;

    [Tooltip("Play 시작 시 구 투명화(WallOcclusionDriver)를 켤지. " +
             "🔴 Play 중에는 바꿀 수 없다 — 머티리얼 교체가 되돌아오지 않기 때문이다.")]
    [SerializeField] private bool startWithWallOcclusion;

    private WallOcclusionDriver _driver;

    private void Start()
    {
        PlayerSilhouetteFeature.DevEnabled = startWithSilhouette;

        _driver = FindFirstObjectByType<WallOcclusionDriver>(FindObjectsInactive.Include);
        if (_driver != null)
            _driver.enabled = startWithWallOcclusion;

        Report("시작");
    }

    private void OnDestroy()
    {
        // 정적 필드는 Play 를 나가도 남는다(도메인 리로드를 끈 경우). 원상복구해 둔다.
        PlayerSilhouetteFeature.DevEnabled = true;
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return;
        if (!Keyboard.current[toggleKey].wasPressedThisFrame) return;

        PlayerSilhouetteFeature.DevEnabled = !PlayerSilhouetteFeature.DevEnabled;
        Report("토글");
#endif
    }

    private void Report(string why)
    {
        string wall = _driver == null
            ? "드라이버 없음"
            : (_driver.enabled ? "켜짐" : "꺼짐");

        Debug.Log(
            $"[RenderCostAB] {why} — 실루엣={(PlayerSilhouetteFeature.DevEnabled ? "켜짐" : "꺼짐")} / " +
            $"구 투명화={wall}. 숫자는 F8(ProfilerHUD) 로 읽을 것.", this);
    }
}
#endif
