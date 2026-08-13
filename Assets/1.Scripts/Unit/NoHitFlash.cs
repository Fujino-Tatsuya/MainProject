using UnityEngine;

/// <summary>
/// 이 오브젝트(와 그 자식)의 렌더러를 <see cref="HitFlash"/> 대상에서 **제외**한다.
///
/// 🔴 왜 필요한가 — 실제로 터진 버그(2026-08-13):
///    <see cref="HitFlash"/> 는 <c>GetComponentsInChildren&lt;Renderer&gt;</c> 로 유닛의 **모든**
///    렌더러를 긁는다. 그래서 보스 자식으로 만들어지는 **연출용 렌더러**(앞뒤 방향 표식의 호 등)까지
///    피격 때 빨갛게 물들었다. 게다가 원래 색을 <c>sharedMaterial</c> 기준으로 캐시하기 때문에
///    플래시가 끝나면 **연출의 저작 색이 아니라 재질 원색으로 복원**된다 —
///    표식 재질이 빨간색이었으므로 한 번 맞으면 앞뒤 표식이 **영구히 빨강**이 됐다.
///
/// ⚠️ 장판(<c>AoeTelegraph</c>)은 HitFlash 가 이미 이름으로 제외하고 있었다. 그 예외를
///    타입 하나에 묶어 두면 새 연출이 추가될 때마다 같은 버그가 재발하므로,
///    **연출 쪽이 스스로 표시하는 방식**으로 바꾼다. VFX(민경) 계통도 이 마커를 붙이면 된다.
///
/// 붙이는 곳: 연출 렌더러의 루트. 런타임 생성물이면 생성 직후 <c>AddComponent</c> 하면 된다.
/// </summary>
[DisallowMultipleComponent]
public class NoHitFlash : MonoBehaviour
{
}
