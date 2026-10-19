using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 플레이어·코어(상태이상·보호막·스킬 장부·어쌔신) EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
/// 테스트는 asmdef 없는 <c>Assembly-CSharp-Editor</c> 에 있어 픽스처 이름 정규식으로 고른다.
/// 패턴은 <see cref="DataTableEditModeTestRunner"/> 를 따랐다. 픽스처가 늘면 <see cref="Fixtures"/> 에만 추가한다.
/// </summary>
internal static class PlayerEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^StatusEffectImmunityTests$", // PLAN-assassin A1 — SuperArmor 의미 통일
        "^PlayerSkillCooldownLedgerTests$", // PLAN-assassin A2 — 스킬 단위 쿨 장부·슬롯 교체
        "^PlayerGroundPointProjectionTests$", // PLAN-assassin A4 — 고정 거리 조준·서버 재투영
        "^AssassinComboModelTests$", // PLAN-assassin A6 — 평타 순서·0.8초 유예·스킬 리셋
        "^AssassinComboWindowRulesTests$", // 할 일 47 연장 — 평타 다음 타 예약 창(정규화 시간·경계·단별 기본값)
        "^AssassinLungeModelTests$", // 할 일 19 — 일반 E 강타 전진 거리 분배
        "^AssassinStateModelTests$", // PLAN-assassin A7 — 분노 게이지(충전·상한·최소량·감소·보존)·변신 지속·해제 2초·종료 대기·쓰러짐 정리
        "^AssassinDashStrikeModelTests$", // PLAN-assassin A8 — Q 경로 대상 1회·쿨 차감 1회·보고 거리 상한
        "^AssassinCircleStrikeModelTests$", // PLAN-assassin A9 — 변신 E 원 판정·타당 1회·5타·무적 구간·종료 대기
        "^BackAttackRulesTests$", // PLAN-assassin A11 — 보스 후방 판정·변신 강제·배율
        "^HealthShieldTests$",
        "^GunnerHeatModelTests$",
        "^GunnerAnimEventAuthoringTests$", // PLAN-anim-event-timing P3 — 거너 클립 이벤트 저작: 상태 속도 환산·정규화·병합(사용자 위치 보존)
        "^GunnerSkillEventRulesTests$", // PLAN-anim-event-timing P3 — 거너 R 레이저 Custom0 생성 위치·대상(대상 사망 시 시전 위치·재탐색)
        "^GunnerAttackFacingModelTests$", // 할 일 27 — 원인별 회전 잠금 토큰·평타 발 단위 잠금·발 사이 제한 회전
        "^AttackTurnTimingTests$", // 할 일 43 — 근접 평타 다 돈 뒤 공격: 각 → 회전 지연·스냅 각·오너 지연 상한
        "^HealthVignetteModelTests$", // 로컬 체력 비네팅 — 시작 비율·맥동·피격 플래시 색·사망 어둡게(맥동·플래시 끔)
        "^ShieldVignetteModelTests$", // 로컬 실드 비네팅 — 표시 여부·고정 알파 페이드 인/아웃
        "^CombatHudLayerRulesTests$", // CombatHUD 상태 레이어 — 생명주기 상태별 표시 레이어
        "^InterruptHighlightModelTests$", // 인터럽트 강조(할 일 5) — 창 열림 × 사거리 → RMB 슬롯 발광·페이드·맥동
        "^InterruptOmniRangeModelTests$", // 인터럽트 강조 사거리 — 방향 무관 판정 형상 도달 거리 × 대상 콜라이더 표면 거리
        "^OverheadDashIconRulesTests$", // 할 일 29 — 머리 위 대시 아이콘 표시(로컬·생존)·준비 색, 에임 대시 틴트 토글(기본 꺼짐)
        "^OverheadStatusLabelRulesTests$", // 할 일 50 — 머리 위 이름 → 상태이상 이름: 붙잡힘·CC 6종 우선순위·버프 무시·사망/Soul 은 이름·BlockedCrowdControl 집합 일치
        "^InterruptSuccessCooldownModelTests$", // 할 일 26 — 간파 성공 쿨 감소(전체 쿨 × 비율·0 클램프·시전당 1회)
        "^PartyLivesRulesTests$", // 할 일 30 (1) — 파티 공용 목숨: 사망 목적지·공용 차감·재충전·공용 0 Soul 전멸 판정
        "^PartyWipeRespawnRulesTests$", // 할 일 30 (2) — 전멸 → 리스폰 분기(결과 화면 아님)·리스폰 조건·지점 선택(패드 제외·간격·폴백)·목숨 5
        "^PlayerHealthRegenModelTests$", // 할 일 31 — 기본 체력 재생: 지연·틱·상한·피격 리셋·누적기·사망 차단
        "^AimIndicatorPlacementTests$", // 할 일 33 — 에임 인디케이터 3D 쿼드: yaw·수평 회전·발밑 높이·전방 오프셋·스케일
        "^InteractPromptSlotTests$", // 상호작용 F 프롬프트 — 마지막 Show 우선·소유자만 Hide·다른 뷰로 Show 시 이전 뷰 숨김·누락·파괴 정리
        "^UnitWallImpactJudgeTests$", // 할 일 32 — 공용 벽 충돌: 스윕 결과 → 벽 충돌·제외 규칙(자기·배리어·유닛·상자·시작 겹침)·막힘 거리·에피소드당 1회
        // 튜토리얼 픽스처(특성·안내 UI·스피너봇 연습)는 TutorialEditModeTestRunner 로 옮겼다(할 일 15 I0).
        "^PlayerMotionSweepStepTests$", // 모터 스윕 — 단차·급경사·낙하 착지(실제 CapsuleCast)
        "^PlayerUnitCollisionRulesTests$", // 할 일 11 — 유닛 장애물 마스크(Soul·유닛 통과)·겹침 밀어내기 상한
        "^PlayerUnitPenetrationTests$", // 할 일 11 — 겹친 유닛 관통 수집(실제 ComputePenetration)·비몸체 Player 콜라이더 무시
        "^DashDirectionRulesTests$", // 할 일 25 — 대시 방향 옵션(캐릭터 정면 기본 / 마우스 방향) 선택 규칙
        "^PlayerInputBufferTests$", // 할 일 44 — 선입력 버퍼 기록·만료·덮어쓰기·거부 사유별 보관/버림
        "^PlayerInputBufferDiagnosticsTests$", // 할 일 44 후속 — 선입력 버퍼 진단: 즉시·버퍼 실행·만료·놓친 간격·중앙값/90%
        "^PlayerBasicAttackInputRulesTests$", // 할 일 44 5차 — 거너 홀드 시작·공격 중 스킬 입력은 지금 발 마무리 뒤 예약(다음 발 금지·만료 없음·마지막 1개·연타 1회)
        "^AssassinHairClipRulesTests$", // 할 일 47 — 머리카락 구동기: 클립 → 보정 종류(회피 v15·v16·v13), 클립 안 초, trailing 재설정(전환·루프·되감기·리셋)
        "^AssassinHairPrefabTests$", // 할 일 47 — Armature 머리카락 체인 7×4 본·캡처·몸 캡슐·구동기·보정 2종, Variant 중첩
        "^AssassinHairArmClearanceTests$", // 할 일 47 후속 — 팔 밀어내기 후처리: 겹침만 밀기·상한·회전만(길이 보존)·팔 캡슐 4개
        "^PlayerAnimationEventRequirementTests$", // PLAN-anim-event-timing P0-1 — 스킬·평타가 선언한 클립 이벤트가 플레이어 프리팹 컨트롤러·클립에 있는가
        "^AssassinVisualPrefabTests$", // PLAN-assassin §9 — v18 비주얼: Flat Kit 슬롯 5개·부모·BaseMap, 단검 루트 = 임포트 배율, Armature 루트·판정 박스 배율 1
    };

    const string Tag = "[PlayerTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/플레이어 EditMode 테스트 실행")]
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
