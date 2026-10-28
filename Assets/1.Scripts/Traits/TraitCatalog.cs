using System;
using System.Collections.Generic;

/// <summary>접속 때 서버·클라 특성 목록 버전 비교 결과.</summary>
public enum TraitVersionCheck
{
    /// <summary>서버 값을 아직 못 받았다(0).</summary>
    Unknown,
    Match,
    Mismatch,
}

/// <summary>
/// 특성 후보 고정표(할 일 125) — 명세(Re_C_특성_개발명세_20261016 §2)의 캐릭터·차수(1~4)별 3개를 그 순서대로 둔다.
/// 랜덤 없음: 제시 = 이 표의 (캐릭터, 차수) 3개 중 풀에 정의가 등록된 것, 자동 선택 = 그중 첫 번째.
/// 캐릭터는 기존 판별(<see cref="TutorialCharacter"/>, <see cref="TutorialLocalPlayerInfo.ResolveCharacter"/>)을 쓴다.
/// 정의 에셋(36개)은 캐릭터 레인이 풀(<see cref="TraitPool"/>)에 넣는다 — 표에 있지만 정의가 없는 ID 는 제시하지 않는다.
/// </summary>
public static class TraitCatalog
{
    public const int TierCount = 4;
    public const int CandidatesPerTier = 3;

    /// <summary>표 한 줄 — 캐릭터·차수·영구 ID.</summary>
    public readonly struct Slot
    {
        public readonly TutorialCharacter Character;
        public readonly int Tier;
        public readonly string Id;

        public Slot(TutorialCharacter character, int tier, string id)
        {
            Character = character;
            Tier = tier;
            Id = id;
        }
    }

    private static readonly Slot[] SpecSlots =
    {
        // 팔라딘
        new Slot(TutorialCharacter.Paladin, 1, "PAL_1_Q_DAMAGE"),
        new Slot(TutorialCharacter.Paladin, 1, "PAL_1_E_COOLDOWN"),
        new Slot(TutorialCharacter.Paladin, 1, "PAL_1_PASSIVE_HEAL"),
        new Slot(TutorialCharacter.Paladin, 2, "PAL_2_DASH"),
        new Slot(TutorialCharacter.Paladin, 2, "PAL_2_E_REFUND"),
        new Slot(TutorialCharacter.Paladin, 2, "PAL_2_SHARE_HEAL"),
        new Slot(TutorialCharacter.Paladin, 3, "PAL_3_Q_STUN"),
        new Slot(TutorialCharacter.Paladin, 3, "PAL_3_E_SHIELD"),
        new Slot(TutorialCharacter.Paladin, 3, "PAL_3_PASSIVE_CDR"),
        new Slot(TutorialCharacter.Paladin, 4, "PAL_4_E_PUSH"),
        new Slot(TutorialCharacter.Paladin, 4, "PAL_4_Q_BLAST"),
        new Slot(TutorialCharacter.Paladin, 4, "PAL_4_HEAL_SHIELD"),
        // 거너
        new Slot(TutorialCharacter.Gunner, 1, "GUN_1_BASIC_DAMAGE"),
        new Slot(TutorialCharacter.Gunner, 1, "GUN_1_CHARGE"),
        new Slot(TutorialCharacter.Gunner, 1, "GUN_1_WIDTH"),
        new Slot(TutorialCharacter.Gunner, 2, "GUN_2_OVERDRIVE"),
        new Slot(TutorialCharacter.Gunner, 2, "GUN_2_HEAT_SHOT"),
        new Slot(TutorialCharacter.Gunner, 2, "GUN_2_COOL_MODE"),
        new Slot(TutorialCharacter.Gunner, 3, "GUN_3_Q_PUSH"),
        new Slot(TutorialCharacter.Gunner, 3, "GUN_3_PIERCE"),
        new Slot(TutorialCharacter.Gunner, 3, "GUN_3_VOLLEY"),
        new Slot(TutorialCharacter.Gunner, 4, "GUN_4_R_FOCUS"),
        new Slot(TutorialCharacter.Gunner, 4, "GUN_4_EXTRA_BASIC"),
        new Slot(TutorialCharacter.Gunner, 4, "GUN_4_ECHO_Q"),
        // 어쌔신
        new Slot(TutorialCharacter.Assassin, 1, "ASN_1_TRANS_E_DAMAGE"),
        new Slot(TutorialCharacter.Assassin, 1, "ASN_1_NORMAL_E_DAMAGE"),
        new Slot(TutorialCharacter.Assassin, 1, "ASN_1_Q_COOLDOWN"),
        new Slot(TutorialCharacter.Assassin, 2, "ASN_2_RELEASE"),
        new Slot(TutorialCharacter.Assassin, 2, "ASN_2_RESTRAINT"),
        new Slot(TutorialCharacter.Assassin, 2, "ASN_2_RETURN"),
        new Slot(TutorialCharacter.Assassin, 3, "ASN_3_E_AREA"),
        new Slot(TutorialCharacter.Assassin, 3, "ASN_3_Q_BASIC"),
        new Slot(TutorialCharacter.Assassin, 3, "ASN_3_BASIC_CDR"),
        new Slot(TutorialCharacter.Assassin, 4, "ASN_4_NORMAL_E_PUSH"),
        new Slot(TutorialCharacter.Assassin, 4, "ASN_4_Q_TRAIL"),
        new Slot(TutorialCharacter.Assassin, 4, "ASN_4_COMBO_EXTRA"),
    };

    /// <summary>명세 고정표 전체(캐릭터 → 차수 → 표 순서).</summary>
    public static IReadOnlyList<Slot> Spec => SpecSlots;

    /// <summary>
    /// 서버가 복제한 목록 버전과 내 버전 비교. 서버 값 0 = 아직 복제 전(<see cref="TraitVersionCheck.Unknown"/>) —
    /// 실제 버전이 우연히 0 이면 검사가 건너뛰어진다(2^-32 확률, 감수).
    /// </summary>
    public static TraitVersionCheck CompareVersion(int serverVersion, int localVersion)
    {
        if (serverVersion == 0)
            return TraitVersionCheck.Unknown;

        return serverVersion == localVersion ? TraitVersionCheck.Match : TraitVersionCheck.Mismatch;
    }

    /// <summary>그 ID 가 표에 있으면 캐릭터·차수.</summary>
    public static bool TryGetSlot(string id, out Slot slot)
    {
        for (int i = 0; i < SpecSlots.Length; i++)
        {
            if (string.Equals(SpecSlots[i].Id, id, StringComparison.Ordinal))
            {
                slot = SpecSlots[i];
                return true;
            }
        }

        slot = default;
        return false;
    }
}

/// <summary>
/// 풀에 등록된 정의 ID 목록(풀 순서)으로 만든 조회표 — 해시 ↔ 풀 칸, 고정 후보, 목록 버전, 검증. Unity 비의존, EditMode 테스트 대상.
/// 같은 ID 목록이면 모든 피어에서 같은 결과를 낸다. 검증 오류가 있는 칸(빈 ID·중복·해시 충돌·표에 없는 ID)은 제시하지 않는다.
/// </summary>
public sealed class TraitCatalogIndex
{
    private readonly Dictionary<int, int> slotByHash = new Dictionary<int, int>();
    private readonly HashSet<string> registeredIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> errors = new List<string>();

    /// <param name="ids">풀 순서의 정의 ID. 빈 칸(null 정의)은 null 로 넣는다 — 칸 번호를 유지하기 위해서다.</param>
    public TraitCatalogIndex(IReadOnlyList<string> ids)
    {
        var hashes = new List<int>();
        var idByHash = new Dictionary<int, string>();
        int count = ids != null ? ids.Count : 0;
        for (int slot = 0; slot < count; slot++)
        {
            string id = ids[slot];
            if (id == null)
                continue; // 빈 칸 — 정의가 없다(오류는 풀 쪽에서 알린다)

            if (id.Length == 0)
            {
                errors.Add($"풀 {slot}번 정의의 ID 가 비었다.");
                continue;
            }

            int hash = TraitIdHash.Of(id);
            hashes.Add(hash);
            if (hash == TraitIdHash.None)
            {
                errors.Add($"'{id}' 의 해시가 0(예약값)이다 — ID 를 바꿔야 한다.");
                continue;
            }

            if (idByHash.TryGetValue(hash, out string other))
            {
                errors.Add(string.Equals(other, id, StringComparison.Ordinal)
                    ? $"'{id}' 가 풀에 두 번 들어 있다(풀 {slot}번)."
                    : $"해시 충돌: '{other}' 와 '{id}' 가 같은 해시 {hash} — ID 를 바꿔야 한다.");
                continue;
            }

            idByHash.Add(hash, id);
            if (!TraitCatalog.TryGetSlot(id, out _))
            {
                errors.Add($"'{id}' 는 명세 고정표(TraitCatalog)에 없는 ID 다 — 제시하지 않는다.");
                continue;
            }

            slotByHash.Add(hash, slot);
            registeredIds.Add(id);
        }

        Version = TraitIdHash.Version(hashes);
    }

    /// <summary>특성 목록 버전 — 풀 순서의 정의 ID 해시 목록에서 계산(<see cref="TraitIdHash.Version"/>).</summary>
    public int Version { get; }

    /// <summary>검증 오류(빈 ID·0 해시·중복·충돌·표에 없는 ID). 비었으면 통과.</summary>
    public IReadOnlyList<string> Errors => errors;

    /// <summary>제시 가능한(검증 통과한) 정의 수.</summary>
    public int Count => slotByHash.Count;

    /// <summary>제시 가능한 특성 해시인가 — 선택·재합류 복원 검증에 쓴다.</summary>
    public bool Contains(int traitHash) => slotByHash.ContainsKey(traitHash);

    /// <summary>해시의 풀 칸. 없으면 false.</summary>
    public bool TryGetPoolSlot(int traitHash, out int poolSlot) => slotByHash.TryGetValue(traitHash, out poolSlot);

    /// <summary>
    /// (캐릭터, 차수) 고정 후보의 해시를 표 순서대로 <paramref name="result"/> 에 덮어쓴다 — 정의가 등록된 것만.
    /// 캐릭터 미상·차수 범위 밖·등록 0개면 비운다(호출자는 0장 제시 = 기존 Empty 흐름).
    /// </summary>
    public void GetCandidates(TutorialCharacter character, int tier, List<int> result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        result.Clear();
        if (character == TutorialCharacter.None || tier < 1 || tier > TraitCatalog.TierCount)
            return;

        IReadOnlyList<TraitCatalog.Slot> spec = TraitCatalog.Spec;
        for (int i = 0; i < spec.Count; i++)
        {
            if (spec[i].Character == character && spec[i].Tier == tier && registeredIds.Contains(spec[i].Id))
                result.Add(TraitIdHash.Of(spec[i].Id));
        }
    }
}
