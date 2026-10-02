# PLAN — 캐릭터 선택 화면 (중간 발표용 최소 구현)

> 2026-10-02 · 은희(Claude) · 브랜치 `feature/SelectCharactorUI` · 그릴 13문항 완료 · ✅ 구현(1단계 Codex `09e9cc57` · 2단계 Claude) · 🔴 MPPM Play 확인 대기
> 배경 사실: [Docs/tech/player-prefabs.md](Docs/tech/player-prefabs.md) §8.3, `LobbyUIController`, `NetworkLoadingFlowController.ResolvePlayerPrefabForClient`

## 목표
Lobby 씬에서 StartHost/StartClient 로 접속 → 캐릭터 선택 패널에서 3개 버튼 중 고름 →
각 플레이어 슬롯에 고른 초상화·Ready 표시 → 호스트 Start → MapScene 에서 **각자 고른 Variant 로 스폰**.

## 확정된 규칙
| # | 결정 |
|---|---|
| Q1 | 호스트 = Start 만(자동 Ready), 클라 = Ready 만. 클라 전원 Ready 면 Start 활성 — **기존 동작 유지** |
| Q2 | 같은 캐릭터 중복 선택 허용 |
| Q3 | Ready 는 토글. Ready 중엔 캐릭터 변경 불가(UI 잠금 + 서버도 거부) |
| Q4 | 입장 시 기본 선택 = 0번(Paladin) |
| Q5 | 퇴장 시 그 클라의 선택·Ready 제거. 게임 시작 후 난입은 범위 밖 |
| Q7 | 분담: **네트워크/데이터 코드 = Codex**, **씬·UI·에셋 배선 = Claude** (아래) |
| Q8 | 버튼 3개: Paladin / Gunner / 3번 = "준비 중" 비활성(로스터에 빈 칸) |
| Q9 | 새 패널 `Panel_CharacterSelect`(버튼 3개)만 추가. 초상화는 기존 `ClientInfo` 슬롯에 Image 추가. Ready/Start 는 기존 `Pannel_Ready&Start` |
| Q10 | 기존 named message 방식 확장: `Lobby.CharacterRequest` 신설 + `Lobby.State` 에 클라별 캐릭터 id |
| Q11 | 새 SO `CharacterRoster`(id 순 배열: 이름·Variant 프리팹·초상화·사용 가능). `CharacterDefinition` 은 안 건드림 |
| Q12 | 선택 없음/범위 밖/비활성 칸 → `defaultPlayerPrefab`(Paladin). Dev Boot 경로는 지금 그대로 |
| Q13 | 범위 밖: 전투 HUD 초상화(Gunner 도 Paladin 얼굴 — 별도 작업), `char_select` 아트 전면 적용(버튼 배경 정도만) |

## 설계
- **로비 → 맵 씬 간 전달**: Lobby 씬은 맵 로딩 때 언로드되므로, 서버의 clientId→캐릭터 id 맵을
  static 홀더(`SessionResult` 와 같은 방식)에 둔다. 세션 시작·종료 때 비우고, 퇴장 시 제거.
- **서버 권한**: 선택 요청은 서버가 검증(사용 가능한 칸인지, Ready 중이 아닌지)한 뒤 상태를 브로드캐스트.
  호스트 자신의 선택도 같은 경로.

## 1단계 — Codex (코드)
1. `CharacterRoster` SO: 항목 = 표시 이름 / Variant 프리팹 / 초상화 Sprite / 사용 가능 여부. id 로 안전 조회.
2. static 선택 홀더: clientId→id 설정·조회·제거·전체 비우기.
3. `LobbyUIController`
   - `[SerializeField] CharacterRoster`
   - `Lobby.CharacterRequest`(클라→서버), 서버 검증, 기본 0번, 퇴장 시 제거
   - `Lobby.State` 페이로드에 캐릭터 id 추가
   - 공개 API: 로컬 캐릭터 선택 요청, 로컬 선택 id·로컬 Ready 조회 (`StateChanged` 이벤트는 기존 것 사용)
   - 슬롯 갱신 시 초상화 전달
4. `LobbyPlayerSlotView`: `[SerializeField] Image portraitImage`, 상태 갱신에 초상화(없으면 숨김) 추가.
5. `NetworkLoadingFlowController`: `[SerializeField] CharacterRoster`, `ResolvePlayerPrefabForClient` 를 홀더+로스터로 구현, 실패 시 `defaultPlayerPrefab`.
6. 검증: 컴파일 오류 0. Play 는 실행하지 않음. 커밋만 하고 push 는 하지 않음.

## 2단계 — Claude (씬·에셋)
1. `Assets/9.ScriptableObject/` 에 `CharacterRoster.asset` 생성: Paladin / Gunner / 빈 칸(비활성). 초상화 = `50.Art/UI/HUD/portrail_*.png`.
2. `3.LobbyScene`
   - `Panel_CharacterSelect` + 버튼 3개(이름 텍스트, 초상화). 3번은 "준비 중" 표시하고 비활성
   - `ClientInfo` ×3 에 초상화 Image 를 추가하고 연결
3. `LobbySceneManager`: 패널·버튼 찾기, 접속 성공 시 패널 표시·퇴장 시 숨김, 버튼 → 선택 요청, Ready 중 버튼 잠금·선택 강조.
4. `NetworkManager.prefab`·`LobbyUIController` 에 로스터 연결.
5. Refresh 후 컴파일 확인. 문서(`player-prefabs.md` §0·§8.3, CONTEXT.md) 갱신.

## 완료 조건 (은희가 MPPM 으로 직접 Play)
호스트 1명 + 클라 2명으로 확인한다.
1. 서로 고른 초상화와 Ready 표시가 모든 화면에 똑같이 보인다.
2. 클라 전원 Ready 가 되면 호스트의 Start 가 켜진다.
3. 맵에서 각자 고른 캐릭터로 스폰된다.

## 리스크
- 씬 편집은 텍스트 에셋 1~2개라 Unity 를 켠 채 해도 된다(CLAUDE.md 6). 50.Art 는 참조만 하고 수정하지 않는다.
- `Lobby.State` 페이로드 형식이 바뀌므로, 같은 빌드끼리만 접속된다(MPPM 은 문제없음).
