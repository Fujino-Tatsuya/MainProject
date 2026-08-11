using UnityEngine;

/// <summary>
/// <b>파트 드라이버</b> — 특정 파티클 기술 하나(Shuriken / VFX Graph / Decal / Trail)를
/// 재생·정지·감속·초기화하는 방법을 아는 어댑터.
///
/// 이 인터페이스는 "서비스 전체"가 아니라 <b>파트 단위</b>다. 컴포지트 이펙트는 파트마다 기술이
/// 다를 수 있어서 "이 엔트리를 어느 시스템이 재생하나"라는 질문 자체가 성립하지 않는다.
/// 서비스 전체를 인터페이스로 두면 기술을 추가할 때 풀링·수명·핸들을 통째로 복제하게 된다.
///
/// 새 기술을 붙이는 방법: 이 인터페이스를 구현하고 <see cref="EffectManager"/>의 Awake에 한 줄 추가.
/// 그 외 코드는 건드리지 않는다 — 그게 v1의 성공 판정이다.
/// </summary>
public interface IEffectSystem
{
    /// <summary>
    /// 이 인스턴스를 내가 몰 수 있는가. 드라이버 배정은 데이터의 enum이 아니라 런타임 탐색으로 정한다
    /// (프리팹이 진실의 원천. 데이터에 적으면 어긋나도 조용히 틀린다).
    /// 두 드라이버가 동시에 손을 들면 = 프리팹 내 기술 혼용 → 금지 규칙이 자동 검증된다.
    /// </summary>
    bool CanDrive(GameObject instance);

    /// <summary>풀에서 대출된 인스턴스를 처음부터 재생한다.</summary>
    void Play(GameObject instance);

    /// <summary>
    /// 정지. <paramref name="immediate"/> = false면 <b>발생만 멈추고 살아 있는 입자는 수명대로 사라진다</b>
    /// (자연스러운 해제의 정체가 이 인자다). true면 즉시 지운다.
    /// </summary>
    void Stop(GameObject instance, bool immediate);

    /// <summary>재생 속도 배율. 히트스톱이 이 경로로 이펙트를 얼린다. 0 = 완전 정지.</summary>
    void SetPlayRate(GameObject instance, float rate);

    /// <summary>풀에 반납하기 직전 초기화. 다음 대출자가 이전 상태를 물려받지 않게 한다.</summary>
    void ResetForPool(GameObject instance);
}
