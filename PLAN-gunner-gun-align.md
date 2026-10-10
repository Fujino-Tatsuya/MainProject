# PLAN — 거너 레이저건 정렬 + 기본 공격 손 이탈 (2026-10-09)

작성: 은희(Claude) / 브랜치: `fix/GunnerGunAlign`(development `778bbb87` 에서 분기)
관련: [PLAN-gunner.md](PLAN-gunner.md) · [character_gunner.md](Docs/design/character/character_gunner.md) · [player-prefabs.md](Docs/tech/player-prefabs.md)

> 담당 = 은희(Player 영역). 보스·몬스터 코드 수정 없음. VFX 에셋(민경)은 건드리지 않는다 — 소켓 위치만 따라 움직인다.

---

## 1. Goal

- 레이저건 **총구가 캐릭터 정면**을 향한다 — Q·E·우클릭·R·기본 공격(발사 중).
- **양손이 총 손잡이에 붙는다** — 오른손 `Grip_R`(Q 계열은 `Brace_R`), 왼손 `Grip_L`.
- 기본 공격 발사 애니가 **발사 간격에 맞춰 클립 전체**를 재생한다.

범위 밖(이번에 안 함): 서버 기본 공격 판정 시작점(§6 보류) · idle/walk 의 총구 34° 옆 자세(원본 애니 연출) ·
Q 클립 왼손 약 4.5cm 잔차(원본 애니) · Blender 원본 수정.

---

## 2. 확인된 사실 (2026-10-09, Unity 에디터 실측 — 클립 13개 샘플링 + 실제 Animator 레이어 합성)

| 항목 | 사실 |
|------|------|
| 총 부착 | `LaserGun`(laser_gun.fbx) 은 이미 **`rig/artillery` 본**의 자식. 오버라이드 = 위치 (0.0067, 0.6147, −0.0307) · 회전 euler (−10.6, 94.8, −73.1) — 09-30 G9(`73e6f1f1`) 손 조정값 |
| 총 모델 기준점 | `LaserGun_Mount`(euler 270,0,0) 아래 `Grip_R` (−0.032, 0.200, −0.118) · `Grip_L` (−0.034, 0.412, −0.121) · `Brace_R` (0.110, 0.279, −0.060) · `Muzzle` (0, 0.598, 0) — 총열 = Mount +Y |
| 애니 저작 기준 | 모든 클립에서 손을 `artillery` 좌표로 보면 위 기준점과 **소수점 3자리 일치**(idle 오른손=`Grip_R`, 왼손=`Grip_L`, Q 오른손=`Brace_R`) → **Mount 프레임 = artillery 프레임(오프셋 0)** 으로 만든 애니 |
| 현재 결과 | 오프셋 때문에 총이 **앞뒤로 뒤집힘** — 총구 yaw ≈ 174°(뒤), 손이 엉뚱한 그립에 |
| 오프셋 0 결과 | Q·E·우클릭·궁 클립 총구 **yaw 0.0°**, pitch 1~6° |
| 기본 공격 레이어 | Base `Gunner_Attack_Start`(= `Q_charge_loop`) + UpperBody `Gunner_Attack_Fire`(= `gunner_attack01`, 10-02 `8940e020` 교체). `attack01` 의 루트 컨트롤 `c_pos` = 180°, `Q_charge_loop` = 146.3° — 상체 마스크가 `rig/c_pos` 를 빼므로 **팔만 34° 돌아 손이 총에서 11~19cm 이탈** |
| `gunner_attack_weapon_v05` | 같은 FBX 의 미사용 테이크(60f, 1.0s). `c_pos` 146.3° = 하체와 같음. 레이어 합성 실측 **yaw 0°, pitch 1~5°, 손–그립 0~4cm**, 시작·끝 포즈 동일(매 발 재시작해도 안 튐) |
| 발사 속도 | `GunnerBasicAttackData.fireClipName` = `"gunner_attack"`(기본값) 인데 컨트롤러엔 그 클립이 없음 → `FireSpeed()` 가 길이를 못 찾고 **항상 1** → 1.17s 클립의 앞 30%만 매 발 반복(총이 들리는 구간) |
| 방향 있는 VFX | `GunnerBeamView.FaceForward` = `transform.rotation`(**플레이어 루트**). 조준으로 도는 건 `Armature` 뿐 → E 냉기·Q 배기 등이 스폰 방향으로 나갈 수 있음(코드 판독, Play 확인 필요) |
| 아트 | SVN r403~415 사이 거너 아트 변경 없음. 수정 대상 파일은 upstream 최근 변경과 겹치지 않음(`GunnerBasicAttack.cs` 는 `778bbb87` 기준으로 수정) |

---

## 3. 접근 — 단계별

### S1. 총 오프셋 0 — `Gunner_Armature.prefab` (텍스트, Unity 꺼진 상태)
- `LaserGun` 오버라이드: 위치 (0,0,0) · 회전 quat (0.7071068, 0, 0, 0.7071068) = euler (90,0,0) = `Inverse(Mount 로컬 회전)` · EulerHint (90,0,0). **스케일은 건드리지 않는다**(lossy 1.6 유지).
- 결과: `Muzzle` 아래 `BeamMuzzle`·VFX 소켓 11개(오프셋 전부 0)가 실제 총구(몸 앞)로 같이 이동.

### S2. 발사 클립 → `gunner_attack_weapon_v05` — `GunnerAnimatorController.controller` (텍스트)
- `Gunner_Attack_Fire` 의 `m_Motion` fileID `-4980908371892057456`(attack01) → `-3545710686996893968`(v05). 상태·파라미터 이름 불변 → NetworkAnimator 계약 변화 없음.

### S3. 발사 속도를 이름이 아닌 실제 재생 클립에서 — `GunnerBasicAttack.cs`·`GunnerBasicAttackData.cs`
- `FireSpeed()` 의 클립 이름 조회 삭제. 상체 레이어가 `FireStateName` 에 들어간 뒤 `LateUpdate` 에서 `GetCurrentAnimatorClipInfo(upper)` 로 길이를 1회 읽어 캐시하고 `FireSpeed` 를 다시 넣는다(`SetAnimator` 에서 초기화 — 기존 그대로). 첫 발만 한 프레임 속도 1.
- `fireClipName` 필드·`FireClipName` 삭제(에셋에 직렬화 안 됨, 데이터 테이블 열 아님, 참조 2곳뿐).
- v05 기준 FireSpeed = 1.0 / 0.35 ≈ 2.86 → 매 발 클립 전체 재생.

### S4. 방향 있는 VFX 기준 = Armature 정면 — `GunnerBeamView.cs`
- `FaceForward`: `transform.rotation` → `Quaternion.LookRotation(PlayerMovement.CurrentFacing)`. 주석(“좌클릭이 BeamMuzzle 회전을 덮어쓴다” 류) 중 사실과 다른 부분 정리.
- Play 에서 E 냉기·Q 배기·충전 루프 방향 확인 후 확정 — 오히려 나빠지면 이 단계만 되돌린다.

### S5. 저작 도구·문서
- `GunnerShellAuthoring.AttachGun`: 오른손 → **`artillery` 본 + `Inverse(Mount 로컬 회전)`**. 발사 상태 모션 기본값 = v05, 이미 모션이 있으면 덮어쓰지 않음(에디터 교체 보존).
- `player-prefabs.md` 의 `hand.r/LaserGun` → `rig/artillery/LaserGun`. `CONTEXT.md` 인수인계.
- 진단 스크립트 `Player/Editor/GunnerGunDiag.cs` 는 검증 후 **삭제**(커밋 안 함).

---

## 4. 리스크

- **총구가 몸 뒤 → 앞(약 0.8~0.9m 이동).** 기본 공격 서버 판정 시작점(`muzzle.position`)도 같이 앞으로 간다. 벽 관통·붙은 적 건너뛰기는 수치상 없음. 단 **몸 앞 약 0.36m 안의 적은 피해는 들어가는데 빔·피격 연출이 안 보일 수 있다**(`ShowBasicShot` 의 `distance<0.01` 조기 반환) → §6 보류 항목, Play 에서 확인.
- **VFX 위치 체감 변화**(소켓이 총구 앞으로) — 민경에게 공유.
- **반동 감소**: v05 반동 약 4°(attack01 은 13~29° 들림) — 타격감은 은희 Play 판단.
- `GunnerShellAuthoring` 재실행 시 컨트롤러 모션을 덮어쓰던 동작이 바뀐다(보존 쪽으로).

## 5. 검증

1. 텍스트 수정 후 Unity 실행 → 컴파일 0 에러, `Gunner_Armature.prefab`·컨트롤러 **guid 불변**·Unity 재직렬화 diff 없음(`git diff`).
2. 진단 메뉴(임시) 레이어 합성 측정: 기본 공격 발사 중 **총구 |yaw| ≤ 1°**, **손–그립 ≤ 4cm**; Q·E·우클릭·궁 yaw 0°.
3. Play(Dev Boot 거너): 기본 공격 클릭·홀드 연사·입력 창 / Q 충전·발사 / E / 우클릭 / R — 총 방향·손·VFX 위치·방향 스크린샷. 근접(보스·허수아비에 붙어서) 기본 공격 빔 표시 확인(§6 판단 근거).
4. MPPM 2인: 원격 프록시에서 총 방향·발사 애니 속도 동일.
5. EditMode 테스트(거너·플레이어 계열) 통과.

## 6. 보류 — 결정 필요

- **기본 공격 서버 판정 시작점**: (a) 몸 중심(루트+높이, Q 와 같은 방식)으로 분리하고 연출만 총구 / (b) 총구 유지 + 거리 0 이면 착탄점 `ClosestPoint` 보정 / (c) 그대로. → S1 적용 후 Play(§5-3) 결과 보고 은희 결정.
