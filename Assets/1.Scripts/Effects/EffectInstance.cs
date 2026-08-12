using System;
using UnityEngine;

/// <summary>
/// 풀에서 만들어진 이펙트 인스턴스의 신분증. 런타임에만 붙는다.
/// 어느 프리팹에서 나왔는지(= 어느 풀로 돌아가야 하는지)와 어느 드라이버가 모는지를 들고 있다.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
public class EffectInstance : MonoBehaviour
{
    /// <summary>이 인스턴스를 찍어낸 프리팹. 풀의 키.</summary>
    [NonSerialized] public GameObject sourcePrefab;

    /// <summary>런타임 탐색으로 배정된 드라이버. 아무도 손을 들지 않았으면 null.</summary>
    [NonSerialized] public IEffectSystem driver;

    /// <summary>
    /// 프리팹에 저작된 원래 scale. 인스턴스를 만들 때 한 번 기록한다.
    /// 배율 재생(<see cref="EffectManager.Play"/>의 scale 인자)이 이 값에 곱해지고,
    /// 풀에 반납될 때 이 값으로 되돌아간다 — 풀 키는 프리팹이라 <b>배율이 다른 재생이 같은 인스턴스를 돌려쓴다.</b>
    /// </summary>
    [NonSerialized] public Vector3 originalScale = Vector3.one;
}
