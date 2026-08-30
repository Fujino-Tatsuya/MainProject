# 새 환경 셋업 — git 만으로는 부족하다

> 2026-08-30 작성. 검사: `python Docs/tech/check-environment.py`

**한 줄: 이 프로젝트는 git 저장소 하나가 아니다.** 코드·씬·프리팹은 git 에 있고
**아트는 SVN 에 있다.** 아트를 안 받아도 Unity 는 그냥 열리기 때문에, 셋업이 틀렸다는 것을
아무도 알려주지 않는다.

실측(2026-08-30): SVN 이 소유한 GUID **1,105개**를, **git 이 추적하는 에셋 392개**가
**23,097번** 참조한다. 로딩·로비·맵·VFX 씬과 맵 프리팹 수백 개가 여기 걸려 있다.
아트 없이 열면 그 참조가 전부 null 이 되는데 **콘솔은 조용하고 `git status` 도 깨끗하다.**

---

## 필요한 것

| | |
|---|---|
| Unity | **6000.3.16f1** (`ProjectSettings/ProjectVersion.txt` 가 정본) |
| git | 게임 레포 접근 |
| SVN 클라이언트 | 아트용. 예: [SlikSvn](https://sliksvn.com/) — `svn` 이 PATH 에 있어야 검사 스크립트가 리비전을 대조한다 |
| Node.js | **MCP 도구를 쓸 때만.** 게임 편집·빌드에는 불필요 |

---

## 1. 게임 레포 clone

```bash
git clone https://github.com/Fujino-Tatsuya/MainProject.git C:/Unity/MainProject
```

경로는 자유다. 아래 명령의 `C:/Unity/MainProject` 를 본인 경로로 바꿔 읽으면 된다.

## 2. 아트(SVN) 체크아웃

리비전은 **`Docs/tech/art-svn.json` 의 `pinnedRevision` 을 따른다.** 그게 지금 git 커밋이
전제하는 아트다. 최신(HEAD)을 받으면 같은 git SHA 인데 다른 아트를 쓰게 된다.

```bash
svn checkout -r 286 https://svna.gameinjae.kr/svn/GA7thFinal_VeyTrace C:/svn/GA7thFinal_VeyTrace
```

작업사본 전체가 약 **600 MB** 다(`4_Resources/Art` 만 해도 그렇다).

⚠️ **작업사본을 OneDrive·Dropbox 같은 동기화 폴더 안에 두지 말 것.**
`.svn/wc.db` 가 동기화되면 작업사본이 깨진다. 그래서 이 PC 는 `C:\svn\` 에 둔다.

## 3. 정션 걸기

Unity 는 `Assets/50.Art` 에서 아트를 찾는다. 그 자리에 SVN 작업사본의 `4_Resources/Art` 를
연결한다. **cmd 를 관리자 권한으로 열 필요는 없다**(정션은 심볼릭 링크와 달리 권한이 필요 없다).

```cmd
mklink /J "C:\Unity\MainProject\Assets\50.Art" "C:\svn\GA7thFinal_VeyTrace\4_Resources\Art"
```

PowerShell 이면:

```powershell
New-Item -ItemType Junction -Path "C:\Unity\MainProject\Assets\50.Art" -Target "C:\svn\GA7thFinal_VeyTrace\4_Resources\Art"
```

`Assets/50.Art.meta` 는 git 에 있으므로 **만들지 말 것.** 폴더만 연결하면 된다.

> 정션 대신 SVN 을 `Assets/50.Art` 에 직접 체크아웃해도 Unity 는 동작한다. 다만
> `.gitignore` 가 그 경로를 제외하고 있어야 하고(이미 84행에 있다), 아트와 코드를
> 한 폴더에서 두 VCS 로 다루게 되어 헷갈린다. 정션을 권한다.

## 4. 검사

Unity 를 열기 **전에** 돌린다.

```bash
python Docs/tech/check-environment.py
```

Unity 버전 · 정션 · SVN URL·리비전 · 아트 GUID 가 실제로 해석되는지 · MCP 패키지 출처를 본다.
`[FAIL]` 이 있으면 그 상태로 열지 말 것 — 게임 내용이 틀어진다.

## 5. Unity 열기

Unity Hub 에서 `6000.3.16f1` 로 연다. 첫 임포트는 오래 걸린다.
콘솔에 뜨는 결손 참조 중 **이미 조사가 끝난 것들**이 있다 — [CONTEXT.md](../../CONTEXT.md) 의
"예상되는 결손 참조" 표를 먼저 볼 것. 새 버그로 오진하지 말라고 적어 둔 표다.

## 6. (선택) MCP 도구

게임 작업에는 필요 없다. AI 도구를 쓸 때만 한다.

1. Node.js 설치
2. Unity 에서 **Window > MCP Server** 를 연다
3. **Copy Config to Clipboard** 를 눌러 MCP 클라이언트 설정에 붙여 넣는다

창이 넣어 주는 경로는 `Bridge/mcp-bridge-launcher.js`(런처)다. **브릿지(`mcp-bridge.js`)를
직접 등록하지 말 것** — 그 경로에는 패키지 해시가 들어 있어서, 패키지 핀을 올리면
`Library/PackageCache/...@해시/` 폴더 이름이 바뀌어 조용히 낡은 사본을 돌리거나 사라진
경로를 가리킨다. 런처는 실행 시점에 브릿지를 다시 찾으므로 그 문제가 없다.

창의 **Resolved Package** 줄이 지금 실제로 도는 패키지가 어디서 왔는지 말해 준다.

---

## 아트를 새로 받았을 때

아트 리비전을 올리고 그 상태로 git 에 커밋한다면, **`Docs/tech/art-svn.json` 의
`pinnedRevision` 과 `pinnedAt` 을 같은 커밋에서 갱신할 것.** 안 하면 다음 사람이
핀을 믿고 옛 아트를 받는다.

```bash
svn update C:/svn/GA7thFinal_VeyTrace
svnversion C:/svn/GA7thFinal_VeyTrace     # 이 수를 pinnedRevision 에 적는다
python Docs/tech/check-environment.py         # expected 수치도 같이 갱신할지 본다
```

---

## 왜 이렇게 되어 있나 (함정 모음)

- **`Assets/50.Art` 는 정션이고, 도구마다 보이지 않는다.** `find -type f` 와 Node 의
  `Dirent.isDirectory()` 는 정션을 안 따라간다. GUID 를 훑는 스크립트를 쓸 때
  `Assets/50.Art` 를 명령줄에 직접 주지 않으면 `.meta` 1,105개가 통째로 빠진다.
  (python 의 `os.walk` 는 따라간다.)
- **동명 에셋 중복.** 과거에 같은 이름의 에셋이 git 과 SVN 양쪽에 존재해 작업자가
  고아 사본을 편집한 사고가 있었다. 원인과 처방은
  [art-vcs-duplication-handoff.md](art-vcs-duplication-handoff.md).
- **`Packages/manifest.json` 에 `skip-worktree` 가 걸려 있을 수 있다.** MCP 패키지를
  로컬에서 개발하는 사람은 커밋된 git 핀 대신 `file:` 경로를 쓰는데, 그 차이가
  `git status` 에 **안 나온다.** 검사 스크립트 4번이 그것을 드러낸다. 새로 clone 한
  사람에게는 해당 없다(플래그는 로컬 인덱스 상태라 clone 에 안 따라온다).
- **아트 리비전은 git 이 강제하지 못한다.** `Docs/tech/art-svn.json` 은 핀이지 잠금이 아니다.
  검사 스크립트가 경고할 뿐, SVN 을 되돌려 주지는 않는다.
