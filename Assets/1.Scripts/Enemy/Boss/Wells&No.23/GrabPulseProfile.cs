using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GrabAttack 동안 팔을 훑는 전기 펄스의 상태별 튜닝 값.
///
/// <b>이 목록이 "언제 재생할지"까지 정한다</b> — <see cref="GrabPulseDriver"/>는 애니메이터의 현재 상태가
/// 여기 적혀 있을 때만 펄스를 낸다. 그래서 항목을 지우면 그 상태에서 이펙트가 사라지고,
/// 다른 패턴에 같은 연출을 붙이고 싶으면 상태 이름 한 줄만 추가하면 된다.
/// (기본값 fallback을 두지 않는 이유: 못 찾은 상태에 값을 물려주면 모든 상태에서 재생돼 버린다)
///
/// 애니메이터 그래프가 아니라 SO에 두는 이유: 밸런싱하는 사람이 상태 노드를 클릭해 다니지 않아도 되고,
/// 상태를 복제·재배치해도 값이 유실되지 않는다.
/// </summary>
[CreateAssetMenu(fileName = "GrabPulseProfile", menuName = "Effects/Grab Pulse Profile")]
public class GrabPulseProfile : ScriptableObject
{
    /// <summary>애니메이터 상태 하나에 대한 펄스 설정.</summary>
    [Serializable]
    public class StateSetting
    {
        [Tooltip("애니메이터 상태 이름. GrabAttack 서브 스테이트머신 기준: Grab / Holding / Throw")]
        public string stateName;

        [Tooltip("이 상태에서 새 펄스를 낼지 여부. 꺼도 이미 떠 있는 펄스는 제 갈 길을 간다")]
        public bool emit = true;

        [Tooltip("펄스 하나가 어깨에서 손까지 가는 데 걸리는 시간(초)")]
        [Min(0.02f)] public float travelTime = 0.4f;

        [Tooltip("펄스 시작과 다음 펄스 시작 사이의 간격(초). travelTime보다 짧으면 펄스가 겹친다(의도된 동작)")]
        [Min(0.02f)] public float interval = 0.6f;
    }

    [Tooltip("펄스를 재생할 상태들. 여기 없는 상태에서는 아무것도 나오지 않는다")]
    public StateSetting[] states;

    // stateName -> 설정. Animator.StringToHash는 shortNameHash와 같은 해시를 낸다.
    private Dictionary<int, StateSetting> _byHash;

    private void OnEnable() => _byHash = null;   // 인스펙터에서 이름을 고치면 다시 짓는다
    private void OnValidate() => _byHash = null;

    /// <summary>현재 애니메이터 상태의 shortNameHash로 설정을 찾는다. 목록에 없으면 null.</summary>
    public StateSetting Resolve(int shortNameHash)
    {
        if (_byHash == null) Rebuild();
        return _byHash.TryGetValue(shortNameHash, out StateSetting s) ? s : null;
    }

    private void Rebuild()
    {
        _byHash = new Dictionary<int, StateSetting>();
        if (states == null) return;

        for (int i = 0; i < states.Length; i++)
        {
            StateSetting s = states[i];
            if (s == null || string.IsNullOrEmpty(s.stateName)) continue;

            int hash = Animator.StringToHash(s.stateName);
            // 같은 이름을 두 번 적으면 뒤의 것이 조용히 먹히는 대신 알려준다.
            if (!_byHash.ContainsKey(hash)) _byHash.Add(hash, s);
            else Edit.LogWarning($"[No.23] GrabPulseProfile에 '{s.stateName}' 상태가 중복으로 적혀 있다. 첫 항목만 쓴다.", this);
        }
    }
}
