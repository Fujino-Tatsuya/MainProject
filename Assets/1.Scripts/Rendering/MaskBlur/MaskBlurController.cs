using UnityEngine;

// 씬 상주 게이트. 이 컴포넌트가 살아 있는 씬에서만 마스크 블러가 돈다.
//
// 왜 필요한가: 렌더러 피처는 PC_Renderer 를 쓰는 모든 카메라에 걸린다. 게이트가 없으면
// 타이틀·로비·결과 화면까지 외곽이 흐려진다. FogRendererFeature 가 FogManager.HasActiveInstance
// 로 같은 문제를 푸는 것과 동일한 패턴이며, 씬마다 다른 설정을 물릴 수 있는 이점도 같다.
//
// ⚠️ 씬 상주 매니저 부재는 기능을 조용히 끈다(2026-07-28 교훈). 맵에서 블러가 안 보이면
//    설정값을 뒤지기 전에 이 컴포넌트가 씬에 있는지부터 확인할 것.
[DisallowMultipleComponent]
public sealed class MaskBlurController : MonoBehaviour
{
    [SerializeField] private MaskBlurSettings settings;

    private static MaskBlurController s_active;

    // 렌더러 피처가 매 프레임 물어보는 유일한 창구.
    // null 이면 패스를 큐잉하지 않는다 = 비용 0.
    public static MaskBlurSettings ActiveSettings
    {
        get
        {
            if (s_active == null || !s_active.isActiveAndEnabled)
                return null;

            MaskBlurSettings s = s_active.settings;
            return s != null && s.enabled ? s : null;
        }
    }

    public MaskBlurSettings Settings => settings;

    public void SetSettings(MaskBlurSettings newSettings) => settings = newSettings;

    // 픽셀레이트에는 런타임 오버라이드를 두지 않는다.
    //
    // 한때 룩 토글이 on/off 를 소유하도록 static bool? 오버라이드를 뒀는데, 2026-08-06 에
    // 픽셀레이트가 룩 A·B '공통'으로 확정되면서 토글이 건드릴 이유가 없어져 걷어냈다.
    // 이제 판단 주체는 MaskBlurSettings.pixelateEnabled 하나다.
    //
    // 🔴 되살릴 일이 생기면 SO 필드를 코드가 직접 쓰지 말 것 — ScriptableObject 는
    //    Play 중에 코드가 쓰면 애셋이 영구 수정되고 Play 를 끝내도 남는다
    //    (Volume.sharedProfile 함정과 같은 부류 — 2026-08-05 교훈). 정적 상태로 분리해야 한다.

    private void OnEnable()
    {
        if (s_active != null && s_active != this)
        {
            Debug.LogWarning(
                $"[MaskBlur] 컨트롤러가 둘 이상이다 — '{s_active.name}' 를 '{name}' 로 교체한다. " +
                "씬에 하나만 두는 것을 전제로 한 구조다.",
                this);
        }

        s_active = this;

        if (settings == null)
        {
            Debug.LogWarning(
                "[MaskBlur] Settings 가 비어 있어 블러가 돌지 않는다. " +
                "MaskBlurSettings 애셋을 물릴 것.",
                this);
        }
    }

    private void OnDisable()
    {
        if (s_active == this)
            s_active = null;
    }
}
