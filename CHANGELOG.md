# 변경 이력
## 0.3.1.0 — 2026-09-26 · 횡단 안전·Python 제어 및 재접속 검증

- 센서 관측 운동에 기반한 횡단 정지와 연속 footprint 충돌 검사, 자체 RRT 후보, 가감속/회전 시간 궤적 및 동적 장애물 prefix 검사를 추가했다. RRT 후보는 실행 불가 상태를 유지한다.
- 선택적 Python 속도/회전 명령과 Unity actuator를 연결하고 명령 만료 제동, actor 재생성 시 replay 거부, endpoint/반전 제동을 보완했다.
- 실제 socket 중단 실험에서 발견한 재접속 snapshot 번호 초기화 결함을 수정했다. 서비스별 run ID와 연결 간 단조 증가 snapshot 번호를 사용한다.
- 이유: 센서 기반 안전과 Python 제어의 합성 통합 범위를 확장하고 통신 중단 시 정지·회복을 검증하기 위해서다.
- 호환성/마이그레이션: 프로젝트 버전 0.3.1.0. schema_version=3 및 map_version은 유지한다. controlCommands는 선택 필드이며 새 기능은 서버/클라이언트 동시 배포와 명시적 synthetic opt-in이 필요하다. 기존 씬은 opt-out이며 데이터 마이그레이션 없음.
- 검증: Python/MapData 242개 및 Ruff 통과. Unity component PlayMode 12/12, socket 중단→제동→재접속→횡단 정지/회복→승객 완료→actor 재생성 통합 1/1 통과. 실행별 XML과 소스 hash는 artifacts/validation에 보존했다. 커밋 준비에서는 Python/Ruff와 diff 검사를 재실행하며 Unity 증거는 각 실행 당시 소스 기준이다.
- 미검증/한계: 최신 기능의 Player/Android/원본 씬, 서버 프로세스 재시작, 관측 free-space 보장·RRT 실제 실행, 실지도·제원/동역학 검증, 연속 실제 Physics 충돌 판정, 성능/50-client 부하 및 전체 MVP는 미완료다. sampled clearance는 연속 충돌 안전 보장이 아니다. 기존 Python 의존성 deprecation 경고가 남는다.

## 0.3.0.0 — 2026-09-26 · 합성 보행자·Radar·관측 추적 및 구역 폐쇄

- 차량 telemetry의 PC host 소유권과 actor 재생성 tick 연속성을 명시하고 모바일 완료 요청의 누락된 차량 참조를 정리했다.
- Unity 전용 보행자 이동/population, Raycast Radar range-rate, 관측 대상 ID·취득 시각·센서 pose 전송과 가림·자기 차량 제외·미터 단위 검사를 추가했다.
- Python Radar 접근 정지, 원형 TTC 계산 기반, 관측 보행자 중복 제거·운동 추정·역행 시각 거부, zone 상태 정책과 명시적 폐쇄/재개를 추가했다. 운동 추정과 zone 관측 정책은 완전한 자동 제어 연결 전 단계다.
- 이유: 합성 시연에서 관측과 서비스 상태의 일관성을 확보하고 센서 기반 보행자·안전·혼잡 처리를 단계적으로 검증하기 위해서다.
- 호환성/마이그레이션: 새 클라이언트의 추가 센서 필드는 이전 strict 서버에서 거부되므로 서버와 클라이언트를 함께 갱신한다. 이에 프로젝트 B를 증가시켰다. schema_version=3 유지; metadata 없는 이전 관측은 수신 가능하지만 운동 추정 대상에서 제외한다. safety 설정에 radar_approach_horizon_s가 필요하다. 새 zone 지도는 별도 합성 fixture다.
- 검증: 커밋 준비 시 Python/MapData 전체 178개 및 Ruff 통과. 기존 실행 증거에 Unity EditMode 17/17, 이동 보행자·Radar·zone 폐쇄의 PlayMode 통합, 300명 population lifecycle 통과가 포함된다. 실행별 XML과 source hash는 artifacts/validation에 보존한다.
- 미검증/한계: 이번 커밋 준비에서 Unity는 재실행하지 않았다. 기존 Unity 결과는 각 실행 당시 소스 기준이며 최신 전체 변경의 Player/Android/원본 씬 검증을 의미하지 않는다. 실제 캠퍼스 graph·Stop·zone, 관측 밀도 통합, full TTC/RRT·footprint·물리 제동, 성능·50-client 부하와 전체 MVP는 미완료다. Python 의존성 deprecation 및 기존 Unity allocation 경고가 남는다.

## 0.2.4.0 — 2026-09-26 · 센서 안전 분리와 합성 Physics 통합 검증

- 센서 안전 정책·평가를 service에서 분리하고 다중 센서의 invalid/stale 검사 순서와 비정상 수신 시각 처리를 보완했다.
- Unity follower의 로컬 속도 상한과 Awake 이전 Rigidbody 확인을 보완하고, 경로 표시를 서버의 불변 전체 polyline과 일치시켰다.
- 격리된 Unity follower·원본 차량 prefab·Python 연동 테스트 실행기와 합성 3-node fixture, 검증 artifact 및 MVP 단계 기록을 추가했다.
- 이유: 서비스 책임을 분리하고 실제 Unity Physics/Raycast/WebSocket 왕복으로 합성 운송 흐름을 검증하기 위해서다.
- 호환성/마이그레이션: 프로젝트 버전 0.2.4.0; schema_version=3 및 기존 map_version 유지. 새 지도는 테스트 전용 합성 fixture다. 데이터 마이그레이션 없음.
- 검증: Python/MapData 111개 및 Ruff 통과. Unity follower PlayMode 7/7, 원본 prefab EditMode 11/11, 승객·화물/장애물 정지·재출발 통합 시나리오의 PlayMode와 Windows 테스트 Player 통과. T03 110 OD × 20회 경로 비용 일치. 실행별 소스 hash와 XML은 artifacts/validation에 보존했다.
- 한계: Player/follower 증거는 각 실행 당시 소스 기준이며 후속 Rigidbody 초기화·경로 표시 수정은 prefab EditMode에서 검증했다. 원본 전체 씬/Android/다중 Physics 차량/실제 지도/성능·부하/완전한 안전 기능은 미검증이다. Player allocation-lifetime 및 Python 의존성 deprecation 경고가 남는다. 전체 MVP는 미완료다.

## 0.2.3.0 — 2026-09-25 · 합성 운송·센서·배차 기반 확장

- 합성 RoadGraph의 경로 비교와 요청→픽업→하차 흐름을 확장하고 3대 차량의 Greedy/Hungarian 배차, 시간대 혼잡 prior와 우회 정책을 추가했다. 배차 비교 artifact의 비용행렬은 수기 합성값이다.
- Python WebSocket 위치·센서 관측 검증과 제한적 정지거리 safety gate를 연결했다. Unity 합성 미리보기에는 차량 wrapper prefab, 경로 표시·추종, 위치·2D Raycast LiDAR 송신과 PC/모바일 표시를 추가했다.
- OSM 도로 후보와 현장 검토표, 합성 graph 시각화 prefab 및 관련 문서를 추가했다. 후보 구간은 모두 미검증·주행 불가 상태다.
- 호환성/마이그레이션: 프로젝트 버전을 0.2.3.0으로 동기화했다. WebSocket schema_version=3과 합성 map_version은 유지한다. 새 센서/경로 필드와 설정 파일을 사용하는 클라이언트는 같은 버전의 서버와 함께 실행해야 한다.
- 검증: 이전 작업 기록의 Python backend 93개·MapData 9개 테스트와 Ruff 통과를 확인했다. 이번 커밋 준비에서는 Python compileall이 통과했다. 현재 환경에 pytest가 없어 전체 suite는 재실행하지 못했다.
- 미검증/한계: Unity Test Runner/Player 왕복, 실제 캠퍼스 운송 그래프·Stop, TTC/RRT·물리 제동, 보행자 통합 및 성능·부하 측정은 완료되지 않았다.

## 0.2.2.0 — 2026-09-24 · 합성 서버-클라이언트 주행 프로토타입

- Python 합성 6-stop graph, Dijkstra/A* 비교, 단일 차량 V01의 경로 이동·요청 상태 전이와 Unity schema-v3 WebSocket 요청 생성/취소를 연결했다. Unity 모바일에 네트워크 데이터 소스와 요청 UI를 추가했다.
- 소유자/명령 멱등성, ACK 복사, 요청별 route projection, 차량 용량·활성 상태 검증, edge 속도 제한 등 안정성 처리를 추가했다. 재사용 가능한 노드·edge 및 캠퍼스 오브젝트는 Prefab으로 관리하는 협업 규칙을 유지한다.
- 호환성/마이그레이션: 프로젝트 버전은 0.2.2.0으로 동기화했다. WebSocket schema_version=3은 유지했다. 실제 지도·Unity Physics 주행, 인증·자동 재접속은 미구현이다.
- 검증: Python/ASGI 테스트 20개 통과, graph route-compare 30/30 경로 비용 일치, Python compileall 및 `git diff --check` 통과. Starlette/httpx deprecation warning 1건. Unity 재컴파일은 수행되지 않아 C# 변경 검증은 미완료다.

## 0.2.1.1 — 2026-09-24 · 지도·구현 현황 및 UI 디자인 기준 정리

- 지도 데이터 출처, 재현 정보, 검사 범위, 한계와 운송 서비스 사용 가능 여부를 `Docs/MapResearch/PLAN.md` 및 `Docs/implementation_status.md`에 구분해 기록했다. OSM/DSM 후보·시각 Terrain을 검증된 차량/보행 그래프나 접근 가능한 Stop으로 간주하지 않는다.
- 현재 M0 클라이언트와 Python 합성 fixture 서비스, M1~M6 미완료 단계를 구분하고 과거 검증 결과가 재실행된 것으로 오해되지 않게 현황을 갱신했다.
- `f97fa11`에서 추가된 세 차량 프리팹을 M1 주행 구현의 시각 에셋 준비물로 기록했다. 프리팹과 차량 동역학·제원·Collider 검증을 구분했다.
- 공통/PC/모바일 UI 디자인 문서를 구현 명세와 연결하고, 서버 명령이 필요한 population 조절 및 현재 요청 상태 계약에 없는 모바일 하차 요청은 확정 기능으로 간주하지 않도록 검토 결과를 기록했다.
- `UI_DESIGN_SYSTEM.md`, `PC_CLIENT_DESIGN.md`, `MOBILE_CLIENT_DESIGN.md`를 확인하고 구현 명세에서 시각 설계 참조로 연결했다. 서버 명령이 필요한 population 조절과 현재 계약에 없는 모바일 하차 요청은 확정 기능으로 간주하지 않는다.
- `AGENTS.md`에 모든 커밋의 버전 증가, 커밋 본문(description) 요건 및 주석 태그 규칙을 명시했다.
- 버전 정합성: VERSION, Unity bundleVersion, Python package/API, README 및 현황 문서를 0.2.1.1로 맞췄다. schema_version, map_version, 과거 문서/fixture 이력은 별도 버전으로 유지했다.
- 호환성/마이그레이션: 통신 schema_version과 DTO 계약 변경 없음. 런타임 동작 변경 없음.
- 검증: 문서 링크·버전 문자열 및 `git diff --check`를 확인했다. Python/Unity 테스트는 실행하지 않았다.

## 0.1.6.0 — 2026-09-23 · Python 서버 M0 기반

- FastAPI/Pydantic 기반 `backend/`를 추가했다. 합성 Landmark·Stop·차량 fixture를 바탕으로 요청 검증, 이동지원 Stop 선택, 차량 능력 조건의 단순 배차, 요청 취소와 command_id 멱등 처리를 제공한다.
- 서버는 기본적으로 `127.0.0.1:8765`에서만 수신한다. Unity WebSocket transport, 실제 지도/Stop 검증, A*·RRT·배터리·혼잡·센서·Physics 연동은 포함하지 않는다.
- 호환성: Unity 클라이언트의 schema_version=3은 변경하지 않았다. VERSION·Unity bundleVersion·README·구현 현황을 0.1.6.0으로 동기화했다.
- 검증: Python `pytest backend/tests` 4/4 통과, Ruff 검사 통과. Unity 재컴파일, Player/실기기, 성능 측정은 이번 변경 범위에서 수행하지 않았다.

## 0.1.5.0 — 2026-09-22 · Fixture PC·모바일 uGUI 화면

- 기존 IMGUI 개발 HUD를 Input System EventSystem 기반 uGUI로 교체했다. 화면은 런타임에 역할 Scene의 Presenter가 만들며 authoritative 상태는 기존 WorldStateStore가 계속 소유한다.
- PC에는 상단 버전/연결 상태, 차량·요청 집계와 목록, Campus 3D 영역, 선택 차량·Zone inspector, 타임라인과 fixture 제어를 배치했다.
- 모바일에는 Safe Area 대응 헤더, Campus 뷰, 요청 상태·출발/목적·Stop·합성 ETA·배정 차량·진행 단계·상태/사유 안내를 포함한 하단 시트를 배치했다. 다른 요청과 Zone 데이터는 계속 projection에서 제외된다.
- PC·모바일의 사용자 안내, 상태, 버튼 문구를 한국어로 제공한다. Fixture·Zone·Stop·ETA·ID처럼 프로젝트에서 기술 용어로 쓰는 표현은 의미가 어색해지지 않도록 영어를 유지했다.
- 지연 로드되는 Campus 카메라를 감지해 Canvas를 Screen Space Camera로 연결하고, 화면당 Canvas/EventSystem을 하나만 생성한다. snapshot 변경 때만 역할 본문을 갱신하고 연결/stale 표시는 10Hz로 제한한다.
- 검증: Unity 6000.3.21f1 컴파일, PC 1440×900 및 모바일 역할 720×1280 시각 확인. 실제 uGUI 버튼으로 pause/restart/disconnect/reconnect 동작을 확인했다. 최종 자동 테스트 결과와 Player/실단말 제한은 구현 현황에 기록한다.
- 호환성: schema_version=3과 map_version=synthetic-ui-v1 유지. 실제 호출 입력·WebSocket·차량/경로 3D overlay·Player 빌드·실단말 성능은 포함하지 않는다.

## 0.1.4.0 — 2026-09-22 · 합성 클라이언트 재생과 개발 HUD

- IClientDataSource 경계와 45초 합성 FixtureClientDataSource를 추가했다. 0.05초 기록 tick/최대 10Hz snapshot, 일시정지 heartbeat, 수신 중단·복구, 새 run 재시작을 제공한다. 실제 배차·주행·네트워크 알고리즘이 아니다.
- Bootstrap의 단일 runtime host를 역할별 PC/모바일 Presenter에 연결했다. 영어 IMGUI 개발 HUD는 fixture/버전/연결·stale/요청·차량 상태를 표시한다. 개발용 로컬 재생 버튼은 Editor/Development Build에만 표시한다.
- 모바일 snapshot 생성 단계부터 다른 요청·차량·무관 Stop/route·Zone을 제외한다. PC의 관측·EMA unknown과 합성 prior를 구분한다.
- 기존 4개 Bootstrap/역할 씬을 Editor API로 연결하고 재실행 가능한 Configure Fixture HUD 메뉴를 추가했다. CampusTerrain·공유 지도/에셋·빌드 프로필은 변경하지 않았다.
- 검증: Unity 6000.3.21f1 컴파일 성공, EditMode 54/54 및 PlayMode 1/1 통과. PC·모바일 역할 Play smoke에서 표시/소유권·stale/복구·재시작과 구독 해제를 확인했다. Android/Windows Player 빌드·실단말·서버 통합·성능 목표는 미검증이다.
- 호환성: schema_version=3 유지, 독립 합성 map_version=synthetic-ui-v1. JSON/WebSocket·사용자 호출 화면·지도 차량 렌더링은 아직 없다. 독립 fixture 앱 간 실시간 동기화를 제공하지 않는다. VERSION·bundleVersion·현재 문서를 동기화했다.

## 0.1.3.1 — 2026-09-22 · Visual Studio 환경 파일 제외

- 로컬에서 자동 생성되는 루트 `.vsconfig`을 Git 추적 대상에서 제외했다.
- VERSION·Unity bundleVersion·현재 문서 머리말을 0.1.3.1로 동기화했다. 코드·통신 schema_version·map_version은 변경하지 않았다.
- 검증: `git check-ignore -v .vsconfig`으로 ignore 규칙 적용을 확인했다.

## 0.1.3.0 — 2026-09-22 · 클라이언트 데이터 모델과 상태 저장소

- 차량·요청·이동지원·랜드마크·Stop·경로·Zone·snapshot·이벤트/명령 응답의 불변 C# 읽기 모델 및 enum을 추가했다. Domain 어셈블리의 UnityEngine 의존을 제거했다.
- WorldStateStore에 완전한 snapshot 교체, ID 조회, 중복/과거 sequence 무시, run 변경/이전 run 패킷 거부, 참조 검증, 모바일 구독 범위 검사, 실시간 1초 stale 판정을 추가했다.
- Python local metric 좌표↔Unity 축 변환과 heading 변환을 추가했다. 음수 좌표와 미확인 측정값을 보존하며 NaN/Infinity를 거부한다.
- 호환성: 기존 통신 schema_version=3 유지. JSON 직렬화·서버 계약 fixture·delta 이벤트·WebSocket 연결은 미구현이며 모델을 직접 JsonUtility에 전달하지 않는다. 사용법/제약은 Docs/ClientUI/DATA_CONTRACT.md에 기록했다.
- 검증: Unity 6000.3.21f1 컴파일 성공, InhaExpress.Client.Tests.EditMode 34/34 통과(0 실패, 0 생략). 실제 Player 빌드·실기기·서버 통합은 미수행. 기존 외부 Vegetation 셰이더 오류 이력은 해결 범위 밖이다.
- VERSION·Unity bundleVersion·README·클라이언트 문서를 0.1.3.0으로 동기화했다.

## 0.1.2.0 — 2026-09-22 · PC·모바일 클라이언트 실행 골격 추가

- 공통 `CampusWorld`와 역할별 `PC_Operator`, `Mobile_Passenger` 씬을 추가하고 PC·모바일 Bootstrap에서 Additive로 조합하도록 구성했다.
- 기존 `CampusTerrain`을 복제하지 않고 공통 월드에서 Additive로 불러오며, Domain·Networking·Presentation·PC·Mobile 어셈블리 경계를 추가했다.
- Windows와 Android Build Profile을 분리해 각 플랫폼이 올바른 Bootstrap과 역할 씬으로 시작하도록 구성했다.
- VERSION·Unity bundleVersion·README·클라이언트 구현 문서를 0.1.2.0으로 동기화했다. 통신 schema_version과 Unity Editor 6000.3.21f1은 변경하지 않는다.
- 검증: Unity Editor C# 컴파일 성공, 신규 씬 참조와 `.meta` 확인, Windows/Android 프로필별 씬 순서 확인. 실제 Player 빌드·Play Mode UI·서버 통합은 미수행이며 기존 외부 Vegetation Shader 오류는 남아 있다.

## 0.1.1.1 — 2026-09-18 · 씬 보행 연결 현황과 생활관 제한사항 기록

- 제3생활관·정석학술정보관·본관·학생회관의 일반/이동지원 출입구와 보행 연결 authoring 결과를 지도 계획에 반영했다.
- 제1·2·3생활관의 실제 외곽, 출입문, 도로·횡단 연결이 아직 확정되지 않았음을 명시했다. 현재 연결은 시각화 초안이며 차량 Stop·이동지원 경로 승인이 아니다.
- 대표 시설 커버리지, 인하대역 이동지원 출입구, 학생회관 경사, 전체 도로·보행 graph, PC/모바일 UI와 Python 서버가 남은 작업임을 정리했다.
- VERSION·Unity bundleVersion·문서 머리말을 0.1.1.1로 동기화했다. Unity Editor 6000.3.21f1과 통신 schema_version은 변경하지 않는다.
- 검증: 최신 walkway-surface-gradients.csv, 생활관/시설 inventory, 씬 캡처와 계획 문서 대조. 실제 접근성 인증·성능 측정·서버 통합은 미수행이다.

## 0.1.1.0 — 2026-09-17 · PC·모바일 UI 상세 설계 통합

- 사용자 제공 34절 UI 명세를 보존하고 모바일 M01~M13, PC P01~P08의 화면별 적용·검증 계획을 추가했다.
- 최신 제작 순서를 씬 검증 → UI 목업·일부 기능 → Python 서버 연결로 정리했다. 목업과 실제 서버 상태를 구분하고 서버 권위·접근성·요청 소유권 제약은 유지한다.
- 유의미한 설계 보완으로 마이너 버전을 증가시켰다. VERSION·문서 머리말·Unity 앱 버전을 0.1.1.0으로 동기화한다. Unity Editor 6000.3.21f1과 통신 schema_version은 변경하지 않는다.
- 검증: 첨부 원문 보존, 13개 모바일 화면/8개 PC 패널 매핑, 문서 링크·버전 동기화 검사. UI 구현·실기기 실행·서버 통합 검사는 아직 미수행이다.
- 이번 커밋은 명세·계획·버전 관리 범위다. 진행 중인 씬·에셋 변경 전체의 완료 릴리스가 아니다.

## 0.1.0.0 — 2026-09-17 · 맵 수정 전 체크포인트

- 지도 개선 계획과 공개 고도/확장 OSM 자료 확보, 재현 가능한 높이 데이터 가공 추가.
- 사용자 확인에 따라 기숙사를 제1~3생활관으로 정정하고 역을 인하대역으로 확정.
- 사용자 요청으로 기존 설계/작업 기준선 0.2.0.0/0.2.1.0을 이번 체크포인트에서 0.1.0.0으로 재설정했다. 일반 증가 규칙의 일회성 예외다.
- VERSION·문서·가공 manifest·Unity 앱 bundleVersion을 0.1.0.0으로 일치시켰다. Unity Editor는 6000.3.21f1 유지. 과거 빌드 변경이나 새 앱 빌드는 수행하지 않았다.
- 새 지도는 기존 근사 좌표 대신 원점을 공유하는 AEQD를 사용한다. 기존 씬과 혼용 전 재투영/정렬 검증 필요. 통신 schema 변경 없음.
- 검증: 공개 원본 다운로드 hash, GeoTIFF 읽기/재투영, 결측 검사, 3점 좌표 왕복, RAW 양자화 왕복, 비교 이미지 확인. Unity 씬 적용/실행 테스트는 미수행.

## 0.2.0.0 — 설계 기준선

- 네 자리 버전 규칙을 도입한 기존 AGENTS 설계 기준선. 구현 완료 버전이 아님.
