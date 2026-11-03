using System.Collections.Generic;

/// <summary>
/// 제압 가속이 켜져 있는가 — 연출 쪽(피해 숫자 합산·3타 줌인·보스 체력바 흰 칸 멈춤, 할 일 174)이 읽는 한 곳.
/// 기준 = 대표 상태이상(<see cref="SuppressHasteRules.HudRepresentative"/>) 보유 — 비네팅(<c>SuppressHasteVignetteHUD</c>)과 같다.
/// 상태이상 목록은 NetworkList 라 전 피어에서 같은 답이 나온다(새 RPC·NetworkVariable 없음). 판정에는 쓰지 않는다.
/// </summary>
public static class SuppressHastePresence
{
    /// <summary>이 유닛(플레이어)에게 제압 가속이 걸려 있는가.</summary>
    public static bool IsActive(Unit unit)
    {
        StatusEffectController effects = unit != null ? unit.StatusEffects : null;
        return effects != null && effects.Has(SuppressHasteRules.HudRepresentative);
    }

    /// <summary>그 clientId 의 플레이어에게 제압 가속이 걸려 있는가. 플레이어가 아닌 공격자(ulong.MaxValue)는 false.</summary>
    public static bool IsActiveForClient(ulong clientId)
    {
        if (clientId == ulong.MaxValue)
            return false;

        IReadOnlyList<PlayerLifeCycleController> lives = PlayerLifeCycleController.Active;
        for (int i = 0; i < lives.Count; i++)
        {
            PlayerLifeCycleController life = lives[i];
            if (life != null && life.IsSpawned && life.OwnerClientId == clientId)
                return IsActive(life.GetComponent<Unit>());
        }

        return false;
    }

    /// <summary>
    /// 그 출처(보스 NetworkObjectId)가 건 제압 가속이 플레이어 누구에게라도 남아 있는가 — 보스 쪽 "제압 중" 표시용.
    /// 시트 durationOverride 가 0 이면 버프 길이 = 제압 길이라 제압 구간과 같다.
    /// </summary>
    public static bool IsActiveFromSource(ulong sourceId)
    {
        IReadOnlyList<PlayerLifeCycleController> lives = PlayerLifeCycleController.Active;
        for (int i = 0; i < lives.Count; i++)
        {
            Unit unit = lives[i] != null ? lives[i].GetComponent<Unit>() : null;
            StatusEffectController effects = unit != null ? unit.StatusEffects : null;
            if (effects == null)
                continue;

            for (int e = 0; e < effects.ActiveCount; e++)
            {
                StatusEffectInstance effect = effects.GetActive(e);
                if (effect.type == SuppressHasteRules.HudRepresentative && effect.sourceId == sourceId)
                    return true;
            }
        }

        return false;
    }
}
