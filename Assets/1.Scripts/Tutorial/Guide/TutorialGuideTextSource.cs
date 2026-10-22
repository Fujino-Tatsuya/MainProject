/// <summary>
/// 튜토리얼 문구 출처 = SO(<see cref="TutorialGuideTextTable"/>) + Strings 시트(<see cref="Loc"/>) — 할 일 86 T6a.
/// <list type="bullet">
/// <item>문구: Strings 일반 키 <c>Tutorial.Guide.&lt;id&gt;</c>(<see cref="KeyOf"/>) 가 있으면 그 문구(현재 언어), 없으면 SO 문구(폴백 — <c>#키</c> 를 내지 않는다).</item>
/// <item>지속시간·효과음: SO 행 것. SO 에 없고 Strings 에만 있는 id 는 기본 지속시간(0 = 큐 기본값)·효과음 없음.
///       단 캐릭터 변형(<c>id_GUNNER</c> 등)이 Strings 에만 있으면 SO 기본 id 행의 지속시간·효과음을 물려받는다.</item>
/// </list>
/// 변형 선택(<see cref="TutorialGuideText.SelectVariantId"/>)은 이 출처 하나를 보므로 변형 행이 SO·Strings 어느 쪽에 있어도 된다.
/// 🔴 툴팁 자동 키(<c>주인ID/필드경로</c>)가 아니라 일반 키다 — 검증기가 <c>{Q}</c>·<c>{mm:ss}</c> 를 필드로 검사하지 않는다.
/// </summary>
public sealed class TutorialGuideTextSource : ITutorialGuideTextSource
{
    public const string KeyPrefix = "Tutorial.Guide.";

    private static readonly TutorialCharacter[] VariantCharacters =
        { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin };

    private readonly ITutorialGuideTextSource fallback;

    /// <param name="fallback">SO 테이블(없으면 null — Strings 만 본다).</param>
    public TutorialGuideTextSource(ITutorialGuideTextSource fallback)
    {
        this.fallback = fallback;
    }

    public static string KeyOf(string id) => KeyPrefix + id;

    public bool TryGetRow(string id, out TutorialGuideTextRow row)
    {
        row = null;
        if (string.IsNullOrEmpty(id))
            return false;

        TutorialGuideTextRow soRow = null;
        bool hasSoRow = fallback != null && fallback.TryGetRow(id, out soRow);

        string sheetText = Loc.Get(KeyOf(id), null);
        if (sheetText == null)
        {
            row = soRow;
            return hasSoRow;
        }

        TutorialGuideTextRow timing = hasSoRow ? soRow : BaseRowOfVariant(id);
        row = timing != null
            ? new TutorialGuideTextRow(id, sheetText, timing.DurationSeconds, timing.Sfx)
            : new TutorialGuideTextRow(id, sheetText);
        return true;
    }

    // Strings 에만 있는 변형 행 — SO 기본 id 행의 지속시간·효과음.
    private TutorialGuideTextRow BaseRowOfVariant(string id)
    {
        if (fallback == null)
            return null;

        foreach (TutorialCharacter character in VariantCharacters)
        {
            string suffix = TutorialGuideText.Suffix(character);
            if (id.Length > suffix.Length && id.EndsWith(suffix, System.StringComparison.Ordinal)
                && fallback.TryGetRow(id.Substring(0, id.Length - suffix.Length), out TutorialGuideTextRow baseRow))
                return baseRow;
        }

        return null;
    }
}
