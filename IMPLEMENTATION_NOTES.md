# IMPLEMENTATION_NOTES — 몬스터 FSM 프레임워크 (슬라이스 1)

작성: 2026-07-16 / 브랜치: feature/map-player-merge
대상: `Assets/1.Scripts/Monster/` 신규 코드 (기존 팀원 BT 코드 무수정)

## 계획 대비 이탈 / 판단 기록

1. **MonsterMeleeAttack가 OverlapAttack을 재사용하지 않고 오버랩 로직을 자체 구현**
   - 이유: 근접 공격에 "넉백" 책임과, 코드에서 직접 호출 가능한 public `Hit()`(선딜 후 폴백)이 필요.
     OverlapAttack은 넉백이 없고 Hit()만 노출. 얕은 래핑보다 BaseAttack을 직접 상속해 ColliderInfo 오버랩
     패턴(프로젝트 확립 패턴)을 따르는 편이 책임이 명확. `TryResolveHit`/`InitializeAttackInfo`/`targetLayer`
     등 기존 계약은 그대로 준수.

2. **공격 히트 판정을 코드 구동(attackWindup 타이머)으로 처리 — 애니메이션 이벤트가 아님**
   - 이유: 슬라이스 1 시점에 ChompBot Animator/클립/이벤트 자산이 확정되지 않음. 자산 없이도 동작해야 하므로
     MonsterBase가 `attackWindup` 경과 시 `meleeAttack.Hit()`를 서버에서 1회 호출.
   - 이관 경로: Animator 이벤트가 확정되면 애니 이벤트에서 `Hit()`를 호출하도록 옮기고, MonsterBase의
     코드 구동 히트(HandleAttack 내 `_attackFired` 블록)를 제거/비활성. 코드에 주석으로 명시함.

3. **이동 블렌드용 `NetworkVariable<float> _animSpeed` 추가**
   - 상태→Animator 매핑 규칙(요구사항)에 더해, 근접 추격형의 locomotion 블렌드가 상태만으로는 부족해
     에이전트 실제 속도를 복제해 클라 Animator `Speed` 파라미터를 구동. 파라미터 없으면 graceful.

4. **상태이상 차단 시맨틱(현재 프로젝트에 시간제 상태이상 실체가 없어 신규 정의)**
   - BlocksMovement = Airborne | Stunned | Rooted
   - BlocksAttack   = Airborne | Stunned
   - BlocksInterrupt = SuperArmor (슈퍼아머면 피격 시 공격 취소/Hit 전이 안 함, 데미지만)
   - 슈퍼아머 활성 중 들어오는 CC(SuperArmor 외)는 ApplyStatus에서 무시.
   - 은희가 성장/스킬 연동 시 값·지속시간·차단 규칙을 `IMonsterStatusFacade`/`MonsterStatusEffect`에서 조정.

5. **StatusEffectType은 Unit의 필드가 아닌 MonsterStatusEffect 자체 NetworkVariable로 복제**
   - 근거: Unit._statusEffectType은 복제되지 않는 평범한 서버 필드(플래그). 클라 반영이 필요한 상태이상은
     MonsterStatusEffect가 `NetworkVariable<StatusEffectType>`로 독립 복제. Unit 쪽은 수정 금지 대상이라 건드리지 않음.

## 검증 상태 (정직 보고)

- **Unity MCP 컴파일 검증 미완료(환경 블로커).** `unity_recompile_scripts`는 성공 반환했으나 이후
  도메인 리로드로 MCP 브릿지가 unreachable 상태가 되어 `get_compilation_status` 재연결 실패(2분+ 재시도).
  Memory의 "Unity MCP 브릿지 경로" 함정과 유사.
- Editor.log 전체에 `error CS` 0건, "forced synchronous recompile" 완료 기록 존재. 단 **신규 스크립트의
  `.meta`가 아직 생성되지 않아** 그 컴파일은 신규 파일을 포함하지 않았을 가능성이 높음(Unity 미임포트).
- 정적 자체 리뷰로는 컴파일 에러를 발견하지 못함(Unit/BaseAttack/ColliderInfo/NGO API 시그니처 대조 완료).
- **다음 조치 필요(사용자):** Unity 에디터에 포커스를 줘 신규 스크립트 임포트 + 컴파일을 트리거하고,
  MCP 브릿지 재연결 후 컴파일 상태를 확인. 에러 발생 시 알려주면 즉시 수정.

## 미검증 리스크

- NavMesh가 베이크되지 않은 씬에서는 이동 안 함(코드는 `isOnNavMesh` 널가드로 예외는 없음).
- NetworkTransform이 프리팹에 없으면 서버의 이동/회전이 클라에 반영되지 않음(배선 레시피 참조).
- Animator Controller/파라미터 부재 시 애니 재생은 무시(graceful) — 시각 피드백만 없음.
- 디졸브 셰이더/프로퍼티 부재 시 즉시 디스폰 폴백.

---

# IMPLEMENTATION_NOTES — ChompBot 데모 프리팹 조립 (2026-07-16, 후속)

대상: `Assets/2.Prefabs/Monster/ChompBot.prefab`, `.../Data/ChompBotData.asset`, `Assets/DefaultNetworkPrefabs.asset`
씬 무수정(임시 오브젝트는 KMKScene에 생성 후 삭제, 씬 저장 안 함).

## 계획 대비 이탈 / 판단 기록

1. **Unity MCP `set_component_property`가 스칼라(bool/float/int/string)만 반영 — Vector3/LayerMask/오브젝트참조/GameObject.layer는 무반영(무음 실패).**
   - 증상: `applyKnockback`/`knockbackStrength`(MonoBehaviour 스칼라) 세팅은 `{success:true}` 응답, 반면
     콜라이더 size/center, `targetLayer`/`playerMask`(LayerMask), `data`/`colliderInfo`(참조), 루트 layer 세팅은
     빈 `{}` 응답 후 프리팹 YAML에 미반영(size 1×1×1, m_Bits 0, fileID 0, layer 21 유지).
   - 조치: 프리팹을 `unity_create_prefab`로 저장한 뒤, **프리팹 .prefab YAML을 직접 편집**해 결정적으로 배선.
     모든 fileID는 저장된 프리팹에서 확인 후 사용. 참조/레이어/치수 전부 이 경로로 확정.
   - 참고: `Hurtbox.ownerUnit`은 컴포넌트 자체 `OnValidate`가 루트 Unit(MonsterBase)로 자동 해석해 이미 배선됨.

2. **베이스 P_ChompBot이 풀 래그돌 프리팹 — 뼈마다 Rigidbody+CharacterJoint+Collider 존재(레이어 19/21).**
   - 현재 상태: 뼈 Rigidbody는 전부 kinematic. 계획대로 스트립하지 않고 **그대로 유지**(대규모/고위험 수술 회피, "최소 변경" 원칙).
   - 전투/탐지 영향 없음: 탐지·근접판정은 playerMask(레이어 6)로, 플레이어 공격은 Enemy(8)로 레이어 마스킹 →
     레이어 19 뼈 콜라이더는 오버랩 쿼리에서 제외됨(중복 히트/오탐 없음).
   - 잔여 리스크(사용자 결정 필요): 몬스터당 뼈 13개분 Rigidbody/CharacterJoint 오버헤드, 레이어 19 콜라이더가
     환경/플레이어와 물리 충돌할 여지, 사망 시 래그돌 활용 여부. → 데모엔 무해하나 정리 원하면 후속 스트립 권장.

## MCP로 설정 못 해 YAML 직접 배선한 필드(수동 배선 대체분)

- 루트 `ChompBot` GameObject layer → Enemy(8)
- 루트 `CapsuleCollider`(몸통): height 2, center (0,1,0), radius 0.5, isTrigger=false, bodyCollider로 연결
- `MonsterBase`: data=ChompBotData, agent/animator/status/meleeAttack 참조, playerMask=Player(64), bodyCollider=몸통 캡슐
- `MonsterMeleeAttack`: colliderInfo=자기 ColliderInfo, targetLayer=Player(64) (런타임에 MonsterBase가 재설정)
- `MeleeHitbox` `BoxCollider`: size (1.5,1.5,2), center (0,1,1), isTrigger=true
- `Hurtbox` GameObject layer → Enemy(8); `CapsuleCollider`: height 2, center (0,1,0), isTrigger=true

## 검증 상태 (정직 보고)

- `unity_get_compilation_status`: errorCount 0, warningCount 0.
- `unity_get_console_logs(Error)`: 신규 에러 0.
- `unity_get_prefab_info`: 루트 layer=Enemy + 10개 컴포넌트, 자식 Hurtbox(Enemy)/MeleeHitbox 및 SRG 모델/Animator 확인.
- DefaultNetworkPrefabs.asset에 ChompBot 엔트리 추가(fileID 5083357185944259280, guid 7a0deecc1539ff543aa747158bacf514).
- 미검증(런타임): 실제 스폰/이동/공격은 NavMesh 베이크된 MonsterScene + MPPM 플레이에서만 확인 가능(사용자 몫).
