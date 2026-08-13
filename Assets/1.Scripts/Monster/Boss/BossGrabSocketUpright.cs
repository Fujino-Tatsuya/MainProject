using UnityEngine;

/// <summary>
/// 잡기 소켓의 <b>자세를 세운다</b>. 위치는 부모(손 본)를 그대로 따르고, 회전만 월드 기준으로 바로잡는다.
///
/// ── 왜 필요한가 (2026-08-10, Play 관찰) ──────────────────────────────────────
/// 잡힌 플레이어가 <b>누운 채로</b> 매달렸다. 원인은 플레이어가 소켓의 회전을 그대로 복사하기 때문이다 —
/// <c>PlayerStateController</c> 의 구속 추종이 <c>followTarget.position</c> 과 함께
/// <b><c>followTarget.rotation</c> 도</b> 가져간다.
/// 그런데 소켓은 <c>hand.r</c> 의 자식이고, 이 리그는 <c>rig</c> 노드가 (270.02, 0, 0) 으로 누워 있는 데다
/// 손 본 자체가 애니메이션으로 매 프레임 회전한다. 그래서 <b>정적인 로컬 회전으로는 고칠 수 없다.</b>
///
/// ── 왜 여기서 고치나 ────────────────────────────────────────────────────────
/// 플레이어가 "위치만 따라가게" 바꾸는 것이 더 깨끗하지만, 그쪽은 담당 경계(Player 계통)다.
/// 소켓의 월드 회전을 매 프레임 세워 두면 <b>플레이어 코드를 한 줄도 건드리지 않고</b> 같은 결과가 난다.
///
/// ⚠️ <c>LateUpdate</c> 에서 한다 — 애니메이션이 손 본 포즈를 확정한 <b>뒤</b>여야 위치가 정확하다.
/// ⚠️ 이 컴포넌트는 위치를 건드리지 않는다. 부모를 따라가는 것은 트랜스폼 계층이 알아서 한다.
/// </summary>
[DisallowMultipleComponent]
public class BossGrabSocketUpright : MonoBehaviour
{
    [SerializeField]
    [Tooltip("자세의 기준. 비우면 부모에서 MonsterBase 를 찾고, 그것도 없으면 최상위 루트를 쓴다.")]
    Transform yawSource;

    void Awake()
    {
        if (yawSource != null) return;

        var owner = GetComponentInParent<MonsterBase>();
        yawSource = owner != null ? owner.transform : transform.root;
    }

    void LateUpdate()
    {
        if (yawSource == null) return;

        // 🔴 기준의 **yaw 만** 물려받고 pitch·roll 은 버린다.
        //    보스 루트의 회전을 통째로 쓰면 안 된다 — 루트가 기울면 플레이어도 같이 기운다.
        transform.rotation = Quaternion.Euler(0f, yawSource.eulerAngles.y, 0f);
    }
}
