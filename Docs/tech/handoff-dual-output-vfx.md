# 핸드오프 — 이중 출력(GUN_4_EXTRA_BASIC) 추가 레이저 VFX

> 2026-11-03 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).
> 판정·피해·카운터 무변경. 이미 나가던 특성 연출 사건을 받아 재생 + 사건 Variant 에 한계 돌파 단계.

## 기획 vs 구현(확인 결과)
로직은 기획과 일치(원래 발 발사 커밋만 셈·세 번째 발 피해 × 0.4·0.05초·공격 속도 반영·사망 취소/카운터 초기화·같은 방향·사거리·폭·관통·과열/패시브 없음). **연출만 없었다** → 이번 작업.

## 동작
- **기존 평타 연출 + 이중 출력 연출 동시**(11-03 민경 요청): Activated 에서 추가 발의 **기본 공격 빔을 따로** 낸다(`FireExtraBasicBeam` — 평타 빔 엔트리를 소켓 없이 원샷, 발사 순간 원점·방향 고정, 길이·날아감·착탄은 평타와 같은 규칙, 같은 단계 색). 🔴 처음엔 `ShowBasicShot`(평타 빔 소켓 = 루프 하나)을 다시 불렀는데 0.05초 전 **세 번째 발 빔이 한 프레임 만에 지워졌다**(Play 로그로 확인) → 따로 내도록 고침. 몬스터 적중은 Hit 에서 평타 `monsterHitPlayer` + 이중 출력 Hit.
- 나선 반지름 0.08m · 가닥 폭 0.075m(처음 0.11m·0.06m — 11-03 민경 조정, 0.30m 는 너무 커서 되돌림). 메시 `SM_DualOutput_StrandA/B` 직접 수정 + 빌더 상수 같은 값.
- 서버 추가 발 판정 순간 `TraitFxTrait.GunnerDualOutput` **Activated**(Pos = 원래 세 번째 발 원점 · End = 끝점) → `GunnerBeamView`:
  총구 섬광 `FX_DualOutput_Muzzle` + 나선 빔 `FX_DualOutput_Beam`(+Z 1m 저작, **축별 배율 Z 를 0 → 길이로 키워 총구 → 끝점으로 날아간다** — 평타 빔과 같은 박자 — 평타 몸통 버스트(0.05초) 전엔 숨기고 진행도 = 경과/`beamTravelDuration`(0.06초), 0.28초에 풀리며 사라짐) + 끝점이 사거리보다 짧으면 멈춤 불티 `FX_DualOutput_Stop`. 멈춤 불티·Hit 착탄은 빔이 끝점에 닿을 때(평타 착탄과 같은 박자).
- 몬스터 적중 **Hit** → 평타 몬스터 적중 + 적중 섬광 `FX_DualOutput_Hit`(멈춤 불티 위에 얹힘 — 평타 착탄 + 몬스터 적중과 같은 겹침). 허공(최대 사거리) = 착탄 없음.
- 한계 돌파와 함께면 **사건 Variant = 세 번째 원래 발의 피해 단계** → 평타와 같은 단계 색(`EffectTint`).
- 나선 간격은 길이에 비례해 늘어난다(1m 당 9바퀴 저작 — 14m ≈ 1.6m 간격, 6m ≈ 0.7m).

## 바뀐 파일
| 파일 | 내용 |
|---|---|
| `Player/Gunner/GunnerBeamView.cs` | 칸 `dualBeam`·`dualMuzzle`·`dualHit`·`dualStop` · `TraitSkillFx.Raised` 구독(`HandleTraitFx`) |
| `Player/Gunner/GunnerBasicAttack.cs`(**은희 영역**) | `CommitShotForTraits` 에 한계 돌파 단계 인자 → 예약 발에 저장 → Activated/Hit `variant:` 로 싣는다. 주석 "VFX 없음" 갱신 |
| `Traits/Gunner/GunnerTraitCombatRules.cs`(**은희 영역**) | `GunnerDerivedShot.OverdriveStage`(연출만, 생성자 선택 인자) |
| `Effects/EffectSocketPlayer.cs` | `Effect` 읽기 전용 프로퍼티(추가 발 빔이 같은 엔트리를 따로 낼 때) |
| `Effects/EffectManager.cs` | `PlayRecallable(entry, pos, rot, Vector3 axisScale)` — 축별 배율 원샷 + 핸들(색 칠하기용) |
| `Effects/EffectCatalog.cs`·`.asset` | `Gunner_DualOutput_Beam/Muzzle/Hit/Stop` 등록 |
| `Player_Gunner.prefab` | 칸 4개 연결 |
| SVN `50.Art/VFX/Common/Players/Player2/LeftClick_BasicAttack/DualOutput/` | 프리팹 4(+엔트리 4) · 재질 7 · 텍스처 6 · 메시 5 |

## 원본 제작(MyProject)
- `Tools/VFX/Dual Output (거너 이중 출력) 빌드` → `Export/Player2/DualOutput/` · 미리보기 `Scenes/DualOutputPreview.unity`(`DualOutputTester` — 평타 3발 대역 → 0.05초 뒤 추가 레이저, 허수아비·벽·허공 번갈아).

## 확인
- DebugTraitLab(Play): 거너 + 이중 출력 + 한계 돌파 → 평타 3발째 뒤 Activated 1번(Variant 3) → 섬광 + 빔(Z 배율 14 = 허공 최대 사거리, 착탄 없음) 생성·단계 색 — 로그.
- [ ] 민경 Play: 허수아비 적중(Hit 섬광)·벽 멈춤(불티) 화면 · MPPM 클라.

## SVN / git
- SVN: `Player2/LeftClick_BasicAttack/DualOutput/` 폴더 add.
- git: 위 코드·SO·프리팹 + 문서.
