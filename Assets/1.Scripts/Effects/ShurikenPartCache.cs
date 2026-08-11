using UnityEngine;

/// <summary>
/// <see cref="ShurikenEffectSystem"/>이 풀 인스턴스마다 붙이는 컴포넌트 캐시.
/// GetComponentsInChildren을 매 재생마다 돌리지 않기 위한 것으로, <b>런타임에만</b> 붙는다.
/// (프리팹 제작자가 컴포넌트를 붙일 필요는 없다 — 그건 결정 16에서 기각한 비용이다.)
///
/// 인스턴스와 수명이 같아서 딕셔너리 캐시처럼 정리 시점을 신경 쓸 필요가 없다.
/// 기술을 추가할 때는 이 컴포넌트를 고치지 말고 드라이버마다 자기 캐시 컴포넌트를 만든다.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
public class ShurikenPartCache : MonoBehaviour
{
    /// <summary>인스턴스 안의 모든 ParticleSystem. 감속·루프 판별용.</summary>
    [System.NonSerialized] public ParticleSystem[] all;

    /// <summary>부모에 ParticleSystem이 없는 최상위 시스템들. 재생·정지는 여기에만 건다(withChildren=true).</summary>
    [System.NonSerialized] public ParticleSystem[] roots;
}
