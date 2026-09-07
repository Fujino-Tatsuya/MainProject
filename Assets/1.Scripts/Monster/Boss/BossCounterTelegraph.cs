using UnityEngine;

// 카운터 창 텔레그래프 기본 구현 — 보스 전체를 노란색으로 틴트한다.
// VFX 가 준비되면 이 컴포넌트를 지우고 같은 인터페이스를 구현한 VFX 컴포넌트로 갈아끼우면 된다.
//
// 🔴 HitFlash 의 SetBaseTint 를 경유하는 것이 핵심이다. 같은 MPB 경로로 직접 칠하면
//    피격 플래시가 끝날 때 머티리얼 원색으로 되돌려 **카운터 색이 피격 한 번에 날아간다.**
//    베이스 틴트로 넣으면 플래시가 이 색 위에서 Lerp 하고, 끝나도 이 색으로 돌아온다.
[DisallowMultipleComponent]
public class BossCounterTelegraph : MonoBehaviour, IBossTelegraph
{
    [SerializeField]
    [Tooltip("카운터 창이 열린 동안의 틴트 색. 기본 = 노란색.")]
    Color windowColor = new Color(1f, 0.85f, 0.2f, 1f);

    HitFlash _flash;

    public void SetCounterWindow(bool open)
    {
        // HitFlash 는 Unit.OnNetworkSpawn 이 자동 부착하므로 Awake 시점엔 없을 수 있다 — 지연 해석한다.
        if (_flash == null)
            _flash = GetComponentInParent<HitFlash>();
        if (_flash == null)
            return;

        if (open)
            _flash.SetBaseTint(windowColor);
        else
            _flash.ClearBaseTint();
    }
}
