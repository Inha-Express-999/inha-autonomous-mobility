# 구현 현황

프로젝트 버전 **0.2.3.0** · 2026-10-18. 이 문서는 현재 checkout을 기준으로 하며, 과거 검증 기록과 현재 단계 판정을 구분한다. 근거가 없는 기능은 완료로 표시하지 않는다.

2026-10-18: 독립 세션 `/demo/`에서 OSM 기반 웹 2D 관제와 차량 3대 경로 재생, 요청·FIFO·배터리·충전, 1~300배속/정지/초기화를 연결했다. 전체 pytest 99개와 브라우저 요청·배차·충전·정지 흐름을 검증했다. Unity/물리/센서 완료 판정과는 구분한다. [시연 안내](WEB_DEMO.md)를 따른다.

## 단계별 상태

| 단계 | 상태 | 확인된 구현 및 남은 종료 조건 |
|---|---|---|
| M0 클라이언트 기반 | VERIFIED (범위 한정) | Bootstrap/Additive 씬, Windows/Android 빌드 프로필, DTO, WorldStateStore, 좌표 변환, fixture replay 및 PC/Mobile uGUI 기본 화면. Unity 6000.3.21f1에서 과거 기록된 EditMode 54/54와 PlayMode 1/1, 화면 smoke 결과가 있다. Android Player/실기기 검증은 아니다. |
| M0 Python 서버 기반 | PARTIAL | FastAPI/Pydantic, 메모리 요청/취소 API, FIFO와 OSM/Unity 형상 기반 가상 시간 실행. 기본 V01~V03 세 대, 테스트 99개 및 Ruff/format check 검증. 독립 OSM 웹 2D 시연 세션을 제공한다. 영속 저장, 인증된 principal/명령 충돌 방어, WebSocket/Unity 연결은 미완료다. |
| 지도 조사·표현 | PARTIAL | OSM/DSM 조사 자료, 기존 Unity 캠퍼스 장면 및 근사 Terrain/건물 표현이 있다. 서비스용 그래프/Stop 또는 통행·접근성 승인은 아니다. 개별 상태는 [지도 계획](MapResearch/PLAN.md)을 따른다. |
| M1 단일 차량 수직 흐름 | PARTIAL (실제 형상·가상 시간 실행) | 자체 Dijkstra, 방향/평행 edge와 geometry 복원, FIFO·용량·Stop 접근성·세 구간 경로 확인, 픽업→시간상 완료→복귀 후 재배차. 정적 100 합성 OD를 Floyd–Warshall 기준과 대조했다. A* 비교, 승인된 실제 운송 지도, 승하차 시간/도착 판정, Unity 주행은 없어 T01/T03 전체 통과가 아니다. 합성 배터리 소모·예비량·충전과 3대 차량 FIFO 가상 시간 운행을 추가했다. |
| M2 실제 지도·Stop·커버리지 | PARTIAL (형상 그래프·후보) | OSM/Unity 도로 59 way, 187 node, 402 방향 edge를 AEQD로 변환했다. 인벤토리 14곳 모두 Stop 후보와 정문 왕복 경로를 제공한다. 하이테크/생활관 1·2·3/역을 포함한 v2이며 역은 Unity 중심선 기반 우회 복귀를 사용한다. 접근성·폭·통행·보행/차량 연결 승인과 비룡플라자 정책은 미완료다. 원본/생성 코드 해시와 누락/오류 실패 처리를 검증했다. |
| M3 혼잡·동적 재계획 | NOT_STARTED | 다섯 시간대, 센서/prior 구분 혼잡 추정, A* Full Replan/D* Lite 및 T04~T06/T24가 없다. |
| M4 센서·안전·지역 계획 | NOT_STARTED | Unity Raycast SensorRig, SensorObservation 계약 연결, TTC/제동/재출발, RRT 및 T07~T09/T21가 없다. |
| M5 다중 차량·배차·경합 | PARTIAL (가상 시간 한정) | 3대 차량의 가용 순서 FIFO, 합성 배터리 소모·15% 예비량·정문 가상 충전과 재배차를 검증했다. Greedy/Hungarian 비교, 실제 제원/충전소 검증, 다중 차량 Physics·Reservation/CBS·허브 정책은 없다. M5 전체 완료가 아니다. |
| M5a 실시간 통합 | NOT_STARTED | Python WebSocket, PC/Mobile 권위 상태, 실제 호출/ACK/재접속/세션 격리, 센서 입력 연결이 없다. |
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

- Python `backend/`는 실제 OSM/Unity 형상에 기반한 가상 시간 실행이다. `osm_geometry_simulation`에서 30km/h(약 8.33m/s) 가정 속도로 계산하며 도로 폭·통행·Stop은 승인되지 않았다. 고정 ETA 180초는 제거했다. 시간상 완료는 Unity 물리 운송 성공을 의미하지 않는다.
- Unity fixture는 합성 snapshot이다. 0.1.5.0 시점 fixture 화면 검증과 0.1.6.0 서버 기반은 같은 네트워크 실행이 아니다.
- PC/Mobile 요청 생성 UI, Landmark 검색·선택, 지도 차량/경로 표시, 공통 JSON Schema, WebSocket, 이벤트 delta, Physics/센서 연동은 미완료다. 상세 화면 인수 기준은 [클라이언트 적용·검증 계획](ClientUI/IMPLEMENTATION.md)을 따른다.
- 2026-10-15 후속: 3대·배터리 적용 후 pytest 91개, Ruff/format check를 통과했다. 차량 가용 FIFO·예비량 경계·적재 에너지·충전 재배차·큰/작은 tick 결과 일치를 검사했다. 확장 목적지 5건과 배터리 18건 데모를 재생했고 `artifacts/battery_fleet_validation.json`에 입력/코드 hash와 결과를 기록했다. 아래 75개 검사는 이전 단일 차량 시점 기록이다.
- 2026-10-15: Python 3.14.4에서 `python -m pytest backend/tests -q -p no:cacheprovider` 75개 통과, Ruff/format check 통과 및 `python -m campus_sim.demo_fifo` 실행. 원본/산출물 해시, 지도 재생성, 세 좌표 기준점, 실패 사례와 HTTP/자동 복귀를 검증했다. Windows 로컬 소켓 권한과 쓰기 가능한 별도 basetemp가 필요했다. 의존성은 `backend/requirements-dev.lock`에 고정한다. Unity/Player/실단말/성능 및 Python 3.11 실행은 미검증이다.

## 차량 모델·프리팹 준비 상태

최근 커밋 `f97fa11`에서 차량 모델과 프리팹을 추가했다. 프로젝트 에셋은 `Assets/CampusSim/Models/AnnyongCar/`, `Models/Default Car/`, `Models/Induck Car/` 및 `Assets/CampusSim/Prefabs/Annyoung Car.prefab`, `DefaultCar.prefab`, `InduckCar.prefab`에 있다. 주행 구현에서는 이 프리팹을 차량의 시각 표현으로 재사용한다. 프리팹 추가만으로 차량 동역학·Collider/Physics 설정·차량 제원·차량 능력·안전 제동이 구현된 것은 아니다. M1/M4에서 축/전방 방향·미터 단위 크기·피벗·Collider/접지·렌더링 비용을 검토한 후, 시각 모델과 물리/제어 루트를 분리해 연결한다. 제원이나 성능은 측정·확인 전까지 미확정이다.

## 다음 작업 순서

2026-10-15 확장 적용: 사용자 요청으로 다섯 거점의 고정 OSM 선분을 v2 기본 지도에 반영했다. 총 14곳에서 FIFO·Dijkstra 픽업/운송/복귀를 제공하며 역은 미지원 지름길을 제외한 407.16m 복귀 경로를 사용한다. V01~V03 세 대·30km/h·합성 배터리/충전을 적용한다. 91개 테스트와 확장 목적지/배터리 데모를 검증했으며 실제 운송/접근성 승인은 미완료다.

1. 지도 계획의 출처·검증 표를 유지하고 필수 Landmark/대표 시설/Stop/차량·보행 연결의 실제 검증 근거를 추가한다.
2. 확장한 하이테크·생활관·역의 통행·폭·경사·Stop·보행/횡단 접근과 실제 씬 적용을 검증해 T14/T19를 확장한다.
3. Dijkstra→A* 비교와 Unity 차량 1대의 실제 운송 흐름을 구현한다. 가상 왕복 타이머는 물리 운송 완료 판정으로 사용하지 않는다.
4. M2 지도/접근성/비룡플라자 범위를 완료한 뒤, 센서·안전, 다중 차량, WebSocket 통합 순으로 진행한다.

버전의 단일 원본은 루트 `VERSION`이다. 현재 Python package/API 버전, Unity `bundleVersion`, README 및 CHANGELOG는 0.2.3.0로 정합화했다. Unity Editor 버전은 별도인 6000.3.21f1이다. schema_version 및 map_version은 프로젝트 버전과 독립적으로 유지한다.
