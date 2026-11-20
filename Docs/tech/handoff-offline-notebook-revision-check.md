# handoff — 노트북(SVN 접속 불가) 작업용: 현재 git·SVN 리비전 확인 요청

> 받는 사람: **SVN 서버에 접속되는 PC 의 에이전트/팀원**. 작성: 노트북 쪽 Claude, 2026-11-18, 브랜치 `development`.
> 요지: 이 노트북은 **SVN 서버(`svna.gameinjae.kr`)에 접속할 수 없다.** 그래서 서버 기준 최신 리비전을 직접 못 본다.
> 아래 "회신 요청"을 채워서 돌려주면, 노트북은 그 값에 맞춰 작업한다.

## 1. 노트북 현재 상태 (오프라인 실측, 2026-11-18)
| 항목 | 값 | 출처 |
|---|---|---|
| git 브랜치 / HEAD | `development` / `209d52d5` ("merge: 293 튜토리얼 T40 …", 2026-11-16 20:17) | `git log -1` · 작업 트리 clean |
| SVN 작업사본(`Assets/50.Art`) 리비전 | **r706** (Last Changed r706 · 7P_LeeEunHi · 2026-11-16 19:35) | `svn info` (로컬 `.svn/wc.db`, 서버 접속 아님) |
| git 이 전제하는 아트 핀 | **r696** (`Docs/tech/art-svn.json` `pinnedRevision`, pinnedAt 2026-11-16) | 파일 |

- ⚠️ 작업사본 r706 ≠ 핀 r696. 노트북이 핀보다 앞선 리비전을 받아 둔 상태다(마지막으로 접속이 됐던 시점 기준). 서버 최신이 몇인지는 모른다.
- `check-environment.py` 는 `svn` 리비전을 서버와 대조하므로 이 노트북에선 접속 부분이 실패할 수 있다(무시 가능).

## 2. 회신 요청 (SVN 접속되는 PC 에서 실행해 값을 알려 주세요)
```bash
git fetch origin && git rev-parse origin/development && git log -1 --format='%H %cd' origin/development
svn info --show-item revision https://svna.gameinjae.kr/svn/GA7thFinal_VeyTrace/4_Resources/Art
svn log -l 5 -q https://svna.gameinjae.kr/svn/GA7thFinal_VeyTrace/4_Resources/Art
svn info https://svna.gameinjae.kr/svn/GA7thFinal_VeyTrace/4_Resources/Art/DataTable~/GameData.xlsx   # Last Changed Rev
```
알려 줄 것:
1. **origin/development 최신 SHA** — 노트북 `209d52d5` 보다 앞서 있는지.
2. **SVN 서버 최신 리비전** — r706 보다 앞서 있는지, `art-svn.json` 핀(r696)과의 차이.
3. **`GameData.xlsx` 의 마지막 변경 리비전·작성자** — 노트북 xlsx 가 최신인지 판단하는 기준.
4. r706 이후 리비전이 있다면 각 리비전이 **무엇을 바꿨는지 한 줄**(특히 `.meta`·FBX·텍스처 — 노트북엔 안 받아질 것).

## 3. GameData.xlsx 는 이메일로 따로 받는다 (SVN 아님)
- 대상: `Assets/50.Art/DataTable~/GameData.xlsx` (SVN 소유, git 은 `50.Art/` 전체를 무시한다 — `.gitignore:84`).
- 노트북 사용자가 **이메일로 받아서 직접 덮어쓴다.** 이 노트북에서 `svn update/commit/lock` 으로 xlsx 를 다루지 않는다.
- 이 경로로 받은 xlsx 는 SVN 잠금(Get Lock)을 잡지 않은 사본이다. 그러므로:
  - 노트북에서 **고친 xlsx 를 SVN 에 직접 올릴 수 없다.** 수정이 생기면 이메일로 다시 보내 SVN 접속되는 PC 에서 잠금 → 반영 → 커밋한다(두 사람 동시 수정 병합 불가 — `Docs/tech/data-table.md`).
  - 보내는 쪽은 메일에 **xlsx 의 SVN 리비전(Last Changed Rev)** 을 같이 적어 달라. 노트북은 그 리비전을 기준 값으로 기록한다.
  - xlsx 를 받은 뒤 테이블 모드 Play 가 시작 전에 멈추면(오류 위치 `GameData.xlsx › 시트!C3`) 코드(git)와 xlsx 버전 불일치를 먼저 의심한다.
- 아트(FBX·텍스처 등)는 이 방법으로 안 받는다. r706 이후 변경은 노트북에서 반영 안 된 채로 간다 — 2번 회신의 4번 항목으로 영향 범위만 파악한다.

## 3.5 회신 반영 결과 + 노트북 최종 환경 (2026-11-18 22:30경, 노트북 실측)
회신(origin/development `914780c4`, SVN 저장소 r707·Art 최신 r706, xlsx Last Changed r705 + 메일 수정본)을 반영했고, 그 뒤 `development` 가 더 나가 **노트북 HEAD 는 `bc449d43`** 이다(914780c4 → 54318540(303 로비 간파 훈련) → bc449d43(L11 대사 3초)). 전부 fast-forward.

| 항목 | 노트북 값 | 데스크탑에서 같아야 하는 값 / 판단 |
|---|---|---|
| git HEAD | `bc449d43b799aa1692b614cfe1ab5a663e3f71d6` (development) | 같은 SHA 이거나 그 뒤 |
| Unity | 6000.3.16f1 (`a56f230f6470`), 에디터 실행 중 | 동일 |
| SVN 작업사본 `Assets/50.Art` | r706, URL `…/4_Resources/Art` | Art 최신 r706 과 같음 |
| `art-svn.json` pinnedRevision | r696 (그대로, 올리지 않음) | 핀 갱신은 접속 PC 가 한다 |
| `GameData.xlsx` | 170,402 B · SHA-256 앞 16자리 `2513113B8F3B44AF` | 이메일 본 = **"r705 + 메일 수정본 + 문구 교정"**, SVN r705 와 내용이 다름 |
| `svn status -q` | `M DataTable~\GameData.xlsx`(위 이메일 본 — 의도) · `! UI\UI_Panel\asset_manifest.json.meta`(로컬에 없음) | 둘 다 알려진 상태. **커밋 금지** (asset_manifest 는 CONTEXT.md 기존 기록 — 무해) |
| `com.community.unity-mcp` 핀 | `…Seoki2000/unity-mcp.git#930124007dd42095eed64735d6f431b5df8940f5` | 데스크탑 manifest 와 같은지 비교 |
| git 작업 트리 | `M Assets/9.ScriptableObject/Localization/StringTable.asset`(xlsx 기준 자동 갱신) · `?? 이 문서` | **StringTable.asset 커밋 금지** — SVN 접속 PC 반영을 기다린다 |
| 못 한 확인 | `python Docs/tech/check-environment.py` — 노트북에 Python 없음(`python`·`py` 둘 다 실패), 위 값은 수동 실측 | 데스크탑에서 스크립트로 한 번 더 돌려 대조해 주세요 |

### 데스크탑에서 대조할 때 (회신 요청 2)
```bash
git rev-parse HEAD                      # 노트북 bc449d43 과 비교(데스크탑이 앞서 있으면 정상)
svn info --show-item revision Assets/50.Art
sha256sum "Assets/50.Art/DataTable~/GameData.xlsx" | cut -c1-16   # 노트북 2513113b8f3b44af — 메일 보낸 파일과 같은지
git status --short                      # StringTable.asset 변경이 데스크탑에도 나오는지(내용 같아야 함)
grep unity-mcp Packages/manifest.json
```
- 노트북 `StringTable.asset` 의 diff 가 데스크탑에서 xlsx 를 같은 파일로 빌드한 결과와 **같으면** 환경 동일로 본다. 다르면 xlsx 가 다른 파일이거나 StringTable 동기화 시점이 다른 것.
- 이 노트북에서 SVN 쪽 변경(xlsx 수정 포함)이 생기면 메일로 보낸다(§3).

## 4. 노트북 작업 규칙 (임시)
- git 은 정상 사용(커밋·푸시). SVN 은 **읽기 전용 흉내도 하지 않는다**(`svn update`·`commit`·`lock` 금지 — 서버 접속 불가).
- 아트 `.meta` GUID 를 바꾸거나 `50.Art/` 를 건드리는 작업은 노트북에서 하지 않는다.
- `art-svn.json` 의 `pinnedRevision` 은 노트북에서 올리지 않는다(실제 받은 리비전을 서버에서 확인할 수 없다). 핀 갱신은 SVN 접속되는 PC 가 한다.
