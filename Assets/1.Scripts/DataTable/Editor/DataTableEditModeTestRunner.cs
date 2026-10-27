using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 데이터 테이블(xlsx 리더·시트 해석·적용기) EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
/// 테스트는 asmdef 없는 <c>Assembly-CSharp-Editor</c> 에 있어 픽스처 이름 정규식으로 고른다.
/// 패턴은 <see cref="DamagePopupEditModeTestRunner"/> 를 따랐다.
/// </summary>
internal static class DataTableEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^XlsxReaderTests$",
        "^DataTableSchemaTests$",
        "^DataTableApplierTests$",
        "^DataSourceKeyTests$",
        "^XlsxWriterTests$",
        "^DataTableTemplateTests$",
        "^DataTableMergeTests$",
        "^DataTableSceneOverridesTests$",
        "^DataTableSaveGuardTests$",
        "^DataTableSheetGroupTests$",
        "^SkillTooltipFormatterTests$",
        "^SkillTooltipTagsTests$", // 할 일 114 — 툴팁 태그 칸 파싱·테이블 검증·이름 키
        "^SkillTooltipLayoutTests$", // 할 일 114 — 툴팁 슬롯 줄·태그 칩 줄바꿈
        "^StringTableValidatorTests$", // 로컬라이징 L1 — Strings 시트 검증
        "^StringsSheetTests$", // 로컬라이징 L1 — Strings 시트 읽기·Export
        "^ErrorCodesSheetTests$", // 로컬라이징 L1 — ErrorCodes 시트 읽기·검증·Export
        "^LocTests$", // 로컬라이징 L1 — Loc 조회·'#key' 폴백·Format·GetError
        "^UserLanguageConfigTests$", // 로컬라이징 L1 — 언어 기본값·저장
        "^LocKeyTests$", // 로컬라이징 L2 — 자동 키(주인ID/필드경로)
        "^DataTableTextKeysTests$", // 로컬라이징 L2 — [DataTableText] Strings 수집·제외
        "^CharacterRosterStringKeysTests$", // 로컬라이징 L2 — 캐릭터 이름 키
        "^LocTextRuleTests$", // 로컬라이징 L2 — 시트 검증·툴팁 포매터 공용 문법·자리표시자 규칙
        "^TableTextMigrationTests$", // 툴팁 문구 단일 원본 — 옛 수치 시트 문구 칸 → Strings 이관
        "^StringOwnerFieldTests$", // 툴팁 자동 키 행 ko·en {필드} 주인 검사
        "^UiStringsTests$", // 로컬라이징 L3 — UI 문구 키 목록·Export 등록
        "^UiStringsFormatTests$", // 로컬라이징 L3 — 과열 단계·변신·머리 위 이름 포맷·LocalizedText 폴백
        "^UiStringsAuthoringTests$", // 로컬라이징 L3 — 라벨 키 부착 멱등
        "^ErrorCatalogTests$", // 로컬라이징 L4 — 오류 코드 목록·ErrorCodes Export·시트 전용 행 경고
        "^ErrorDisplayTests$", // 로컬라이징 L4 — 오류 표시 형식·인자 치환·SessionError
        "^UiStringsL4bTests$", // 로컬라이징 L4b — 상태이상 enum→키 전수·로딩 칸 순서·초 단위 한 곳
        "^UiStringsL4bFormatTests$", // 로컬라이징 L4b — 로딩·툴팁·상태이상·결과·허수아비 포맷·폴백
    };

    const string Tag = "[DataTableTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/데이터 테이블 EditMode 테스트 실행")]
    static void Run()
    {
        if (_runner != null)
        {
            Debug.LogWarning($"{Tag} 이미 실행 중이다 — 끝날 때까지 기다릴 것.");
            return;
        }

        _runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        _runner.RegisterCallbacks(new Callbacks());
        _runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, groupNames = Fixtures }));
    }

    sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            string summary =
                $"{Tag} {result.TestStatus} — 통과 {result.PassCount} / 실패 {result.FailCount} / " +
                $"건너뜀 {result.SkipCount} ({result.Duration:F3}s)";

            // 통과 0건은 "실패 0건"과 다르다 — 필터가 아무것도 못 잡아도 TestStatus 는 Passed 가 된다.
            if (result.FailCount > 0 || result.PassCount == 0)
                Debug.LogError(summary + (result.PassCount == 0 ? "  ⚠️ 통과 0건 — 필터가 안 걸렸는지 확인" : ""));
            else
                Debug.Log(summary);

            Object.DestroyImmediate(_runner);
            _runner = null;
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus != TestStatus.Failed) return;

            Debug.LogError($"{Tag} FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
        }
    }
}
