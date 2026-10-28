using System.Collections.Generic;

/// <summary>
/// 특성 1장의 효과 목록을 허브에 거는 순수 절차(할 일 125 3/5) — <see cref="TraitDirector"/> 와 EditMode 테스트가 같은 경로를 쓴다.
///  · '획득 1회' 동작(<see cref="TraitEffect.HasOnceAction"/>)은 장부 표시(플레이어·특성 해시·효과 칸)가 없을 때만 실행하고 표시한다 —
///    재선택으로 다시 고르거나 재합류로 다시 걸어도 다시 하지 않는다.
///  · 이벤트형 효과는 매번 새로 건다(구독·등록은 실행이 아니라 연결이다). 되돌리기 핸들은 <paramref name="bindings"/> 에 덧붙인다.
/// 장부의 적용 기록(<see cref="TraitEffectLedger{THandle}.Add"/>)은 호출자가 한다.
/// </summary>
public static class TraitEffectBinder
{
    /// <returns>이번에 실행한 '획득 1회' 동작 수.</returns>
    public static int Bind<THandle>(IReadOnlyList<TraitEffect> effects, ITraitEffectHost host, ulong clientId, int traitHash,
        TraitEffectLedger<THandle> ledger, List<ITraitEffectBinding> bindings)
    {
        if (effects == null || host == null)
            return 0;

        int onceRan = 0;
        for (int i = 0; i < effects.Count; i++)
        {
            TraitEffect effect = effects[i];
            if (effect == null)
                continue;

            if (effect.HasOnceAction && (ledger == null || ledger.MarkOnce(clientId, traitHash, i)))
            {
                effect.RunOnceServer(host, traitHash);
                onceRan++;
            }

            ITraitEffectBinding binding = effect.BindServer(host, traitHash);
            if (binding != null)
                bindings?.Add(binding);
        }

        return onceRan;
    }
}
