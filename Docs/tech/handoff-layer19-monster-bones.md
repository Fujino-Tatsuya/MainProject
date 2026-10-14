# handoff — 레이어 19 는 몬스터 팩 뼈가 쓴다 (경석 공유)

> 2026-10-14 · 은희 · 브랜치 `feature/WallConcept` (할 일 32)

## 무슨 일이 있었나
- 할 일 32 에서 튜토리얼 배리어용 **Barrier** 레이어를 처음엔 **19번**에 만들고(10-13 `3abc1bea`), 거너 Q·평타 빔 차단·플레이어 이동 장애물·리스폰 장애물 마스크에 넣었다.
- 그런데 Robot Sentries 팩 원본(SVN `Assets/50.Art/Char/Monster/Robot Sentries - 3D Character Mega Pack/…/Prefabs/P_*.prefab` 8종)이
  **모델 뼈 오브젝트 레이어를 19번으로 덮어쓰고 있었다**(`P_HumanoidBot` 12곳 등). 그동안 19번은 이름 없는 빈 레이어라 아무 영향이 없었다.
- `P_HumanoidBot`·`P_MortarBot` 은 그 뼈에 **켜진 비트리거 콜라이더**(팩 래그돌용, 13개)가 있어서,
  19번이 Barrier 가 되자 **거너 Q 가 HumanoidBot·MortarBot 몸에 막혀 관통하지 않았다**(플레이어 이동도 그 뼈에 막힘).

## 고친 것 (`c8a037aa`)
- Barrier 를 **20번**으로 옮기고 19번은 다시 이름 없는 레이어로 되돌렸다(TagManager·충돌 매트릭스·마스크 3개·코드 상수).
- 몬스터 원본 에셋(SVN, 경석 영역)은 **건드리지 않았다**.

## 앞으로
- 🔴 **19번 레이어에 이름을 붙이거나 마스크에 넣지 말 것** — 몬스터 팩 뼈가 들어 있다.
- 정리하고 싶다면(경석 판단): 팩 프리팹의 뼈 레이어 오버라이드를 Enemy(8)나 전용 레이어로 바꾸거나, 쓰지 않는 래그돌 콜라이더를 끄는 것. 그 전까지는 19번을 비워 둔다.
- 관련 코드: `Assets/1.Scripts/Utility/GameLayers.cs`(Barrier 는 이름으로 찾는다), `PlayerGameRuleData.DefaultObstacleMask`.
