# 변경 이력

## 0.4.4.0 — 2026-10-23 · main/decision 통합 및 지도 해시 재현성 수정

- 작업: main의 WheelCollider 차량 제어·Anti-Roll·프리팹/씬과 decision의 지도·경로·배차·배터리·웹 시연을 이력을 보존해 병합했다. 통합 브랜치는 codex/integrate-decision-main이다.
- 이유: 서로 분리된 차량 물리 기반과 Python 알고리즘을 한 검토 기준선에서 보존하고, 새 checkout의 지도 해시 오류를 해결한다.
- 지도 manifest schema_version=2/hash_format=sha256-utf8-lf-v1을 도입했다. 지도 전용 해시는 UTF-8 개행만 정규화하고 실제 내용 변경은 거부한다. JSON/출처 산출물은 LF로 재생성했다. 기존 원시 digest와 과거 실행 증거는 보존한다.
- 버전: 기존 최고 태그 0.4.3.0 이후의 미사용 마이너 버전 0.4.4.0으로 VERSION·Python package/API·Unity bundleVersion·현재 문서를 맞췄다. 다른 브랜치의 서버/알고리즘 작업을 포함한다는 뜻은 아니다.
- 호환성/마이그레이션: 구형 지도 manifest는 재생성이 필요하다. map_version=inha-osm-unity-v2, 운행 설정 schema_version=3, HTTP/Unity schema_version=3 및 Editor 6000.3.21f1은 유지한다. 메모리 상태의 마이그레이션은 없다.
- 검증: Python 205개, Unity EditMode 54개/PlayMode 1개 통과. Ruff src/tests 42파일 및 JS 3파일 구문 검사 통과. Git index 지도 해시 23개와 main 5파일 보존 확인. LF/CRLF checkout·내용 변조·재생성·버전 일치 회귀를 추가했으며 상세는 Docs/INTEGRATION_MAIN_DECISION.md에 기록한다.
- 미검증/한계: Python 가상 시간 완료와 Unity 물리 완료는 미연결이다. WebSocket·SensorRig·실제 차량 경합/접근성·성능 보장은 포함하지 않는다. Unity/플랫폼별 실행 범위는 통합 보고서를 따른다.

## 0.2.4.0 — 2026-10-19 · 분산 운용·배차 비교·A* 및 관제 UI

- 관제·RRT/TTC의 공통 헤더, 활성 탭, 입력/버튼, 카드, 상태 배지와 여백을 정리했다. Freesentation을 유지하고 한 줄 운행 컨트롤 및 상세 펼쳐보기를 보존했다. 브라우저에서 1600×900·1280×720 한 화면 배치, 390px 가로 넘침 없음, 차량 상세/노드 토글/TTC 모드 전환을 확인했다.

- 충전 중 차량을 완충까지 배차에서 제외하던 문제를 수정했다. FIFO/Greedy/Hungarian 공통으로 왕복+15% 예비량 충족 시 후보에 포함하고 선택된 차량만 충전을 중단한다. 필요 잔량 도달 이벤트, 중단 이력과 안내 문구를 추가했다. 전체 backend 196개 통과. 기존 완료 요청 8건·차량 잔량·충전/운송 이벤트를 보존해 실행 서버에 반영했다. 충전 중단은 합성 시간 모델이며 Unity 충전기는 미연결이다.

- 관제 상단 메뉴를 두 개로 정리하고 16:9 한 화면 요약/상세 대화상자·펼쳐보기로 재배치했다. RRT/TTC의 한 시점 그림을 독립 20Hz 운동 실험 및 재생/일시정지/배속/시간 이동으로 개선했다. 정적 RRT 추종, 횡단 TTC 제동/대기/재출발, 센서 만료 및 실패를 기록한다. 전체 backend 191개 통과. 기존 정적 preview API와 관제 상태를 유지하며 Unity/실차·다중 차량 경합 구현은 아니다.
- FIFO 기준선 외 자체 Greedy/Hungarian 배차를 공통 가능성/우선순위/J 비용 및 장기 대기 보호와 연결했다. 동시 요청 batch 접수와 웹 정책 선택, 동일 조건 독립 비교/반복·지표·JSON 저장을 추가했다. 사용자 제공 아이콘을 헤더/파비콘에 포함했다. 전체 backend 187개, Ruff/format check, JS 3개 구문 및 브라우저 확인. demo control dispatch 필드와 비교 endpoint만 확장하며 기존 통신 schema는 유지한다. 실제 차량/통행/Unity 검증은 아니다.
- 웹 FIFO 운행에 자체 A* 왕복 계산/선택을 연결하고 187개 실제 노드 토글·같은 snapshot의 Dijkstra 탐색 비교를 추가했다. 서비스 기본 Dijkstra와 기존 활성 임무는 유지한다. 별도 RRT/TTC 합성 시각화/API는 배차와 격리했다. 통신 기존 경로/버전은 유지하고 demo API 필드/경로만 추가했다. 검증: 전체 backend 147개, Ruff/format check, JS 2개 구문 검사와 데스크톱/390px 브라우저 확인. Unity 센서/제어는 미연결이다.
- 프로젝트 표시명을 Inha Mobil로 정정했다. 웹/Unity UI, Unity productName, README와 API 제목을 맞추며 기존 namespace/package/API 경로는 유지한다. Unity productName 변경에 따른 앱 저장 경로 영향 및 Unity 빌드는 미검증이다.
- Python 센서 관측 검증, 독립 TTC/정지거리 판단과 정적 RRT 기하 후보를 추가했다. 신규 24개/전체 backend 133개 테스트, Ruff/format check 통과. 합성 JSON/SVG와 연결 계획은 `Docs/LOCAL_AVOIDANCE.md`에 기록한다. Unity 센서/통신/제어 및 웹 운행과 미연결이며 동적 회피 완료가 아니다.
- UI에 로컬 Freesentation 400/500/600/700 웹폰트와 SIL OFL 1.1 라이선스를 포함하고 설명 문구 및 중간점 표시를 정리했다. API/운행 계산은 유지한다.
- V01 정문·V02 후문·V03 운동장 옆 비룡주차장을 기본 출발/복귀/가상 충전 거점으로 연결했다. 기존 OSM/Unity 대응 도로 노드만 사용하고 새 직선 연결을 만들지 않는다. 차량 FIFO는 유지한다.
- 설정 schema_version=3의 hubs/home_hub_id, 주차장 polygon·Unity 허브 출처·노드·거점별 14개 Stop 왕복 검사와 해시 검증을 추가했다. 도로 graph/map_version은 유지한다.
- 차량별 픽업/복귀 ETA·배터리와 충전 위치를 계산하고 웹 지도/카드/로그/초기화도 세 거점으로 표시한다. 각 거점의 1,000W 충전 1포트는 합성 가정이다.
- 검증: 전체 pytest 109개, Ruff/format check와 JS 구문 검사, 세 거점 초기 배치·왕복·충전·FIFO 및 잘못된 출처/바인딩 실패를 확인했다. 기존 단일 정문 기준 시각/횟수 기록은 보존한다.
- 호환성/마이그레이션: 구형 설정/지도 묶음을 재생성한다. HTTP/Unity schema_version=3, map_version=inha-osm-unity-v2는 유지하며 메모리 데이터 마이그레이션은 없다.
- 미검증/한계: 실제 주차 베이/진입/통행/충전소·Physics·센서·충돌 회피·수요 재배치는 미구현 또는 미검증이다.

### 함께 포함한 개발 중 충전 수정 (별도 0.2.3.1 릴리스 없음)

- 원인: 기존 정책은 다음 요청 에너지 부족 또는 미배정 SOC 30% 미만에서만 충전하여, 복귀한 차량이 30% 이상이면 충전되지 않았다.
- `charge_when_idle=true`를 기본 운행 설정에 명시해 즉시 배정되지 않은 거점 차량이 잔량 100% 미만이면 충전하도록 수정했다. 대기 요청 즉시 배차를 우선하고, 충전 중에는 배차하지 않으며 완충 후 FIFO 뒤에 넣는다.
- 호환성/마이그레이션: 선택적 battery 설정이며 생략/false이면 기존 정책을 유지한다. 통신/지도/설정 schema_version은 유지하고 설정/버전 변경으로 지도 묶음을 재생성한다. 데이터 마이그레이션은 없다.
- 검증: 복귀 시 SOC 30% 이상인 차량의 충전 시작·잔량 증가·완충·FIFO 재진입과 기존 정책 선택 회귀를 추가했다. 전체 pytest 101개, Ruff 검사/format check 및 웹 충전 상태를 확인한다.
- 한계: 기존의 합성 배터리·정문 독립 충전 3포트/1,000W 가정을 유지한다. 가상 시각이 정지되면 충전도 정지하며 물리 배터리/충전 제어는 아니다.

## 0.2.3.0 — 2026-10-18 · OSM 웹 2D 관제와 배속 시연

- 실제 캐시 OSM 건물/주변 도로와 v2 운행 그래프·14개 거점 후보를 로컬 SVG 지도로 표시한다. 차량 3대는 기존 Dijkstra 간선별 시간에 따라 이동하며 FIFO·왕복·배터리·충전 상태와 운행 기록을 제공한다.
- 독립 메모리 시연 세션과 1~300배속·정지·초기화, 승객/화물 요청·대기 취소, 5개 목적지/18건 충전 시나리오를 추가했다. 기존 HTTP 서비스의 상태와 Unity schema_version=3은 유지한다.
- 사용자 요청에 따라 공통 UI/PC 디자인 문서의 Primary #1677FF·네이비·흰 카드·읽기 쉬운 글자와 자체 SVG 아이콘을 적용했다. 구현하지 않은 관제 수치는 추가하지 않는다.
- 이유: 2026-10-19 팀 시연에서 기존 Python 알고리즘의 흐름을 외부 지도 키/타일/CDN 없이 보여준다. 시작 스크립트·시연 순서·한계를 문서화했다.
- 호환성/마이그레이션: `/demo/`와 `/v1/demo/*`가 추가된다. 기존 요청 API·설정 schema_version=2·map_version=inha-osm-unity-v2는 유지한다. VERSION 변경에 따라 지도 출처 manifest를 재생성했다. 데이터는 메모리이므로 마이그레이션은 없다.
- 검증: Python pytest 99개, Ruff 검사/format check, `node --check` 통과. 브라우저에서 요청→3대 배차→FIFO 대기, 경로 표시, 일시정지·배속, 18건 충전, 데스크톱/390px 화면을 확인했다. Starlette TestClient deprecation 경고 1개.
- 미검증/한계: 타이머·경로 재생이며 Unity Physics/센서/실제 도착 판정은 아니다. 실제 통행·폭·Stop·배터리·충전 시설, 다중 차량 충돌/경합, Python 3.11/다른 OS/실단말/성능은 미검증이다. OSM building relation은 표시하지 않으며 기본 타일/실시간 교통은 사용하지 않는다. 인증/탭별 격리 없이 로컬 시연 세션을 공유한다.

## 0.2.2.0 — 2026-10-16 · OSM/Unity 지도·FIFO·Dijkstra 및 차량 3대·배터리 가상 왕복

- 기본 V01~V03 세 대를 가용 대기열 FIFO로 배차하고 요청도 접수 순서를 따른다. 기존 campus.osm/campus.json과 Unity 출입구 인벤토리에서 OSM way 59개/노드 187개/방향 간선 402개를 구성했다. 14개 Landmark 모두 도로 Stop 후보와 정문 왕복 경로를 제공한다. 왕복 에너지와 15% 예비량을 검사하고 합성 배터리 소모·정문 가상 충전 후 재배차를 제공한다.
- OSM 공유 노드·일방통행·원본 geometry를 보존하고 보행로와 명시적 제한을 제외한다. 좌표는 WGS84→AEQD와 기존 Unity 로컬 그리드 간 명시적 변환을 사용한다. manifest가 원본/생성 코드/설정/산출물 해시와 미검증 가정을 기록하며 잘못된 원본·묶음은 실패시킨다.
- 사용자 요청으로 기준 가상 주행속도를 30km/h로 변경하고 낮은 원본 제한 우선 적용을 검사했다. 비용 snapshot ID에 설정 hash를 포함한다. 사용자 요청으로 확장 OSM에서 고정한 93개 선분 구간을 v2 기본 지도에 병합했다. 하이테크/생활관 1·2·3/역을 연결하고 역은 Unity 중심선 자료의 우회 복귀를 사용한다. 외곽 전체 네트워크는 개방하지 않는다.
- heap/g-score/부모 간선/stale entry 제거를 사용하는 자체 Dijkstra와 방향 멀티그래프, 전체 polyline 복원, 비용 snapshot 및 통행·계단·폐쇄·폭 제약을 구현했다.
- 픽업/운송/복귀 경로의 시간으로 가상 운행을 진행한다. 요청 목적지 도착과 차량 복귀를 분리하고 복귀 시 FIFO 대기열을 재평가한다. 백그라운드 tick, debug 상태 API와 무대기 데모를 추가했다.
- 이유: 실제 도로 형상에서 요청→배차→왕복→재배차를 독립 검증한다. 시간상 완료는 물리 운송 완료가 아니다. 미검증 통행 정보는 별도 `osm_geometry_simulation` 모드의 합성 정책으로만 사용한다.
- 호환성/마이그레이션: 기존 HTTP 입력과 Unity schema_version=3 유지. 기본 차량 수는 3대, 정책 설정 schema_version=2의 vehicles/battery로 단일 vehicle 필드를 대체한다. 기본 지도는 원본 OSM/Unity 기반으로 변경하고 고정 ETA 180초는 실제 형상 길이/가정 속도 기반 ETA로 교체한다. 기존 가상 6거점은 `--synthetic`으로 명시한다. 배정 후 픽업 전 취소는 차량을 즉시 해제하지 않고 원래 복귀 시각까지 점유한다. 지도 원본·설정·생성 코드 변경 시 재생성하며 메모리 상태의 데이터 마이그레이션은 없다.
- 검증: Python 3.14.4에서 pytest 91개 통과(100 합성 OD 독립 참조 비교, 원본/해시·좌표·접근성·미연결·왕복·HTTP 검사 포함), Ruff/format check 통과, 실제 형상 기본/확장 목적지 3대 데모 및 18건 배터리/충전 데모 실행. 검증 의존성을 `backend/requirements-dev.lock`에 기록한다. Starlette TestClient deprecation 경고 1개.
- 미검증/한계: Python 3.11/다른 OS, Unity/Player 주행, 차량 통행/폭/Stop/보행 접근성 승인·Physics·센서·안전·승하차 시간·성능·인증·명령 충돌 방어·영속 저장은 미검증 또는 미구현이다. 새 5곳은 가상 시간 운행에 연결했으나 실제 통행·Stop·보행 접근은 미검증이다. 배터리/충전은 합성 제원·독립 3포트·대기 전원 OFF 가정이다. A*·Greedy/Hungarian·실제 3대 Physics/센서/경합 통합은 포함하지 않는다.

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
