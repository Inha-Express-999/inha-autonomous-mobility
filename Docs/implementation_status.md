# 구현 현황

프로젝트 버전 **0.2.2.0** · 2026-09-24. 이 문서는 현재 checkout을 기준으로 하며, 과거 검증 기록과 현재 단계 판정을 구분한다. 근거가 없는 기능은 완료로 표시하지 않는다.

## 단계별 상태

| 단계 | 상태 | 확인된 구현 및 남은 종료 조건 |
|---|---|---|
| M0 클라이언트 기반 | VERIFIED (범위 한정) | Bootstrap/Additive 씬, Windows/Android 빌드 프로필, DTO, WorldStateStore, 좌표 변환, fixture replay 및 PC/Mobile uGUI 기본 화면. Unity 6000.3.21f1에서 과거 기록된 EditMode 54/54와 PlayMode 1/1, 화면 smoke 결과가 있다. Android Player/실기기 검증은 아니다. |
| M0 Python 서버 기반 | PARTIAL | FastAPI/Pydantic, 합성 Landmark·Stop·차량, 요청 검증·메모리 배정·취소 및 HTTP API. WebSocket schema-v3 snapshot, 모바일 요청 create/cancel·ACK와 합성 차량 1대 상태 전이가 있다. 서비스·HTTP·실제 ASGI WebSocket을 포함한 Python 테스트 20개가 통과했다. Unity 연결, 영속성, 실제 지도/Physics, 인증·재접속은 미검증/미구현이다. 과거 pytest 4/4와 Ruff 통과 기록은 0.1.6.0 기준이다. |
| 지도 조사·표현 | PARTIAL | OSM/DSM 조사 자료, 기존 Unity 캠퍼스 장면 및 근사 Terrain/건물 표현이 있다. 서비스용 그래프/Stop 또는 통행·접근성 승인은 아니다. 개별 상태는 [지도 계획](MapResearch/PLAN.md)을 따른다. |
| M1 단일 차량 수직 흐름 | PARTIAL | 합성 6거점 RoadGraph와 Dijkstra/A*, A* 기반 합성 차량 1대의 요청→픽업→운송→완료 상태 흐름이 연결됐다. 30개 directed OD 비교에서 비용 일치 30/30 기록이 있다. 상태 전이·접근성 경로·속도 제한·취소/재배정·snapshot 격리 및 HTTP/ASGI 통합 등 20개 검증이 통과했다. Unity Physics·실제 캠퍼스 그래프, 100쌍 T03·반복 측정, Unity transport/UI 통합 검증은 남아 있다. |
| M2 실제 지도·Stop·커버리지 | PARTIAL (조사만) | 후보·출입구 조사가 진행 중이나 검증된 필수 Landmark/Stop, 대표 시설 커버리지, 차량·보행 연결, 비룡플라자 정책은 미완료다. |
| M3 혼잡·동적 재계획 | NOT_STARTED | 다섯 시간대, 센서/prior 구분 혼잡 추정, A* Full Replan/D* Lite 및 T04~T06/T24가 없다. |
| M4 센서·안전·지역 계획 | NOT_STARTED | Unity Raycast SensorRig, SensorObservation 계약 연결, TTC/제동/재출발, RRT 및 T07~T09/T21가 없다. |
| M5 다중 차량·배차·경합 | NOT_STARTED | Greedy/Hungarian, 3대 차량 운용, 에너지/허브, Reservation/CBS 비교가 없다. 현재 Python fixture의 즉시 배정은 이 단계 구현이 아니다. |
| M5a 실시간 통합 | PARTIAL (synthetic command/snapshot alpha) | Python WebSocket과 Unity adapter가 구독·snapshot, 모바일 요청 생성/취소 UI·command·ACK를 지원한다. 0.5초 snapshot으로 합성 6거점 graph를 따라 움직이는 서버 차량 위치·경로·요청 상태를 보낸다. Unity Physics와 연동된 차량은 아니다. subscriber ID는 인증이 아니며 PC는 조회 전용, TLS/LAN 배포·재접속·세션/동시성 및 센서 입력 연결은 미완료다. |
| M6 실험·성능·OSS/IOSS | NOT_STARTED | E1~E4, 부하/성능 원자료, 재현 run, 동시 데모와 OSS/IOSS 증빙이 없다. |

## 지도 데이터 출처와 검증 상태

| 데이터 | 출처/보관 위치 | 현재 확인된 검증 | 서비스 사용 상태 |
|---|---|---|---|
| 기본 OSM | `Assets/InhaCampus/Source/campus.osm`, OSM 기여자·ODbL 1.0, 출처/범위는 `Assets/InhaCampus/Source/ATTRIBUTION.txt` | 원본 및 기존 장면 생성 입력. 기존 좌표 변환은 원점 37.4506N, 126.6535E의 로컬 평면 좌표이며 지도 계획상 AEQD 대비 검사점 최대 약 0.69m 차이가 남아 있다. | 시각 표현/조사 원본. 폭·통행 허용·경사·Stop은 승인되지 않음. |
| 확장 OSM | `Docs/MapResearch/2026-09-17/expanded_campus.osm`, 2026-09-17 다운로드 기록과 SHA-256은 같은 폴더 `downloads.json` | 조사 bbox 126.645~126.665E, 37.443~37.457N. 조사 범위이며 최종 서비스 경계가 아니다. | 원본 조사 자료. relation 조립·위상/연결 검증 후 그래프화해야 함. |
| Copernicus GLO-30 DSM | `Docs/MapResearch/2026-09-17/` 다운로드 기록·metadata·EULA 및 `maps/inha_relief_research/` 가공 결과 | manifest가 원본 SHA-256, AEQD 원점, 수직 기준 EGM2008, 257×257·8m 보간 격자, 처리 방법과 제한을 기록. RAW 양자화 왕복 오차 약 0.00077m는 파일 표현 오차이지 지형 정확도가 아니다. DSM은 건물/수목을 포함할 수 있다. | 근사 시각 Terrain 전용. 경사·단차·접근성·건물 높이 증명에 사용 금지. |
| OSM landmark 후보 | `maps/inha_relief_research/osm_features.geojson`, `landmark_candidates.json` | 983개 node/way feature 및 19개 landmark 후보 기록. 후보 수는 완료 시설 수가 아니다. relation 및 시설 대응·출입구 검증은 미완료다. | 검색/검토 후보. 런타임 graph/Stop으로 사용 금지. |
| 공식·보조 시설 참고 | `Docs/MapResearch/2026-09-17/inhatc_campus.html`, `inha_campus_reference.html`, 입학 자료 및 `Docs/MapResearch/BUILDING_REFERENCES.md` | 시설 명칭/위치 참고 자료. 시설 전체 목록과 각 출입구·보행/차량 동선의 현장 확인은 별도다. | 교차 확인 근거. 통행성·접근성 승인 아님. |
| Terrain·출입구·도로 작업 기록 | `Docs/MapResearch/Iterations/` 아래 개별 검사 결과 | 각 보고서는 기록된 구역·검사항목만 증명한다. 최신 계획은 인벤토리 14 랜드마크/27 출입구/27 접근 메시, 미해결 역할 1개로 기록하며 인하대역 이동지원 출입구가 미확정이다. | 전체 도로 그래프 연결, 교차로 접합, 경사/단차, 접근성 검증 전에는 운송 가능으로 표시하지 않음. |

필수 랜드마크 목록과 대표 시설 전체 대조, service_needs별 Stop 선택, 접근 가능한 보행 연결, 차량 통행 그래프, 비룡플라자 경계/대체 지점은 M2 종료 조건이다. 생활관 1·2·3은 현재 일부 형상·도로가 시각화 초안이며 차량 Stop이 아니다. 지도 데이터 세부 현황과 다음 검증은 [Docs/MapResearch/PLAN.md](MapResearch/PLAN.md)에 기록한다.

## 클라이언트·Python 기반의 한계

- Python `backend/`는 서비스 기반이다. 현재 합성 ETA는 그래프 경로 길이와 edge 허용속도에 기초한 값이며, 실제 위치·혼잡·승하차 시간의 현장 측정 ETA가 아니다. 합성 서버 요청 성공은 실제 시뮬레이션 운송 성공이 아니다.
- Unity fixture는 합성 snapshot이다. 0.1.5.0 시점 fixture 화면 검증과 0.1.6.0 서버 기반은 같은 네트워크 실행이 아니다.
- PC의 요청 입력, Landmark 검색, 지도 위 차량/경로 그리기, 공통 JSON Schema 파일, 이벤트 delta, Unity Physics/센서 연동은 미완료다. Mobile은 합성 Landmark 순환 선택, step-free 선택 및 create/cancel command UI가 있다. 서버 차량·경로 좌표는 합성 graph 좌표이며 Unity 차량을 움직이지 않는다. Python HTTP/ASGI는 검증했지만 Unity UI/전송 통합, 끊김/재접속·세션/부하 안정성은 미검증이다. 검증 범위와 위험은 [서버 안정성 메모](server_stability.md)에 기록한다.
- 과거 클라이언트/API 테스트 결과는 해당 버전 당시의 기록이다. 이번 통합 검증에서는 Python pytest 20개를 실행했다. Unity suite/실기기 검증은 실행하지 않았다.

## M1 RoadGraph·경로 탐색 초안

- 입력 계약/검증: `backend/src/campus_sim/road_graph.py`. graph는 현재 `schema_version=1`, 독립 `map_version`, `data_status`, 좌표 프레임, 출처 날짜/hash/원점, 노드/Stop, 방향 edge의 전체 geometry·길이·폭·허용 차량 클래스·속도·zone·출처·검증 상태·비용 penalty·서비스 허용과 step-free 조건을 기록한다. 미지원 필드/스키마 버전, 중복 ID, 없는 endpoint, endpoint와 geometry 불일치, geometry보다 짧은 edge 길이, 비유한 값은 거부한다.
- fixture: `maps/fixtures/campus-synthetic-6.json`. 6개 합성 Stop·랜드마크와 양방향 edge 12개를 포함한다. 폭 4m 등 graph 숫자는 알고리즘용 합성 가정이며 실측값이 아니다. 좌표·연결은 캠퍼스 실제 위치가 아니며 운송 가능한 지도 데이터가 아니다. schema_version 1과 map_version `synthetic-campus-6stop-v1`은 프로젝트 버전과 독립이다.
- planner: `backend/src/campus_sim/planning.py`에 자체 Dijkstra/A*를 구현했다. edge 비용은 `length_m / allowed_speed_mps + crowd_penalty_s + zone_penalty_s + expected_wait_s`; 폐쇄, 서비스/차량 클래스 부적합, 입력된 차량 폭보다 좁은 edge, 요구 step-free 미충족 edge는 Hard Constraint로 탐색에서 제외한다. A* 휴리스틱은 직선거리/그래프의 최대 허용 속도다.
- 실행: `PYTHONPATH=backend/src python -m campus_sim.cli route-compare --map maps/fixtures/campus-synthetic-6.json`. `--start-stop`·`--goal-stop`을 같이 주면 한 OD, 생략하면 모든 ordered distinct OD 30쌍을 비교한다. `--require-step-free`와 `--service-type`도 지원한다. 출력에 경로/비용/길이, expanded nodes, 고유 generated nodes, peak open set, 진단용 elapsed_ns가 포함된다.
- 실행 결과(합성 fixture, 단일 실행): 전체 OD 비용 일치 30/30, 도달 가능 30/30, A* expanded nodes가 더 적은 pair 11개, 평균 expanded nodes Dijkstra 3.0/A* 2.53. step-free 제약에서도 비용 일치 30/30, A* expanded nodes 우세 3쌍, 평균 Dijkstra 3.0/A* 2.9. Stop 1→4 경로 비용은 양 알고리즘 모두 102초, 경로는 stop 1→2→5→6→4다.
- 해석 제한: 평균 확장량은 이 작은 합성 fixture의 결과이며 알고리즘 우위나 실제 캠퍼스 성능을 증명하지 않는다. elapsed_ns는 단일 실행 진단치이지 p50/p95 벤치마크가 아니다. T03의 정적 graph 100 OD 쌍, 20 seed/반복 측정, 실지도 edge/geometry 검증, 요청→완료 통합은 남아 있다.

## 차량 모델·프리팹 준비 상태

최근 커밋 `f97fa11`에서 차량 모델과 프리팹을 추가했다. 프로젝트 에셋은 `Assets/CampusSim/Models/AnnyongCar/`, `Models/Default Car/`, `Models/Induck Car/` 및 `Assets/CampusSim/Prefabs/Annyoung Car.prefab`, `DefaultCar.prefab`, `InduckCar.prefab`에 있다. 주행 구현에서는 이 프리팹을 차량의 시각 표현으로 재사용한다. 프리팹 추가만으로 차량 동역학·Collider/Physics 설정·차량 제원·차량 능력·안전 제동이 구현된 것은 아니다. M1/M4에서 축/전방 방향·미터 단위 크기·피벗·Collider/접지·렌더링 비용을 검토한 후, 시각 모델과 물리/제어 루트를 분리해 연결한다. 제원이나 성능은 측정·확인 전까지 미확정이다.

주행 그래프 노드·edge는 지도 데이터 계약을 원본으로 삼고, graph node/Stop marker·edge/route 시각화 요소는 협업과 반복 편집을 위한 Unity prefab으로 재사용한다. 캠퍼스 장면의 반복 환경물도 구조가 안정적이고 여러 위치/씬에서 공유되는 항목부터 프로젝트 소유 prefab/variant로 정리한다. 장면 GameObject Transform을 주행 graph 원본으로 쓰지 않으며, 현재 graph node·edge prefab은 아직 없다. 상세 기준은 [AGENTS.md](../AGENTS.md)의 지도 그래프·캠퍼스 오브젝트 재사용 규칙을 따른다.

## Python–Unity WebSocket 초기 연결

- 서버 경로 `ws://<host>:8765/v1/client/ws`. 첫 메시지는 `type=subscribe`, `schemaVersion=3`, 네 자리 `projectVersion`, `role`이며 Mobile은 `subscriberId`도 보낸다. schema와 프로젝트 버전은 독립이며 schema 불일치만 거부한다.
- 서버는 `connected`와 2Hz `snapshot` envelope를 보낸다. snapshot은 기존 C# `WorldSnapshotDto` schema v3용 camelCase JSON이다. Unity는 백그라운드 수신 후 `Pump`에서 Unity 메인 스레드의 `WorldStateStore`에 적용한다.
- Unity Bootstrap의 `useFixture`를 끄면 WebSocket을 사용한다. 기본 주소는 `ws://127.0.0.1:8765/v1/client/ws`; 모바일 기기에서는 PC 서버의 LAN 주소로 설정한다.
- 현재 연결은 합성 alpha다. Mobile passenger의 `create_request`·`cancel_request` command와 `command_ack` 이후 요청이 다음 주기 snapshot에 반영된다. 같은 메모리 서비스가 합성 6-stop graph를 읽어 V01의 A* 경로를 만든다. 차량 속도는 polyline을 구성한 각 edge의 허용속도와 합성 상한 중 낮은 값이며, 픽업/하차 서비스 각 2초 뒤 상태가 진행된다. 위치·속도·서비스 시간은 합성 데모값이지 실측값이나 실제 차량 제어가 아니다. Unity Physics, 실지도 좌표, 정교한 ETA, 재접속·세션 안정성은 없다. `subscriberId`는 인증이 아니므로 신뢰 네트워크에만 서버를 공개한다.
- Python 20개 테스트가 통과했다. fake socket 기반 command handler 단위 검증과 FastAPI ASGI HTTP/WebSocket handshake/timeout/message limit/schema rejection을 확인했다. Unity 직렬화/Player, 실제 재접속 및 동시 연결 부하는 포함하지 않는다. 검증 조건과 위험은 [서버 안정성 메모](server_stability.md)에 기록했다.

## 다음 작업 순서

1. 지도 계획의 출처·검증 표를 유지하고 필수 Landmark/대표 시설/Stop/차량·보행 연결의 실제 검증 근거를 추가한다.
2. 작은 검증 RoadGraph와 좌표/edge 계약을 만들고, 반복 편집용 node/Stop marker·edge 시각화 prefab을 함께 만든다. T14에 해당하는 누락·오류 거부도 구현한다.
3. M1을 이어서 100개 OD 비용 동등 비교와 반복 측정, 차량 1대 요청→완료 흐름을 구현한다. 이 단계에서 새 차량 프리팹의 축·크기·Collider 연결 요구를 기록한다.
4. Unity transport/UI의 실제 server 연동과 끊김/재접속·세션/부하 검증을 수행한다. 그 결과를 반영한 뒤 실제 지도 경로와 차량 prefab/Physics 표현 연결을 진행한다.
5. M2 지도/접근성/비룡플라자 범위를 완료하는 동안 재사용 빈도가 높은 캠퍼스 오브젝트를 prefab/variant로 단계적으로 정리한 뒤 센서·안전, 다중 차량, 성능 실험을 진행한다.

버전의 단일 원본은 루트 `VERSION`이다. 현재 Python package/API 버전, Unity `bundleVersion`, README 및 CHANGELOG는 0.2.2.0로 정합화했다. Unity Editor 버전은 별도인 6000.3.21f1이다. schema_version 및 map_version은 프로젝트 버전과 독립적으로 유지한다.
