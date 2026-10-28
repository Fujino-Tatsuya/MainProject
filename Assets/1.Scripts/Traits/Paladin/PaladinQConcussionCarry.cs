using System.Collections.Generic;

/// <summary>
/// 제압의 진격(PAL_3_Q_STUN, 할 일 126) — 팔라딘 Q 지속 밀치기 한 시퀀스 동안 뇌진탕을 매 틱 다시 싣는다(서버, 순수 계산).
/// 부여는 공용 밀치기 효과(<see cref="TraitKnockbackEffect"/>)가 '시전당 대상별 1회'로 첫 판정에만 한다. 여기서는 그 대상이
/// 끊김 없이(매 틱) 계속 맞는 동안 같은 값을 이어 실어, 수신측(<see cref="ConcussionState"/>)이 맞을 때마다 재설정하고
/// 마지막 밀치기가 끝난 뒤 한 번 기절시키게 한다 — 명세 '원래 Q 지속 밀치기는 전체 시퀀스를 한 번으로'.
/// Q 데이터는 넉백 시간 = 틱 간격(0.25초)이라 틱 사이에 넉백이 잠깐 끝날 수 있다. 이어 싣지 않으면 그때 기절이 소비되고
/// 다음 틱 밀치기의 경직이 그 기절을 덮어 결국 기절이 사라진다.
/// 한 틱이라도 빠진 대상(히트박스를 벗어남)은 그 시퀀스가 끝난 것이라 같은 시전에서 다시 싣지 않는다(시전당 대상별 1회).
/// </summary>
public sealed class PaladinQConcussionCarry
{
    private Dictionary<int, float> previous = new Dictionary<int, float>();
    private Dictionary<int, float> current = new Dictionary<int, float>();

    /// <summary>시전 시작·종료 — 이전 시전의 대상을 지운다.</summary>
    public void Reset()
    {
        previous.Clear();
        current.Clear();
    }

    /// <summary>
    /// 이번 틱 대상 1건. <paramref name="granted"/> = 특성 수정자가 이번 판정에 실은 뇌진탕(시전당 대상별 첫 판정만 &gt; 0).
    /// 반환 = 실을 뇌진탕(초). 0 = 없음.
    /// </summary>
    public float Resolve(int targetId, float granted)
    {
        float value = granted > 0f
            ? granted
            : previous.TryGetValue(targetId, out float carried) ? carried : 0f;
        if (value > 0f)
            current[targetId] = value;
        return value;
    }

    /// <summary>틱 끝 — 이번 틱에 실은 대상만 다음 틱으로 넘긴다.</summary>
    public void EndTick()
    {
        (previous, current) = (current, previous);
        current.Clear();
    }
}
