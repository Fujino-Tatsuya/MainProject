# handoff — 서울알림체 TMP 글꼴 Static 고정

> 대상: 민경(VFX·UI 문구가 보이는 프리팹)·경석(몬스터·보스 UI·설정창). 작업: 은희 결정(a), 2026-10-23. 브랜치 `chore/SeoulAlrimStaticAtlas`(기준 `development` e0195093).
> 상세 규칙은 [localization.md §10](localization.md#10-글꼴--서울알림체-static-아틀라스).

## 1. 왜

기본 UI 글꼴 `Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset`(guid `dcd1a3c7…`, `TMP Settings` 기본 글꼴)이 **Dynamic** 이었다.
Play 중 아틀라스에 없는 글자가 화면에 나올 때마다 TMP 가 에셋에 글리프를 더해 **에셋 파일이 바뀌었다**(약 9.4MB 텍스트 YAML).
워크트리마다 다른 글자가 더해져 git 에 노이즈가 남고, 병합하면 아틀라스 텍스처·글리프 표가 충돌해 손으로 풀 수 없었다.

## 2. 무엇을 바꿨나

| 전 | 후 |
|---|---|
| `AtlasPopulationMode` = Dynamic, 2446자(이미 ASCII + KS X 1001 한글 2350자 포함) | **Static**, 2498자 — 기호·자모 52자 추가(—·–·→·×·①·★·「」·【】·ㅏ·ㅣ 등). 아틀라스 2048² **1장** 그대로 |
| 파일 9,439KB | 9,464KB |
| 새 문구 → Play 중 자동 추가(파일 변경) | 새 문구 → **메뉴로 채우고 폰트 에셋 커밋**(아래 3절). Play 는 에셋을 바꾸지 않는다 |
| — | 에디터 메뉴 `Tools/UI/Fonts/Refill SeoulAlrim (Static)` — `Assets/1.Scripts/Localization/Editor/SeoulAlrimStaticRefill.cs`(에디터 전용, 런타임 코드 변경 없음) |

- guid·기존 글리프·머티리얼·폴백 목록은 그대로다. 프리팹·씬 참조는 안 바뀐다.
- 메뉴를 다시 돌려도 더할 글자가 없으면 파일이 바이트 그대로다(확인함).

## 3. 새 문구 추가 절차

1. 문구를 넣는다(Strings 시트 ko·en, `UiStrings` 등 코드 상수, 문구 SO, 프리팹 TMP 글자).
2. 최신 SVN `GameData.xlsx` 가 있는 워크트리에서 `Tools/UI/Fonts/Refill SeoulAlrim (Static)` 실행(Play 정지 상태).
3. 콘솔 `[SeoulAlrimStatic]` 줄에서 글자 수가 늘었으면 `SeoulAlrimTTF-Medium SDF.asset` **한 파일만** 커밋. 안 늘었으면 변경 없음.
4. 다른 사람이 같은 에셋을 커밋했다면 병합 대신 **최신 development 를 받은 뒤 메뉴를 다시 돌려** 만든다(아틀라스는 손으로 병합하지 않는다).

### 3.1 경석 공유 — Dynamic 판을 커밋하지 말 것 (10-23 재고정)

- **`m_AtlasPopulationMode: 1`(Dynamic) 인 `SeoulAlrimTTF-Medium SDF.asset` 을 커밋하지 않는다.** 새 글자는 위 메뉴로 굽고, Static(`m_AtlasPopulationMode: 0`) 에셋을 커밋한다.
  커밋 전 `grep m_AtlasPopulationMode "Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset"` 가 `0` 인지 본다.
- 경위: 48d79acc(경석, 로컬 폰트 → 서울 폰트 기준판)가 이 에셋을 Dynamic 판(2453자)으로 덮어 Static 고정(95065a4e, 2498자)이 풀렸다. 그 판에만 있던 글자는 없었다(2453자 ⊂ 2498자).
- 브랜치 `chore/SeoulAlrimStaticRefix` 에서 메뉴로 다시 채워 **Static, 2498자, 아틀라스 1장**으로 잠갔다(guid 그대로, 재실행 바이트 동일, Static 상태에서 집합 밖 글자 추가 거부 확인).
- 메뉴 글자 집합에 **`현재 에셋 글자`** 출처를 더했다 — 지금 에셋 글자 표(characterTable)의 글자도 다시 굽는다. 다음에 Dynamic 판이 또 들어와도 그 판이 Play 중 모은 글자를 메뉴 한 번으로 잃지 않고 Static 으로 되돌릴 수 있다.

## 4. 폴백 상태 (바꾸지 않았다)

- 서울알림체의 폴백 = `Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset` 하나. `TMP Settings` 전역 폴백도 같은 에셋.
- NotoSansKR 은 **이미 Static**(11930자, 한글 11172자 전부, 4096² 1장, 자기 폴백 없음) — Play 로 바뀌지 않는다.

## 5. 알려진 한계

- 서울알림체 서체에 글리프가 없는 5자 `•` `›` `≈` `▸` `⚠` 는 아틀라스에 못 넣는다. `▸` 외 4자는 NotoSansKR 폴백이 그린다(글꼴 모양이 다르다).
  `▸`(U+25B8)는 두 글꼴 모두 없어 TMP 로 그리면 □ — 지금은 `EffectEntry` Inspector 툴팁에만 있어 게임 화면엔 안 나온다.
- 이모지·서로게이트 문자·이체 선택자(⚠️ 의 U+FE0F)는 굽지 않는다.
- KS X 1001 밖 한글(똠·햏 등)은 서울알림체에 없으면 폴백(NotoSansKR)이 그린다 — 문구에 쓰면 메뉴를 돌려 서울알림체에 넣을 수 있는지 콘솔로 확인.
- 문구 수집은 텍스트 스캔이다(코드 리터럴·SO `.asset`·씬/프리팹 `m_text`). 런타임에 조립되는 글자(사용자 이름·Steam 닉네임 등)는 미리 알 수 없어 폴백에 기댄다.
- 설정창 전용 글꼴 `Assets/50.Art/UI/Fonts/SeoulAlrimTTF-Heavy SDF.asset`(SVN)은 별도 메뉴(`SettingsFontAssetAuthoring`)가 만들며 이 작업 범위 밖이다.
- 사람 Play 확인 필요: Ko·En 각각 Play 후 `git status` 에 폰트 에셋 변경 0, 콘솔 Missing glyph(□) 경고 수.
