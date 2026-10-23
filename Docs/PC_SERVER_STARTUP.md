# PC 클라이언트와 로컬 서버 동시 실행

프로젝트 0.4.6.0 · 2026-10-23

## 유지한 Additive 구조

PC 진입 씬은 `PC_Bootstrap`이다. Bootstrap은 `CampusWorld`와 `PC_Operator`를 Additive로 로드하고, CampusWorld의 기존 loader가 `CampusTerrain`을 Additive로 로드한다. 새 씬을 만들거나 CampusTerrain을 복제/평탄화하지 않는다.

```text
PC_Bootstrap (세션/서버 프로세스 소유, DontDestroyOnLoad)
├─ CampusWorld                 [Additive]
│  └─ CampusTerrain            [Additive, 기존 노드·허브]
└─ PC_Operator                [Additive]
```

PC Bootstrap은 fixture 대신 실제 서버 지도 모드를 사용한다. 기존 fixture 구현과 모바일 fixture는 보존한다. Editor의 Additive 로딩은 기존 씬 경로로 `LoadSceneAsyncInPlayMode`를 사용하여 전역 Build Settings가 꺼져 있어도 실행할 수 있다. Windows Player는 기존 Windows 빌드 프로필의 네 씬 목록을 사용한다.

## 실행과 종료

Unity에서 `PC_Bootstrap.unity`를 열고 Play하면 서버 실행 명령을 별도로 입력하지 않아도 된다. PC가 먼저 loopback 8765의 `/v1/desktop/info`를 확인한다.

- 호환 service ID 및 desktop/map schema가 있는 서버는 그대로 재사용한다. 프로젝트 버전은 기록하며, 실제 지도 hash·좌표는 기존 지도 클라이언트가 추가 검증한다.
- 서버가 없으면 숨겨진 Python 프로세스를 한 번 시작하고 준비를 기다린 뒤 Additive 씬을 로드한다.
- 다른 서비스가 포트를 사용하면 해당 프로세스를 종료하거나 교체하지 않는다. 시작 실패 시 원인과 상태를 표시하며 fixture로 자동 대체하지 않는다.
- PC가 직접 실행한 서버만 PC 종료 시 stdin으로 정상 종료를 요청한다. 부모가 비정상 종료되어 pipe가 닫혀도 Python은 종료한다. 재사용한 서버는 PC 종료 후에도 유지한다.
- 중복 Bootstrap은 기존 singleton 규칙으로 거부한다. CampusWorld/CampusTerrain/모바일은 Python 프로세스를 추가로 시작하지 않는다.

웹은 `http://127.0.0.1:8765/demo/`에서 같은 session을 표시한다. 현재는 읽기 전용 연결이며 운송 요청·시간 진행·차량 조작을 자동으로 실행하지 않는다. 직접 CampusTerrain만 Play하는 디버그 흐름은 서버를 소유하지 않으므로 기존 수동 서버 또는 PC Bootstrap을 사용한다.

## Windows 실행 파일

Windows64 빌드가 성공하면 `DesktopServerBuild`의 postprocessor가 실행 파일 옆 `CampusServer/`에 backend, 오프라인 지도, manifest가 요구하는 원본과 웹/라이선스 파일을 복사한다. 이 작업 환경의 로컬 의존성이 있으면 함께 복사하며 원본 자료를 다시 작성하지 않는다. Python 런타임 자체는 포함하지 않는다.

Python 3.11 이상 호환 환경이 필요하며 현재 검증 환경은 Windows/Python 3.14.4다. `python.exe`가 PATH에 있어야 하며 다른 Python을 쓰려면 `CAMPUS_SIM_PYTHON`을 설정한다. 별도 서버 폴더를 쓰려면 `CAMPUS_SIM_SERVER_ROOT`를 repository 또는 동일 구조의 서버 bundle로 설정한다. 프로젝트 Editor에서는 기본 root가 repository이고 `.venv`, `backend/.venv`, PATH 순으로 Python을 선택한다.

다른 PC에서 의존성을 포함하지 않은 bundle을 사용할 때는 backend 요구 의존성을 먼저 설치해야 한다. 자동 실행은 dependency 설치, Python 설치, 방화벽 변경이나 외부 공개를 수행하지 않는다. Mobile은 로컬 서버를 실행하지 않으며 별도 PC/LAN 서버 설정 단계가 필요하다.

수동 패키징:

```powershell
python scripts/package_pc_server.py --destination <player-folder>/CampusServer --include-local-dependencies
```

## 실제 동시 실행 검증

첫 실제 실행에서는 PC Bootstrap이 Python PID 33628을 자동 시작했다. Unity와 독립 HTTP 조회 및 브라우저의 `/v1/desktop/info`가 모두 run ID `9330cbb21142411d986c0feaf8e0401f`를 반환했다. 웹 관제의 '로컬 서버 연결됨', 세 허브, 00:00:00 일시정지 상태를 확인했다. 운송 요청과 차량 조작은 하지 않았다.

런타임에서 PC_Bootstrap/CampusWorld/CampusTerrain/PC_Operator 네 씬이 모두 로드됐고, 지도 클라이언트는 하나였다. 187개 도로 노드·3개 허브·14개 Stop 후보의 실제 Transform X/Z를 서버 projection과 0.01m 이내로 대조했다. 다른 launcher가 같은 서버를 재사용한 뒤 파괴되어도 서버가 유지됐고, 중복 Bootstrap은 거부됐다. 소유 PC를 종료한 뒤 Python도 정상 종료됨을 검사했다.

첫 동시 실행의 PlayMode 3개는 모두 통과했다. [실행 증거](../artifacts/desktop_live_0.4.6.0/first-live-run.json), [브라우저에서 읽은 identity](../artifacts/desktop_live_0.4.6.0/browser-session.txt), [웹 관제 화면](../artifacts/desktop_live_0.4.6.0/web-dashboard.png)을 보관한다. 최종 검증에서도 실제 프로세스 통합을 포함한 PlayMode 3개가 통과했다.

Windows 개발 Player 빌드가 성공했고 `.utmp/pc-client/InhaMobil.exe`를 실제 실행했다. 이 실행 파일이 Python PID 5336을 자동 시작했으며 OS에서 부모 PID 18196(검증 Player)을 확인했다. Player 연결 로그와 독립 브라우저 identity가 run ID `08f8305654a9455d8b2168b758b6ac65`로 일치했다. 네 Additive 씬 이름이 Player 로그에 모두 기록됐고 웹 시간은 0초/paused=true, 요청은 0건이었다. 실행 파일 옆 CampusServer의 Python 3.14.4 의존성과 지도/source hash 검증을 통과한 상태다.

증거 저장 후 이 작업에서 시작한 headless Player만 종료했고, Python도 stdin EOF로 종료됨을 확인했다. 기존/다른 서버를 종료한 것은 아니다. 결과물은 로컬 검증용 Windows 개발 빌드이며 Git 커밋에는 대형 실행 파일을 포함하지 않는다.

최종 결과: Python 212개, Unity EditMode 57개/PlayMode 3개 통과. Ruff는 backend src/tests 및 패키징 스크립트 48파일 검사/포맷을 통과했다. Python의 기존 Starlette deprecation 경고 1개가 있다. [최종 결과 요약](../artifacts/desktop_live_0.4.6.0/summary.json), [Windows 실제 세션](../artifacts/desktop_live_0.4.6.0/windows-player-live.json), [부모 종료 검증](../artifacts/desktop_live_0.4.6.0/windows-player-shutdown.json) 및 원본 XML을 보관한다.

실제 Windows 실행은 -batchmode -nographics로 검증했다. Unity 화면의 실제 렌더링/FPS를 측정했다고 주장하지 않는다. 기존 Annyoung Car의 누락 스크립트 경고와 TempJob 임시 메모리 수명 경고가 관찰됐으며 차량 조작 범위 밖 원본을 임의 수정하지 않았다. 빌드가 자동 변경한 URP prefilter/runtime settings, GraphicsSettings, batching/analytics 설정은 원래 값으로 복원했다.

재현 시 backend 의존성을 준비하고 `CAMPUS_SIM_LIVE_TEST=1` 환경에서 Unity PlayMode 테스트를 실행한다. 이 opt-in 테스트는 비어 있는 로컬 8765 포트를 요구한다. 일반 fixture 회귀에서는 해당 실제 프로세스 통합 테스트를 건너뛴다. `CAMPUS_SIM_BROWSER_HOLD=1`은 검증자가 브라우저를 확인할 때만 사용하는 임시 대기 옵션이다.

차량 제어/센서/WebSocket/실제 운송 완료·모바일 실행·FPS·실측 접근성 검증은 포함하지 않는다. Terrain 표시 높이를 쓴 4개 마커의 기존 확인 항목은 [지도 안내](CAMPUS_SERVER_MAP.md)에 남아 있다.
