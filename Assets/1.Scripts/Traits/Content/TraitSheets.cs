/// <summary>
/// 특성 수치 데이터 테이블 시트 이름(할 일 125 5/5) — 캐릭터마다 한 장, 세로 표(특성 = 열 = 정의 에셋 파일 이름, 필드 = 행).
/// 행 이름 = 정의의 직렬화 경로(<c>effects[0].damageIncrease</c> 처럼 효과 칸 번호 + 효과 필드). 비율 0.20 = 20%, 시간 초, 거리 m.
/// 시트는 캐릭터별 정의 하위 타입이 <c>[DataTableSheet(TraitSheets.X)]</c> 로 가리킨다(Traits/Paladin·Gunner·Assassin 폴더).
/// xlsx 에 시트가 아직 없으면 아무 값도 적용하지 않는다(오류·경고 없음 — 인스펙터 값으로 돈다). 시트 추가는 병합 때 메인이 Export.
/// </summary>
public static class TraitSheets
{
    public const string Paladin = "Traits_Paladin";
    public const string Gunner = "Traits_Gunner";
    public const string Assassin = "Traits_Assassin";

    /// <summary>세 장 전부(캐릭터 순).</summary>
    public static readonly string[] All = { Paladin, Gunner, Assassin };

    /// <summary>캐릭터의 시트 이름. 모르는 캐릭터면 null.</summary>
    public static string For(TutorialCharacter character)
    {
        switch (character)
        {
            case TutorialCharacter.Paladin: return Paladin;
            case TutorialCharacter.Gunner: return Gunner;
            case TutorialCharacter.Assassin: return Assassin;
            default: return null;
        }
    }
}
