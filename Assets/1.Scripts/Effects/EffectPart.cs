using System;
using Ami.BroAudio;
using UnityEngine;

/// <summary>
/// 컴포지트 이펙트의 한 조각. 파티클 프리팹 하나 + (선택) 사운드 하나 + 오프셋 + 지연.
///
/// 예고·타격·잔류의 "3막"을 코드가 아니라 데이터로 표현하기 위한 단위다.
/// 사운드가 여기 들어 있는 이유(결정 13): delay를 호출자가 따로 관리하면
/// 타이밍 값이 두 군데로 복제돼, 이펙트만 튜닝했을 때 에러 없이 싱크만 어긋난다.
/// </summary>
[Serializable]
public class EffectPart
{
    [Tooltip("단일 기술만. ParticleSystem과 VisualEffect를 섞지 않는다 (프리팹 규칙). 비워두면 사운드만 재생한다")]
    public GameObject prefab;

    [Tooltip("BroAudio 원샷. 이펙트에 딸린 사운드는 여기에만 둔다. 비워두면 무시")]
    public SoundID sound;

    [Tooltip("재생 지점 기준 로컬 오프셋")]
    public Vector3 offset;

    [Tooltip("재생 시작부터 이 파트가 발화하기까지의 지연(초). 3막을 데이터로 표현하는 값")]
    [Min(0f)] public float delay;

    /// <summary>이 파트가 실제로 무언가를 하는가. 둘 다 비어 있으면 발화해도 아무 일이 없다.</summary>
    public bool IsEmpty => prefab == null && !sound.IsValid();
}
