using System;
using System.Collections.Generic;

/// <summary>
/// 카드 설명의 <c>{dmg}</c> 계산식(할 일 125 5/5) — 특성별로 캐릭터 레인이 등록한다(명세 §8 — 돌파의 방패 = 교체 Q 1회 등).
/// <paramref name="player"/> = 카드를 보는 로컬 플레이어(null 일 수 있다 — 그때 <c>default</c>(값 없음)를 돌려주면 화면은 <see cref="TraitUiStrings.DamageUnknown"/>).
/// 집중 포화·적 방어·실시간 백어택은 넣지 않는다(대상 미지정 수치).
/// </summary>
public delegate SkillTooltipDamage TraitDamageCalculator(TraitDefinition trait, Player player);

/// <summary>
/// 특성 콘텐츠 등록부(할 일 125 5/5) — 카드 툴팁 <c>{dmg}</c> 계산식·툴팁 값 별칭·2차 상충 규칙을 특성 ID 로 모은다. 순수 C#(EditMode 테스트 대상).
/// <para>
/// 🔴 캐릭터 레인(126 팔라딘·127 거너·128 어쌔신)은 이 파일을 고치지 않는다 — 자기 폴더의 partial 파일
/// (<c>Traits/Paladin/TraitContent.Paladin.cs</c> 등)의 <c>RegisterPaladin</c>·<c>RegisterGunner</c>·<c>RegisterAssassin</c> 본문만 채운다.
/// 등록은 캐릭터별 <see cref="Registrar"/> 로 하고, 그 캐릭터의 ID(<see cref="TraitCatalog"/>)가 아니면 <see cref="Errors"/> 에 남는다.
/// </para>
/// </summary>
public sealed class TraitContentRegistry
{
    private readonly Dictionary<string, TraitDamageCalculator> damage = new Dictionary<string, TraitDamageCalculator>(StringComparer.Ordinal);
    private readonly Dictionary<(string id, string name), Func<TraitDefinition, object>> values =
        new Dictionary<(string, string), Func<TraitDefinition, object>>();
    private readonly Dictionary<string, List<string>> disables = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> conflictTexts = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<string> errors = new List<string>();

    /// <summary>등록 오류(다른 캐릭터 ID·모르는 ID·같은 등록 두 번·빈 계산식). 비었으면 통과.</summary>
    public IReadOnlyList<string> Errors => errors;

    /// <summary>캐릭터 하나의 등록 창구. 등록은 그 캐릭터 ID 만 받는다.</summary>
    public Registrar For(TutorialCharacter character) => new Registrar(this, character);

    /// <summary>{dmg} 계산식이 등록된 특성 ID 전부.</summary>
    public IEnumerable<string> DamageIds => damage.Keys;

    // ── 조회 ─────────────────────────────────────────────────────────────

    public bool TryGetDamage(string traitId, out TraitDamageCalculator calculator) =>
        damage.TryGetValue(traitId ?? string.Empty, out calculator);

    /// <summary>툴팁 별칭 값(<see cref="Registrar.Value"/>). 없으면 false.</summary>
    public bool TryGetValue(string traitId, string name, out Func<TraitDefinition, object> getter) =>
        values.TryGetValue((traitId ?? string.Empty, name ?? string.Empty), out getter);

    /// <summary>
    /// <paramref name="chosenId"/> 를 고르면 효과를 잃는 보유 특성 ID(보유 순서). 결과는 덮어쓴다.
    /// 같은 특성을 두 번 갖지 않으므로 중복은 없다.
    /// </summary>
    public void GetDisabledBy(string chosenId, IReadOnlyList<string> ownedIds, List<string> result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        result.Clear();
        if (chosenId == null || ownedIds == null || !disables.TryGetValue(chosenId, out List<string> targets))
            return;

        for (int i = 0; i < ownedIds.Count; i++)
        {
            if (ownedIds[i] != null && targets.Contains(ownedIds[i]) && !result.Contains(ownedIds[i]))
                result.Add(ownedIds[i]);
        }
    }

    /// <summary>상충 확인창 문구 ko 폴백(<see cref="Registrar.Conflict"/> 에 준 것). 없으면 false — 화면은 <see cref="TraitUiStrings.ConflictDefault"/>.</summary>
    public bool TryGetConflictText(string chosenId, out string ko) => conflictTexts.TryGetValue(chosenId ?? string.Empty, out ko);

    /// <summary>상충 규칙이 있는 특성 ID 전부(Export 키 수집용).</summary>
    public IEnumerable<string> ConflictSources => disables.Keys;

    // ── 등록 ─────────────────────────────────────────────────────────────

    /// <summary>캐릭터 하나의 등록 창구 — 캐릭터 레인 partial 파일이 받는다.</summary>
    public sealed class Registrar
    {
        private readonly TraitContentRegistry owner;

        internal Registrar(TraitContentRegistry owner, TutorialCharacter character)
        {
            this.owner = owner;
            Character = character;
        }

        public TutorialCharacter Character { get; }

        /// <summary>카드 설명 <c>{dmg}</c> 계산식. 설명에 <c>{dmg}</c> 가 있는데 계산식이 없으면 검증 오류다.</summary>
        public Registrar Damage(string traitId, TraitDamageCalculator calculator)
        {
            if (!Check(traitId, "{dmg} 계산식"))
                return this;
            if (calculator == null)
            {
                Fail($"'{traitId}' {{dmg}} 계산식이 null 이다.");
                return this;
            }

            if (!owner.damage.ContainsKey(traitId))
                owner.damage.Add(traitId, calculator);
            else
                Fail($"'{traitId}' {{dmg}} 계산식을 두 번 등록했다.");
            return this;
        }

        /// <summary>
        /// 툴팁 별칭 — 설명의 <c>{name}</c> 을 계산해서 낸다(명세 §8 — 다른 SO 값·표시용 파생 값 <c>totalDamageRatio</c> 등).
        /// 정의·효과 필드 이름보다 먼저 찾는다.
        /// </summary>
        public Registrar Value(string traitId, string name, Func<TraitDefinition, object> getter)
        {
            if (!Check(traitId, $"별칭 '{name}'"))
                return this;
            if (string.IsNullOrEmpty(name) || getter == null)
            {
                Fail($"'{traitId}' 별칭 이름·계산이 비었다.");
                return this;
            }

            if (!owner.values.ContainsKey((traitId, name)))
                owner.values.Add((traitId, name), getter);
            else
                Fail($"'{traitId}' 별칭 '{name}' 을 두 번 등록했다.");
            return this;
        }

        /// <summary>
        /// 2차 상충 — <paramref name="chosenId"/> 를 고르면 보유한 <paramref name="disabledId"/> 의 효과가 사라진다(명세 §2 — 확인 후 그대로 허용, 환불·교체 없음).
        /// <paramref name="ko"/> = 확인창 문구 ko 폴백(<c>{traits}</c> = 보유 특성 이름, Strings 키 <c>Trait.&lt;chosenId&gt;.Conflict</c>). null 이면 기본 문구.
        /// 효과를 잃는 쪽은 다른 캐릭터 특성이어도 등록할 수 없다(같은 캐릭터 안에서만 상충).
        /// </summary>
        public Registrar Conflict(string chosenId, string disabledId, string ko = null)
        {
            if (!Check(chosenId, "상충 규칙") || !Check(disabledId, "상충 대상"))
                return this;
            if (string.Equals(chosenId, disabledId, StringComparison.Ordinal))
            {
                Fail($"'{chosenId}' 가 자기 자신과 상충한다.");
                return this;
            }

            if (!owner.disables.TryGetValue(chosenId, out List<string> list))
                owner.disables.Add(chosenId, list = new List<string>());
            if (!list.Contains(disabledId))
                list.Add(disabledId);

            if (!string.IsNullOrEmpty(ko))
            {
                if (owner.conflictTexts.TryGetValue(chosenId, out string previous) && !string.Equals(previous, ko, StringComparison.Ordinal))
                    Fail($"'{chosenId}' 상충 문구를 서로 다르게 두 번 등록했다.");
                else
                    owner.conflictTexts[chosenId] = ko;
            }

            return this;
        }

        private bool Check(string traitId, string what)
        {
            if (!TraitCatalog.TryGetSlot(traitId, out TraitCatalog.Slot slot))
            {
                Fail($"{what}: '{traitId}' 는 명세 고정표(TraitCatalog)에 없는 ID 다.");
                return false;
            }

            if (slot.Character != Character)
            {
                Fail($"{what}: '{traitId}' 는 {slot.Character} 특성이다 — 그 캐릭터 파일에서 등록할 것.");
                return false;
            }

            return true;
        }

        // 오류는 등록한 캐릭터 이름으로 시작한다 — 캐릭터 레인 테스트가 자기 오류만 골라 본다.
        private void Fail(string message) => owner.errors.Add($"{Character} {message}");
    }
}

/// <summary>
/// 프로젝트 특성 콘텐츠 등록부 하나(<see cref="Registry"/>) — 처음 읽을 때 캐릭터별 partial 등록 함수 세 개를 부른다.
/// 각 본문은 캐릭터 폴더 파일(<c>Traits/&lt;캐릭터&gt;/TraitContent.&lt;캐릭터&gt;.cs</c>)에 있다. 이 파일은 고치지 않는다.
/// </summary>
public static partial class TraitContent
{
    private static TraitContentRegistry registry;

    public static TraitContentRegistry Registry => registry ??= Build();

    /// <summary>등록부를 새로 만든다(테스트·도메인 리로드 없는 갱신용). 같은 등록 함수를 다시 부른다.</summary>
    public static TraitContentRegistry Build()
    {
        var result = new TraitContentRegistry();
        RegisterPaladin(result.For(TutorialCharacter.Paladin));
        RegisterGunner(result.For(TutorialCharacter.Gunner));
        RegisterAssassin(result.For(TutorialCharacter.Assassin));
        return result;
    }

    static partial void RegisterPaladin(TraitContentRegistry.Registrar r);
    static partial void RegisterGunner(TraitContentRegistry.Registrar r);
    static partial void RegisterAssassin(TraitContentRegistry.Registrar r);
}
