using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 프로젝트의 EffectEntry를 한 곳에 모아두는 데이터 허브. <see cref="SoundCatalog"/>와 같은 명명 프로퍼티 방식.
///
/// 두 가지 일을 한다:
/// ① 인스펙터가 없는 코드 경로의 룩업 (EffectManager.Instance.Catalog.HitSpark)
/// ② <b>빌드 포함 보장</b> — 어떤 씬/프리팹에서도 참조되지 않는 SO는 빌드에서 조용히 빠진다.
///
/// 엔트리를 늘릴 때는 아래에 프로퍼티 한 줄만 추가하면 된다.
/// (<see cref="All"/>는 리플렉션으로 프로퍼티를 훑으므로 목록을 두 곳에 적을 필요가 없다.)
/// </summary>
[CreateAssetMenu(fileName = "EffectCatalog", menuName = "Effects/Effect Catalog")]
public class EffectCatalog : ScriptableObject
{
    [Header("Common — 피격")]
    [field: SerializeField] public EffectEntry HitEffect1 { get; private set; }
    [field: SerializeField] public EffectEntry HitEffect2 { get; private set; }
    [field: SerializeField] public EffectEntry HitEffect3 { get; private set; }
    [field: SerializeField] public EffectEntry HitEffect4 { get; private set; }
    [field: SerializeField] public EffectEntry HitEffect5 { get; private set; }



    [Header("Boss")]
    [field: SerializeField] public EffectEntry Drop_Charge_Boundary { get; private set; }
    [field: SerializeField] public EffectEntry Drop_Charge_Indicator { get; private set; }
    [field: SerializeField] public EffectEntry Drop_Collision { get; private set; }
    [field: SerializeField] public EffectEntry BossRage { get; private set; }
    [field: SerializeField] public EffectEntry Grab_Lightning { get; private set; }
    [field: SerializeField] public EffectEntry Grab_ArmElectric { get; private set; }
    [field: SerializeField] public EffectEntry Grabbed_Electric { get; private set; }
    [field: SerializeField] public EffectEntry Throw_Lightning { get; private set; }

    private List<EffectEntry> _all;
    // 피격 이펙트 테스트용 enum
    public enum HitVFXType
    {
        HitEffect1,
        HitEffect2,
        HitEffect3,
        HitEffect4,
        HitEffect5
    }

    public EffectEntry GetHitEffect(HitVFXType hitVFX)
    {
        EffectEntry effectEntry = HitEffect1;
        switch (hitVFX)
        {
            case HitVFXType.HitEffect1:
                break;
            case HitVFXType.HitEffect2:
                effectEntry = HitEffect2;
                break;
            case HitVFXType.HitEffect3:
                effectEntry = HitEffect3;
                break;
            case HitVFXType.HitEffect4:
                effectEntry = HitEffect4;
                break;
            case HitVFXType.HitEffect5:
                effectEntry = HitEffect5;
                break;
        }
        return effectEntry;
    }

    /// <summary>
    /// 이 카탈로그가 들고 있는 모든 엔트리(중복·null 제거). 프리워밍이 이 목록을 쓴다.
    /// 최초 접근 시 한 번만 리플렉션으로 만든다.
    /// </summary>
    public IReadOnlyList<EffectEntry> All
    {
        get
        {
            // 빈 결과는 캐시하지 않는다. 에디터는 도메인 리로드 설정에 따라 이 인스턴스의 관리 상태를
            // 살려두기도 하는데, 그때 "비어 있던 시점의 캐시"가 눌러앉으면 프리워밍이 조용히 통째로 빠진다.
            if (_all == null || _all.Count == 0) Rebuild();
            return _all;
        }
    }

    private void Rebuild()
    {
        _all = new List<EffectEntry>();

        PropertyInfo[] properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < properties.Length; i++)
        {
            if (properties[i].PropertyType != typeof(EffectEntry)) continue;

            var entry = properties[i].GetValue(this) as EffectEntry;
            if (entry != null && !_all.Contains(entry)) _all.Add(entry);
        }
    }
}
