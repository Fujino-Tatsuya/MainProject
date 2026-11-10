# 계획 = **사운드 2차 정리 — 재생 경로 통일 · 거리 감쇠 · 루프 끊기 · 미연결 연결 · 믹스** (2026-11-09 · 경석 · ✅ grill Q1~Q4 · ✅ Codex 교차검증 · ✅ Q5·Q6 · ⏳ 승인)

> 브랜치 `feature/SettingsDdol`. 클립 = SVN `4_Resources/Sound/Sound_2`(r628). 측정 = 전체 LUFS(Integrated) + 가장 큰 0.1초.
> Codex 검증 원문 = 세션 scratchpad `codex_mix_out.txt`(11-09). 아래 사실은 경석(Claude)이 코드로 재확인한 것만 적었다.

## 사실 (11-09 코드 재확인)
- **VFX 소리는 지금 3D로 나고, 멀면 거의 안 들린다.** `EffectManager.Fire` → `AudioManager.PlayOneShot(SoundID, pos)` → `BroAudio.Play(id, pos)`. 위치가 있으면 BroAudio 가 공간 설정이 없어도 3D 로 바꾸고(`AudioPlayer.cs` `SetSpatialBlend`→`SetTo3D`), 거리 기본값은 `AudioPlayer.prefab` 최소 1m · 최대 500m · 로그 감쇠. 리스너는 내 캐릭터(`PlayerAudioListenerActivator`) → **10m 에서 약 -20dB, 20m 에서 약 -26dB.**
  → 23호 돌진·점프 착지·몬스터 사망음이 "작게" 들리는 주원인. 내 스킬 소리(내 위치)와 내 앞 타격음은 감쇠 없이 크게 들려 차이가 더 벌어진다.
- **VFX 소리는 시트 값을 안 읽는다.** `volumeMultiplier`·`maxSimultaneous`·`retriggerCooldown`·`is3D`·피치 랜덤은 `AudioManager.Play(키)`(`PlayEntry`)에서만 적용. 시트의 S1_* 키는 전부 `is3D: 0` 인데 VFX 경로는 이걸 무시하고 3D 로 튼다.
- **`PlayEntry` 의 피치 배율이 엔티티 피치를 덮을 수 있다**(BroAudio 재생은 큐 → `LateUpdate` 시작, 그 전에 `SetPitch`). 거리 설정도 나중에 덮일 수 있다 → `OnStart` 에서 적용해야 한다.
- 1 초과 볼륨: 믹서 트랙을 잡으면 **+20dB(10배)까지 증폭된다**(`ToDecibel` 기본 증폭 허용). 트랙이 없으면 `AudioSource.volume` 경로라 1 로 잘린다.
- 동시 재생 제한(`SoundPlaybackLimiter`)은 소리가 끝날 때(`OnEnd`) 풀린다. 원샷은 막힐 일 없음. **루프를 Stop 없이 두면 그 키가 계속 막힌다.**
- 사운드 동기화는 **기존 클립 볼륨을 보존**하고 새 클립만 1 로 넣는다(앞 판의 "1로 되돌린다"는 틀림).
- BroAudio 일반 트랙 36개 · Unity 실제 음성 32개. 몹 대량 사망(사망 폭발 33개) 때 트랙 초과 경고 4회 — 소리가 버려지진 않고 믹서 없이 출력돼 볼륨·증폭이 틀어진다.
- 타격음 `S1_Monster_Hit` 클립은 -26 / -19.9 LUFS 로 작다. 크게 들리는 건 맞을 때마다 1회 + 내 앞(감쇠 없음). 11-09 병합(`49bbd7b9`)으로 23호 피격도 같은 엔트리.
- 간파 시작은 캐릭터마다 경로가 다르다 — 팔라딘은 `InterruptProductionDirector.LocalAttempt`, 거너·어쌔신은 각자 `OnClientPlay`. 디렉터 한 곳에만 넣으면 둘이 빠진다.
- Sound_2 미연결 20개(아래 3단계 표).

## 단계 (Codex 권고대로 순서 변경 — 루프 기반을 먼저)
1. **재생 경로 통일(코드).** `AudioManager` 에 `Dictionary<SoundID, SoundEntry>` 역조회(SoundID 동등성 = 엔티티 참조, 중복이면 경고). 두 `PlayOneShot` 오버로드 모두 카탈로그에 있으면 `PlayEntry` 로, 없으면 지금처럼 직행. 피치 배율·거리(min/max)는 `OnStart` 에서 적용, 배율 1 이면 생략.
   - **거리 감쇠(Q5):** 시트 S1_* 전부 `is3D` 1 · `minDistance` 10 · `maxDistance` 50 · 로그 감쇠 그대로 → 10m 안 100% · 20m 약 -6dB · 30m 약 -9.5dB(지금은 10m 에서 -20dB). 숫자는 청취 후 조정. 보스방 대각선 약 42m.
   - **다른 플레이어 소리 30%(Q6):** `AudioManager` 에 `remotePlayerVolume` 0.3 한 칸. 소리의 주인이 원격 플레이어면 시트 볼륨에 곱한다.
     주인 판정 = ① 주인을 아는 경로는 정확히 — 발소리(`PlayerFootstepSound`)·대시(`PlayerDashVfx`)·간파 성공·어쌔신 E(직접 재생 코드), 소켓 루프(`EffectSocketPlayer` 추종 대상의 부모 `Player`) ② 위치만 넘기는 VFX 원샷은 엔트리가 플레이어 VFX(`Players/Player1~3` 폴더 = 키 접두사 `S1_Paladin/Gunner/Assassin`)일 때 재생 위치에서 가장 가까운 플레이어로 판정(2m 이내, 내 캐릭터와 겹치면 100%).
     몬스터·보스·타격음은 대상이 아니다(누가 때렸든 몬스터 위치의 3D 소리).
2. **루프 소리 수명(코드).** `EffectPart` 에 `stopOnRelease` 옵션(기본 끔). `Fire` 가 그 파트의 재생기만 들고 있다가 `Release`(본편 Stop → outro) · `Recycle` · `ReleaseImmediate` · 매니저 파괴에서 `Stop(0.05)`. 콜백은 (슬롯, generation) 검증. 리미터는 직접 Release 하지 않는다(OnEnd 와 중복 방지).
3. **미연결 20개 연결.**
   | 소리 | 붙일 곳 | 전파 |
   |---|---|---|
   | `BGM_Boss_001` | 보스 `Combat` 진입. `SceneBgmSwitcher` 캐시와 한 곳에서 관리 | 서버 판정 → 복제 Phase 관찰 |
   | `23_Jump_Start` | `CrossFadeJumpTakeoffClientRpc` | 기존 ClientRpc |
   | `23_Pylon_Up` | `BossChargingPylon.BeginCharge` | 서버 → RPC 1개 |
   | 23 취약 글리치 | `OnVulnerableVisualChanged` false→true | 복제값 |
   | `Player_Hit` | Player `ClientHitAttributed` (타격당 1회) | 기존 피해 RPC |
   | `Vignetting` | 저체력 진입/회복(임계값 결정 필요) | 로컬 HUD |
   | `Interrupt_Start_01/02` | 간파 시작 — 3캐릭터 각 경로, 같은 키 Random 2개 | 전 피어 |
   | `Interrupt_Wall_01` | 취약 보스 벽 충돌 성공 분기(추측 — 경석 확인) | 서버 → RPC |
   | `Mortar_Attack` | `SkyStrikeShowRpc`(발사) 또는 착탄 | 각 피어 |
   | `Assassin_R_Skill_End` | `FX_Assassin_BladeVortex_Entry.outroParts` | 전 피어 |
   | `Gunner_Q_Skill_Loop` | `FX_GunnerCharge_Entry` + `stopOnRelease` | 전 피어 |
   | `Paladin_Q_Skill_Loop` | `FX_ShieldDash_Smash_Entry` + `stopOnRelease` | 전 피어 |
   | `Paladin_E_Skill_Fail` | `FX_GuardianWill_Release_Entry`(추측 — 성공 땐 안 나게) | 각 피어 |
   | 대시 swoosh · 발소리 테스트 | 기존 `S1_Dash`·`S1_Footstep` Random 후보(경석 청취 후) | 기존 |
   | 통풍구 발소리 3 | 새 키 — 바닥 재질 판정은 이번 범위 밖, 키만 준비 | — |
   | UI 클릭 | 기존 `UI_Click` 클립을 Sound_2 wav 로 | 로컬 |
4. **초기 믹스(xlsx).** 배율 = 10^((목표 − 측정)/20) 로 초깃값. 효과음 약 -18 LUFS, 사망음 > 타격음. 타격음 `maxSimultaneous` 2 · `retriggerCooldown` 0.06, 사망음 4 · 0.06. BGM 3키 그대로(Q2). 이후 경석 청취로 조정. 부족하면 BroAudio `PlaybackGroup` 으로 일반 SFX 동시 수 제한(BGM·간파·보스 예고음 제외).
5. **검증.** 컴파일 · 솔로 Play(로비·맵·보스) · MPPM 3인(원격 Q 시작/취소, 동시 간파, 보스 BGM 전환, 대량 사망). 합격: 트랙 경고 0 · 루프 종료 후 남는 소리 0 · 호스트 중복 재생 0 · 경석 청취.

## 리스크
- 1단계 후 VFX 소리에 시트 피치 랜덤(0.95~1.05)이 켜진다 — 원하면 키별 1·1.
- 3단계 일부 호출부가 은희(플레이어·네트워크)·민경(VFX) 영역 — 한 줄 훅은 `Docs/tech/handoff-sound-mix.md` 로 공유.
- 추측 항목(Vignetting·Interrupt_Wall·Paladin_E_Fail)은 연결 전에 경석이 의미 확인.

## 결정 (grill)
| 항목 | 결정 |
|---|---|
| Q1 볼륨 조절 방식 | **시트로 통일** — 1단계 |
| Q2 BGM 내리는 폭 | **그대로** — BGM 3키 안 건드림 |
| Q3 미연결 범위 | **전부** 20개 |
| Q4 루프 끊기 | **포함** — 2단계 |
| Q5 공간음(거리 감쇠) | **3D 유지, 거리 감쇠만 완만하게**(팀 합의 11-09). 2D 는 UI·연출 전용 — 둘 다 이번 범위 밖(연출 작업 없음 · UI 에셋 미전달) |
| Q7 시트 볼륨 배율 | **전부 1 — 시트로 키우거나 줄이지 않는다**(경석 11-09). 효과음은 유저가 개별 조절할 수 없으니 **클립 자체가 비슷한 크기**여야 한다 → 크기 문제는 사운드 담당에 클립 수정 요청(23호 돌진·점프 착지·몬스터 사망음 키우기, 드론 앞 무음). 4단계 초기 믹스는 취소, 동시 수 제한(타격 2·0.06 / 사망 4·0.06)·3D 거리만 유지 |
| Q6 다른 플레이어 소리 | **30%**(팀 합의 11-09). 내 스킬·공격·발소리·대시 = 100%. 아예 안 들리면 어색, 100% 는 시끄러움 |
