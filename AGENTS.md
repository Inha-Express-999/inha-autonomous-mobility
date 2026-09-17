# AGENTS.md

## 0. 프로젝트와 작업 규칙

**제목:** 실제 캠퍼스 지도 기반 승객·물류 통합 자율주행 운송 서비스 시뮬레이션  
**문서:** v0.1.0.1 · 2026-09-17 · 요구사항/설계/구현/검증 통합본
**대상:** 개발자와 AI 코딩 에이전트. 구현 완료 보고서가 아니다.

Python이 배차·경로·보행자·상태를 계산하고 Unity PC 관제와 모바일 승객 클라이언트가 각 역할에 맞게 표시·입력한다. 핵심은 **혼잡·접근성·안전을 고려한 승객 이동과 배송**이다.

1. 작업 전 기존 코드·Git 변경·버전·테스트를 조사한다. 사용자 코드를 무단 교체하지 않는다. 신규 작업은 §16의 M0→M1부터 시작한다.
2. 신규 저장소는 Python 권위적 시뮬레이션+Unity+WebSocket을 기본으로 한다. 기존 ROS2 구현은 유지하고 어댑터로 연결한다.
3. **09:00/10:30/12:00/13:30/15:00 전후 혼잡과 비룡플라자 앞 우회**는 필수다. 사용자 관찰이지 공식 시간표/실측 통계가 아니다.
4. 안전·통행·접근성·차량 능력은 강제 제약이다. 급한 요청도 위반할 수 없다. RRT보다 감속·정지를 먼저 처리한다.
5. 좌표·도로 폭·경사·차량 제원·성능·IOSS 인정 여부를 추측해 확정하지 않는다. 합성 가정과 측정값을 구분한다.
6. A*/RRT는 자체 구현한다. MCP는 선택적 Editor 자동화에만 사용하며 런타임 판단에는 LLM을 넣지 않는다.
7. 이 프로젝트는 시뮬레이션이다. 실제 차량 제어·승객 운송·안전 인증·공공도로 운행 허가는 범위 밖이다.

### 프로젝트·문서·Unity 버전 규칙

프로젝트 버전은 **`A.B.C.D`의 네 자리 숫자**로 통일한다. 각 자리는 0 이상의 정수이며 자릿수 상한은 두지 않는다. 표시/태그에는 `v` 접두사를 사용할 수 있지만 저장 값은 숫자와 점만 사용한다.

| 자리 | 의미 | 증가 기준 |
|---|---|---|
| A | 공개 단계 | 프로젝트 개발·통합·검증·중간 시연 동안 **0 유지**. 필수 범위와 M6 완료 기준을 충족해 최종 결과물을 공개/발표할 릴리스를 확정할 때 최초로 1 |
| B | 메이저 업데이트 | 아키텍처·핵심 기능·서비스 범위의 큰 변경 또는 호환되지 않는 계약 변경 |
| C | 마이너 업데이트 | 기존 구조 안의 기능 추가·개선 및 유의미한 설계 보완 |
| D | 패치 업데이트 | 버그 수정·작은 설정 조정·문서 정정 등 기능 범위를 바꾸지 않는 수정 |

상위 자리를 올리면 하위 자리는 모두 0으로 초기화한다. 예: 패치 `0.2.0.0→0.2.0.1`, 마이너 `0.2.0.1→0.2.1.0`, 메이저 `0.2.1.0→0.3.0.0`, 최초 최종 공개 `0.x.x.x→1.0.0.0`. 이후 메이저도 두 번째 자리를 올린다(`1.0.0.0→1.1.0.0`). 첫 자리를 일반 메이저 번호로 사용하지 않는다. 단순 데모·중간 발표·모바일 빌드 성공만으로 첫 자리를 올리지 않는다.

기존 문서 v1.0~v1.2는 네 자리 규칙 도입 전 문서 개정 번호이며 완성/공개 버전이 아니다. 초기 통합 설계 기준선은 `0.2.0.0`이었으며, 2026-09-17 사용자의 명시적 요청으로 맵 수정 전 체크포인트를 **`0.1.0.0`**으로 재설정한다. 이번 재설정은 일회성 예외이며 후속 변경에는 위 증가 규칙을 적용한다. 이는 구현 완료를 뜻하지 않으며 후속 변경부터 위 규칙으로 증가시킨다.

저장소 루트 `VERSION`을 단일 원본으로 두고 문서 머리말·Python 서버의 프로젝트 버전·Unity PC/모바일 앱의 프로젝트 표시 버전·릴리스 태그를 같은 릴리스에서 일치시킨다. Unity PC와 모바일은 동일 프로젝트 버전을 사용하고 플랫폼·빌드 식별자는 별도 기록한다. `CHANGELOG.md`에 버전·날짜·변경 이유·호환성/마이그레이션·검증 결과를 기록한다. 문서만 바꾸는 경우에도 해당 변경 수준에 맞게 증가시키되 과거 빌드의 버전은 소급 변경하지 않는다. 현재 문서는 이 관리 체계의 구현 요구사항이며 VERSION/앱이 이미 갱신되었다고 가정하지 않는다.

Unity **앱/프로젝트 버전**과 Unity **Editor 버전**은 구분한다. Editor는 실제 설치된 공식 버전 문자열을 그대로 기록·고정하며 네 자리 프로젝트 번호로 바꾸지 않는다. PC/모바일은 같은 Editor 버전을 사용하고 업그레이드 시 함께 검증한다. 플랫폼의 버전 필드 형식 제약이나 증가 전용 빌드 번호가 필요한 경우 빌드 단계에서 명시적 매핑을 적용하고, 네 자리 원본·플랫폼 버전·빌드 번호·Editor 버전·코드 commit을 빌드 기록에 함께 남긴다. 앱 내 정보 화면에서는 네 자리 프로젝트 버전을 확인할 수 있어야 한다.

통신 `schema_version`, 설정 schema_version, map_version은 각 계약/데이터의 독립 버전이다. 프로젝트 버전과 혼용하거나 일괄 치환하지 않는다. 계약 호환성 검사는 실제 schema_version에 따르며 PC·모바일·서버 프로젝트 버전도 접속/실행 기록에 남긴다.

### 커밋 버전 표기·태그 필수 규칙

1. 앞으로 이 저장소에서 커밋할 때는 **제목 맨 앞에 `[vA.B.C.D]`를 반드시 표기**한다. 예: `[v0.1.0.1] 커밋 버전 및 태그 규칙 명문화`. 버전은 해당 커밋의 루트 `VERSION`과 정확히 일치해야 한다.
2. **모든 새 커밋에는 같은 버전의 annotated Git 태그 `vA.B.C.D`를 반드시 생성**한다. 제목에 버전을 적는 것만으로 완료하지 않는다. 태그 메시지에도 버전과 변경 요약을 남긴다.
3. 새 변경을 커밋할 때는 위 증가 규칙에 따라 미사용 버전을 부여하고 VERSION·현재 문서 머리말·Unity 앱 버전·존재하는 서버 버전과 CHANGELOG를 동기화한다. 문서만 변경해도 적용한다. 과거 실행 결과/빌드 기록과 독립 schema/map 버전은 소급 변경하지 않는다.
4. 커밋 후 태그가 해당 커밋을 가리키는지, 제목·VERSION·Unity 앱 버전이 일치하는지 검증한다. 최종 보고에는 **버전·커밋 해시·태그·푸시 여부**를 적는다.
5. 이미 존재하는 버전 태그를 다른 커밋으로 옮기거나 덮어쓰지 않는다. 공유된 커밋/태그 재작성과 강제 푸시는 사용자가 명시적으로 요청한 경우에만 수행한다.
6. 푸시가 승인된 작업에서는 커밋과 해당 버전 태그를 함께 푸시한다. 로컬 커밋/태그 생성만 승인된 경우 원격 푸시는 하지 않는다. 이 규칙 자체가 별도 요청 없는 커밋·원격 업로드 권한을 부여하지는 않는다.

## 1. 범위와 필수 요구사항

실외 지정 거점 사이에서 일반 승객, 이동지원 승객, 수업 등 도착 마감이 있는 승객과 물품을 운송한다. 시간 민감 요청은 의료 응급 이송이 아니다. 승객 이동은 캠퍼스 랜드마크에서 호출해 다른 랜드마크로 이동하는 방식으로 단순화한다. 차량은 초기 3대이며, 개발용 최소 fixture는 거점 6개로 시작하되 최종 서비스 범위는 §3의 대표 시설 전체를 포괄한다. 인하공전 및 캠퍼스 밖 연결 구간은 필요성과 통행·접근성을 검증한 뒤 확장한다.

초기는 차량당 한 요청/한 승객 그룹이다. 통합은 같은 배차·관제 체계를 뜻하며 동시 사람/화물 적재, 합승, 실내 이동, 인식/SLAM/강화학습, 결제는 제외한다.

필수 요구사항 ID는 MAP(지도·그래프), REQ(요청·상태·마감), ACC(이동지원), DSP(배차·공정성·충전), PLN(A*·RRT), CRD(보행/밀도), ZON(비룡플라자 우회), SAF(난입/경합), UI(PC 관제·모바일 승객), EXP(실험/재생), OSS(IOSS 증빙)다. 각 인수 조건은 §14 테스트와 §16 단계로 검증한다.

초기 성능 목표는 기준 장비에서 차량 3대·보행자 300명, tick p95≤50 ms, UI≥30 FPS, A* p95≤100 ms, 배차 p95≤300 ms다. PC와 모바일의 UI 목표는 각각 기준 장비/단말·품질 설정을 기록해 측정한다. 이는 측정 전 목표이며 안전 루프를 막아 달성하지 않는다. 10대/1,000명은 스트레스 실험이다.

## 2. 아키텍처와 저장소

```text
OSM/현장 검증 → Map Builder → 버전 고정 graph/geometry/landmarks/stops/zones
요청 → Dispatch → Global A* → Local RRT → Safety/Controller
          ↑           ↑                         ↓
       Crowd ← Python Simulation Engine/WorldState → 기록·평가
                       ↕ WebSocket 서버
              ┌───────────┴───────────┐
       Unity PC_Operator      Unity Mobile_Passenger
       전체 3D 디지털트윈 관제   단순 3D 승객 호출·운행 안내
AI Agent → MCP(선택) → Unity Editor importer/검증/테스트
```

위치·속도·시간·인원·적재·충돌·예약의 단일 원본은 Python `WorldState`다. Unity는 승인된 상태를 보간 표시하며 NavMesh/물리가 차량·보행자를 별도로 이동시키지 않는다. 무창 실행과 시각화 실행에 같은 엔진을 사용한다. PC와 모바일은 서로 직접 통신하지 않고 동일 Python authoritative 서버에 접속한다. 초기 시연은 PC 한 대에서 Python 서버와 PC 관제 앱을 함께 실행하고 모바일은 같은 Wi-Fi/LAN으로 Python 서버에 접속한다. 두 화면은 같은 run/map/tick의 상태를 역할별로 표시하며 클라이언트가 배차·Stop·경로·안전을 독자 결정하지 않는다.

하나의 Unity 프로젝트에서 공통 3D `CampusWorld`와 코드·DTO·네트워크·프리팹을 공유한다. 역할별 씬/UI는 `PC_Operator`, `Mobile_Passenger`로 분리하고 `CampusWorld + 역할 씬`의 Additive 로딩을 권장한다. 지도 수정은 공통 자산에 반영하고 역할별 표현·품질만 분리한다.

기본 스택은 Python 3.11 이상 호환 버전, FastAPI/Pydantic, NumPy, OSMnx/NetworkX, pyproj/Shapely, Unity LTS와 NativeWebSocket이다. 기존 Unity 버전을 우선 유지하고 신규 환경은 M0에서 호환성을 검증한다. 의존성은 smoke test 후 lockfile에 고정한다.

OSMnx·pyproj[S1] [S2], FastAPI·NativeWebSocket[S3] [S4]을 활용한다. 선택 ROS2는 Connector/Endpoint를 함께 검증하며[S5] OMPL은 비교용이다.

`domain`은 HTTP/Unity/ROS와 분리한다. 경계에서 DTO를 검증하고 application으로 상태를 바꾼다. 기존 폴더는 아래 책임에 대응시킨다.

```text
AGENTS.md / README.md
backend/{pyproject.toml,tests/}
backend/src/campus_sim/{domain,maps,planning,dispatch,simulation}/
backend/src/campus_sim/{application,transport,evaluation,cli.py}
unity/Assets/CampusSim/{Editor,Tests,Prefabs}/
unity/Assets/CampusSim/Scripts/{Common,PC,Mobile}/
unity/Assets/CampusSim/Scenes/{CampusWorld,PC_Operator,Mobile_Passenger}.unity
contracts/      # JSON Schema·공통 fixture
configs/        # simulation, crowd, vehicles, dispatch, zones
maps/{map_id}/  # manifest, graph, geometry, landmarks, stops
scenarios/      # 이벤트·검증 입력
artifacts/      # 실행 결과
docs/           # ADR·구현 상태·실험·OSS 증빙
```

## 3. 실제 지도와 접근성

검증된 polygon으로 OSM 범위를 고정하고 원본·추출일·조건·OSM ID를 캐시한다. OSMnx의 `graph_from_polygon`, `features_from_polygon`, `project_graph`, GraphML 저장을 참고하되 설치 버전 API를 확인한다.[S1] 런타임 외부 API 호출은 피한다.

`vehicle_graph`와 `pedestrian_graph`를 분리한다. OSM 보행로를 차량 허용으로 해석하지 않는다. 폭·계단·경사·일방통행·공사·진입 조건은 검증된 overlay로 보정한다. `unknown`은 실제 지도 모드에서 제외하며 합성 모드에서만 가정으로 허용한다. 고도 미확인은 평면 실험으로 표시한다.

edge 전체 polyline과 `(u,v,key)`를 보존해 곡선/평행 도로를 잃지 않는다. 건물 중심 대신 출입구에 연결된 안전한 승하차 거점을 둔다. manifest는 지도 버전·출처·날짜·CRS/원점·hash·검증 상태, edge는 geometry·길이/폭·허용 차량·속도·zone·출처를 가진다.

### 랜드마크와 서비스 범위

`Landmark`는 사용자가 선택하는 시설/목적지이고 `Stop`은 차량이 실제 정차하는 검증된 승하차 지점이다. 하나의 랜드마크에 여러 Stop을 연결해 일반/접근 가능한 출입구를 지원한다. 건물 외에 문·역·광장도 동일한 선택 단위로 취급하며 건물 중심이나 광장 중앙을 차량 목적지로 사용하지 않는다.

필수 초기 목록은 사용자 지정 명칭인 **정문, 인하대역, 후문, 5호관, 2호관, 하이테크, 60주년, 비룡플라자, 기숙사 1·2·3**다. 2026-09-17 사용자 확인으로 제4생활관은 제외하고 지하철역은 인하대역으로 확정했다. 기숙사는 세 개의 개별 랜드마크로 관리한다. 이는 검증 전 요구 목록이며 역 출구, 기숙사 번호와 실제 시설의 대응, 공식 시설명·좌표·출입구는 공식 캠퍼스 지도와 현장 확인으로 확정한다. 존재나 위치가 확인되지 않은 항목을 임의로 생성하지 않는다. 지형/외형은 대략적인 형태를 목표로 하며 공개 DSM 가공값과 검증된 통행·접근성 정보를 구분한다. 지도 개선 계획과 데이터 현황은 `Docs/MapResearch/PLAN.md`를 따른다.

이 목록을 상한으로 삼지 않는다. 지도 구축 시 전체 시설 목록을 대조해 교육·연구동, 도서관, 학생회관, 행정·복지·체육 시설 등 대표 시설마다 직접 연결된 랜드마크 또는 접근 가능한 보행 연결로 이용할 수 있는 인근 랜드마크를 지정한다. `landmarks` 데이터에 명칭/별칭·시설 유형·소속 캠퍼스·연결 Stop·담당 시설·보행 연결·검증 상태를 기록하고 시설별 커버리지 표에 누락/불가 사유를 남긴다. 인근 거점을 공유할 때 보행 거리와 service_needs별 접근성을 검증하고 기준은 설정으로 명시한다. 커버리지 미확인 시설을 서비스 가능으로 표시하지 않는다.

필요하면 인하공전 시설을 별도 campus_id로 추가하되 인하대와 연결되는 차량/보행 경로 및 서비스 영역을 검증한다. 지하철역처럼 캠퍼스 경계 밖일 수 있는 시설도 동일하게 검증하며 미확인 도로를 직선 연결하지 않는다. 기본 필수 목록 및 대표 시설 커버리지 검증은 선택적 교외 확장과 구분해 완료한다.

### 좌표 계약

원본은 WGS84 경도/위도, 연산은 미터 단위 투영 좌표다. 적합한 CRS와 검증된 원점을 고정하고 `Transformer(..., always_xy=True)`로 축 순서를 명시한다.[S2]

```text
Python: x=E-E0, y=N-N0, z=height
Unity : (Python.x, Python.z, Python.y)
heading_rad: 북쪽=0, 동쪽=π/2, 시계방향 양수
Python XY 이동 방향: (sin(heading), cos(heading))
Unity Y 회전: heading_rad*180/π
단위: m, m/s, m/s², rad, s
```

왕복 오차≤0.01 m와 기준점 3개의 Unity 축/축척을 검사한다. 이는 실제 지도 정확도 보장이 아니다. 길이/면적은 투영 좌표에서 계산한다.

`biryong_plaza_front`는 **비룡플라자 앞**이다. polygon·진입 edge·경계 밖 대기/대체 거점을 사람이 확인한다. 이름만으로 좌표를 만들지 않으며 geometry 누락 시 실제 지도 검증을 실패시킨다.

이동지원은 장애인/비장애인 이분법이나 진단명이 아닌 `service_needs`의 `requires_step_free`, `wheelchair_slots`, `boarding_assistance`로 표현한다. **승하차장↔출입구의 보행 접근**도 검사하고 대체 거점 접근 실패를 성공으로 세지 않는다. 계단은 모든 차량에 금지하며 보행 접근성과 차량 통행성을 구분한다.

한 랜드마크(건물 포함)에 일반 출입구 Stop과 접근 가능한 출입구 Stop을 함께 둘 수 있다. 각 Stop은 `landmark_id`, 해당 시 `building_id`·`entrance_id`, 연결 보행 경로와 검증된 접근성 정보를 가진다. 모바일은 출발/목적 landmark_id를 보내고 서버가 `service_needs`·차량 능력·현재 폐쇄·보행 접근성을 검사해 실제 pickup/dropoff Stop을 결정한다. 일반 이동도 접근 가능한 Stop을 이용할 수 있으며 유형별 강제 분리는 하지 않는다. 적합한 Stop이 없으면 이유와 함께 대기/거부하고 조건을 완화하지 않는다. 선택·대체된 출입구와 이유를 승객에게 알린다.

## 4. 모델과 상태 전이

ID는 문자열, 시간은 run 이후 초/Asia/Seoul 표시다. 누락·NaN·음수·미지원 enum은 거부하고 미확인은 `null/unknown`이다.

| 모델 | 필수 정보 |
|---|---|
| Request | id, service_type, created_s, pickup/dropoff_landmark_id, pickup/dropoff_stop_id, latest_arrival_s, service_needs, party_size/cargo_kg, status, vehicle_id |
| Vehicle | id, 지원 서비스·정원·휠체어/화물 용량, footprint, 속도, pose, battery_wh, mission/motion_state |
| Landmark | id, 이름/별칭, 유형, campus_id, 담당 시설, stop_ids, 보행 연결, 검증 상태 |
| Stop | id, landmark_id, building_id/entrance_id(해당 시), geometry, 보행 연결·접근성·승하차 슬롯, 검증 상태 |
| Passenger | id(합성), request_id, origin/destination_landmark_id, pedestrian_id, 이동 단계, vehicle_id |
| Zone | id, geometry, 유효면적, profile·정책, 검증 상태 |
| Route | id, edge_ids/polyline, map_version, cost_snapshot_id, 생성 tick, 비용·ETA·사유 |
| RunManifest | run_id, seed, code_commit, project_version, config/map hash, 정책·의존성 버전, 접속 클라이언트 버전/빌드 식별자 |

`service_type`은 PASSENGER/CARGO다. 이동지원·마감은 요청 속성이다. 실명·학번·장애 진단명은 수집하지 않는다. `service_needs`는 `requires_step_free: bool`, `wheelchair_slots: int≥0`, `boarding_assistance: bool`을 가지며 일반 이동은 false/0/false다. 휠체어 슬롯 요구가 있으면 계단 없는 접근도 요구한다. 이동지원 우선순위는 이 요구조건에서 도출하며 별도 장애 여부 플래그를 두지 않는다. 요청 생성 시 Stop은 미결정일 수 있으나 VALIDATED 전에 서버가 확정한다. 승객 요청은 모바일·PC·자동 생성 모두 랜드마크 입력 계약을 사용한다. 배송의 Stop 직접 지정은 허용하되 같은 서버 검증을 거친다. 출발=목적 랜드마크 및 미등록/미검증 랜드마크의 승객 요청은 명시적 사유로 거부한다.

```text
CREATED → VALIDATED → QUEUED → ASSIGNED → PICKUP_SERVICE
        → IN_TRANSIT → DROPOFF_SERVICE → COMPLETED
분기: REJECTED / CANCELLED / EXPIRED / FAILED
재배차: ASSIGNED → QUEUED (탑승/적재 전만)

차량 임무: IDLE → TO_PICKUP → PICKUP_SERVICE → TO_DROPOFF
              → DROPOFF_SERVICE → IDLE
운행: DRIVING / YIELDING / REPLANNING / EMERGENCY_STOP / WAITING_RESOURCE
충전/고장: TO_CHARGER / CHARGING / OUT_OF_SERVICE
```

임무/운행 상태를 분리하고 전이 tick·원인을 기록한다. 거점 도착·정지·슬롯 확보 후 승하차/상하차 시간을 소모한다. 탑승 후 임무를 빼앗지 않으며 취소/고장은 안전 거점 정차·운영자 처리로 전환해 적재물을 보존한다.

## 5. 배차·배송 알고리즘

초기는 접근성 승객/승객·화물 전환/화물 차량 각 1대다. 전환은 빈 차량만 허용한다. 제원은 합성 설정이다.

### 가능성 → 요청 순서 → 차량 선택

랜드마크별 후보 Stop을 service_needs로 필터링하고 거점/서비스 영역, 차량 능력·정원/적재, 접근성, 픽업/운송 경로, 완료 후 충전 거점까지 에너지를 먼저 검사한다. 요청 검증에서 확정한 Stop을 배차 시 재검증하며 대체 시 변경 이유를 기록·통지한다. 차량이 바쁘면 대기, 구조적으로 수행 불가면 이유와 함께 거부한다. 요청은 새 입력/차량 해제/고장/중요 경로 변경과 매 1초마다 재평가한다.

```text
wait_s = now_s-created_s
completion_time_s = now_s+estimated_remaining_duration_s
slack_s = latest_arrival_s-best_feasible_completion_time_s
          (마감이 없으면 +∞)
priority = base + wait_s/aging_interval_s
           + deadline_weight*clamp(1-slack_s/deadline_window_s, 0, 2)
```

초기는 base=일반 승객 2/화물 1/이동지원 3, deadline_weight=2, deadline_window_s=600인 정책 가정이다. 동률은 생성 시각→ID로 결정한다. 순서대로 적합 유휴 차량 중 아래 비용이 최소인 차량을 할당한다.

`J(v,r)=픽업 소요시간 + 0.5×운송 소요시간 + 2×예상 지각시간 + 에너지 환산시간 + 희소 차량 사용 패널티`

비용은 초 단위이며 경로·승하차 ETA와 선택 내역을 기록한다. 동일 요청의 priority를 모든 차량 점수에서 빼도 차량 선택은 같으므로 요청 순서에 적용한다. 예약·배정은 원자적으로 처리한다.

### 공정성·고장·배터리

초기 aging은 120초, 장기 대기는 600초다. 장기 대기 요청이 있으면 적합 유휴 차량의 매 3번째 배차 기회를 가장 오래 기다린 요청에 준다. 능력이 없는 차량까지 분모에 넣지 않는다. 접근성 차량의 일반 임무 투입은 이동지원 대기와 다른 차량 ETA를 고려한다. 조건을 완화해 배차하지 않는다.

과부하에서는 대기를 보장하지 않고 서비스별 p95·미완료·만료를 보고한다. 마감은 soft deadline으로 초과 예상/취소 선택을 제공하되 자동 삭제하지 않는다.

승차 전 재배차는 고장/경로 단절/뚜렷한 ETA 개선에 한정하고 cooldown을 둔다. 탑승/적재 후 재배차는 금지한다. 실패는 요청·화물의 현재 상태와 이유를 보존한다.

에너지는 `거리×Wh/m + 대기전력(W)×초/3600`에 적재 보정을 더한 단순 모델이다. 픽업→목적지→도달 가능한 충전소와 예비량 15%를 확인한다. 수행 중 부족하면 새 배차 중지·안전 거점/지원 상태로 전환하며 순간이동하지 않는다.

FIFO+거리와 우선순위+ETA를 비교한다. 선택 batch 매칭은 미할당/불가능 조합을 처리한다. 합승은 M6 후 픽업 선행·구간 용량·우회 한도를 지키는 삽입법으로 확장한다.

## 6. 전역 경로: 자체 A*

**제약 필터 → 비용 snapshot → A* → geometry 복원 → 주행 검증** 순서다. 통행 금지·차체 폭·접근성·폐쇄는 edge 제외 조건이며 큰 비용으로 대체하지 않는다. edge 중간 출발은 임시 노드로 연결하고 순간이동하지 않는다.

```text
cost_e = length_m/allowed_speed_mps
         + crowd_penalty_s + zone_penalty_s + expected_wait_s
h(n) = straight_line_distance(n,goal)/global_max_speed_mps
```

추가 비용은 모두 0 이상, 제한속도는 전역 상한 이하다. 정적 snapshot에서 허용적 휴리스틱을 유지한다. heap/g-score/parent edge/stale entry 제거를 자체 구현하고 동일 비용 Dijkstra로 검증한다. NetworkX A*는 참고·검증용이다.[S6]

1차는 관측/단기예측 비용을 탐색 동안 고정한다. 미래 전체의 시간의존 최적 경로가 아니며 시간확장 그래프는 선택이다.

경로용 혼잡 비용은 1초마다 갱신한다. 폐쇄/위험은 즉시, 일반 재계획은 최소 5초 간격이다. 안전한 기존 경로의 변경은 `개선≥10% AND 개선≥15초`일 때만 적용한다. cache key에는 지도·차량/요청 profile·cost snapshot을 포함한다.

도달 불가·시작=목적지·일방/평행 edge를 테스트한다. 경로가 없으면 대체/대기/실패를 반환하며 직선 이동·금지구간 통과는 금지한다.

## 7. 시간대·보행자·혼잡 밀도

기준은 **09:00, 10:30, 12:00, 13:30, 15:00**, 기본 혼잡 창은 각각 전후 15분이다. 사용자 관찰을 설정으로 보존하며 공식 수업 시작/종료 시각으로 확정하지 않는다. 평일/휴일/축제 profile은 분리한다.

simulation clock을 사용하며 일시정지 시 생성·대기도 멈춘다. 시각 이동은 새 run/중간 tick 실행으로 처리하고 인구를 둔 채 시계만 바꾸지 않는다.

### 생성률과 관측

초기 관측은 합성 정답 상태(ground truth)다. 생성률 λ는 명/초, 밀도 ρ는 명/m²다. 초기 전/후 흐름은 아래와 같다. 시간과 σ는 모두 초이며 pulse는 `[Tk-900,Tk+900]` 밖에서 0이다.

```text
G(t;μ,σ) = exp(-0.5*((t-μ)/σ)^2)
pulse_k(t) = 0.6*G(t;Tk-300,240)+0.4*G(t;Tk+300,240)
λ_z(t) = base_rate_z+peak_rate_z*zone_multiplier_z*Σpulse_k(t)
spawn_count ~ Poisson(λ_z(t)*dt)
```

비룡플라자 앞 multiplier=2.0 등 계수는 가정이다. 생성 그룹을 포털/OD에 매핑해 보행 graph로 이동시키며 인물을 zone마다 재생성하지 않는다. 도착/이탈로 제거하고 생성 한도로 생략된 인원도 집계한다.

승객으로 생성되는 각 보행자는 출발 랜드마크에서 다른 목적 랜드마크를 선택해 호출한다. 수요 생성은 설정된 랜드마크 OD·생성률·service_needs 분포와 독립 RNG를 사용하며 특정 목적지/지원 요구를 임의로 균등하다고 가정하지 않는다. 혼잡·횡단용 배경 보행자도 랜드마크 OD로 보행하되 차량 호출 여부는 시나리오로 구분한다. 보행자 300명이 모두 동시에 호출하는 것은 별도의 부하 시나리오다.

승객은 서버가 정한 pickup Stop까지 검증된 보행 연결로 접근·대기하고, 차량 정지·슬롯 확보·승차 시간 경과 후 탑승한다. 탑승 중에는 별도 보행 개체로 이동/밀도/충돌에 중복 포함하지 않고 차량 탑승 인원으로 보존한다. 하차 후에는 dropoff Stop에서 목적 랜드마크 출입구까지 검증된 보행 연결을 이용한다. 요청 COMPLETED는 기존대로 하차 완료이며, 랜드마크 최종 도착은 별도 기록해 보행 접근 실패를 전체 이동 성공으로 집계하지 않는다. 출발/도착의 짧은 보행도 생략할 경우 검증된 동일 지점 fixture로만 처리하고 순간이동으로 접근성을 우회하지 않는다.

초기는 waypoint·개별 속도·간격 유지 모델이다. 난입은 차량 길을 가로지르는 보행 경로 이벤트로 만든다. 가림은 관측 레이어에서 구현하고 차량 내부에 사람을 순간 생성하는 극단 테스트와 구별한다.

### 현재 밀도·단기예측

유효면적은 zone에서 건물 등 비보행 면적을 뺀 m² 값이다. 면적 미확인은 unknown으로 처리한다. Unity 렌더링에서 생략한 사람도 집계/충돌에는 포함한다.

```text
rho_observed = zone 내 World 보행자 수 / usable_area_m2
alpha_dt = 1-exp(-실제_갱신간격_s/ema_tau_s)
rho_ema = alpha_dt*rho_observed+(1-alpha_dt)*previous_ema
N_prior(t) ≈ integral(λ_z(s), s=t-mean_dwell_s .. t)
rho_prior(t) = N_prior(t)/usable_area_m2
rho_route(t) = max(rho_observed(t),
                  0.7*rho_ema(t)+0.3*rho_prior(t+60))
```

유입/체류 근사의 근거를 기록하고 예상/현재 인원을 더하지 않는다. 계획기에 미래 난입/숨은 위치를 주지 않는다. zone 외에 차량 주변 격자도 검사하며 안전에는 원시 관측을 쓴다. 화면은 합성 관측/예상/실측 보정 여부를 구분한다.

## 8. 비룡플라자 앞 정책

**시간 기반 선제 우회 + 현재 밀도 제한 + 안전한 예외**를 적용한다.

| 상태 | 초기 기준(명/m²) | 동작 |
|---|---|---|
| NORMAL | <0.2 | 검증된 통행 조건 적용 |
| CAUTION | 0.2~0.5 미만 | 감속·혼잡 비용 |
| AVOID | 0.5~1.0 미만 또는 혼잡 창 | zone 제외 우회 우선 |
| CLOSED | 현재 관측≥1.0 또는 명시적 폐쇄 | 신규 진입 금지 |

상태는 CLOSED가 우선한다. 수치는 **합성 정책값이며 실제 군중 안전 기준이 아니다.** 폐쇄는 즉시, 해제는 현재 밀도<0.7이 10초 유지될 때다.

AVOID에서는 통과 edge를 제외하고 우회를 찾는다. 안전·접근성 조건을 만족하고 시간 증가가 `max(90초,기준 경로시간×0.3)` 이하면 우회한다. 그렇지 않으면 zone 통과 길이에 비례한 양의 패널티를 넣어 비교하고 저속 통과 사유를 기록한다. penalty 계수는 설정으로 둔다.

CLOSED는 우회가 길어도 진입하지 않는다. 내부 차량은 급회전/임의 후진하지 말고 정지 또는 검증된 최소위험 이탈을 수행한다. 내부 목적지는 경계 밖 대체 거점을 제안하되 이동지원 보행 접근성을 다시 검사한다. 불가능하면 대기/수행 불가다.

사유는 `CROWD_AVOIDANCE/ZONE_CLOSED/NO_ACCESSIBLE_ALTERNATIVE`로 구별한다. polygon 미확인은 합성 fixture로만 검증한다.

## 9. RRT·제어·난입 안전

차량은 **저속 차동구동형 unicycle 모델**이다. 자동차형은 별도 곡률 제한 모델이 필요하다. waypoint 방위와 heading 차이의 비례제어로 각속도를 제한하고 급회전 시 감속한다. `x+=v*sin(heading)*dt`, `y+=v*cos(heading)*dt`, `heading+=omega*dt`로 적분하며 가감속·회전 footprint를 검증한다.

자체 RRT는 전역 경로 주변 검증된 corridor 안에서 `sample→nearest→steer→segment 검사→attach→goal 연결`을 수행한다. 차체+여유로 장애물을 팽창시키거나 swept footprint를 검사하며 끝점만 검사하지 않는다. 초기 확장 1,500회, step 0.5 m, goal bias 0.1, 반경 15 m는 조정할 가정이다.

2D 후보에 시작/끝 heading·회전·속도 제한을 적용하고 시간 매개화한 뒤 보행자 단기 예측과 충돌 검사한다. 예측은 관측 위치/속도의 등속 근사+불확실성 여유이며 2초 이후를 확정하지 않는다. 짧은 유효 구간만 실행하고 매 tick 재검증한다. 실패하면 정지/양보하며 사람 사이 불가능한 틈이나 금지 영역을 쓰지 않는다.

선택 RRT*는 rewire/subtree 비용을 갱신하며 유한 시간 최적성을 보장하지 않는다. 기하 경로가 동적 안전을 보장하지 않으며 OMPL도 기하/제어 계획을 구분한다.[S7]

### 독립 안전 감시

계획기와 독립해 매 tick 명령을 제한한다. Nav2 감시 구성을 참고하되 안전 인증은 아니다.[S8]

```text
d_stop = v*reaction_time_s+v²/(2*brake_decel_mps2)+margin_m
```

정지 여유와 횡단 충돌을 함께 검사한다. 횡단 TTC는 상대 위치 r/속도 u에 대해 `|r+u*t|²=R²`의 최소 비음수 근이다. 현재 겹침은 0, 근 없음은 +∞다. R은 두 물체+여유이며 사각 차체는 swept footprint로 최종 확인한다.

순서는 **관측 유효성→감속/정지 판단→제어 제한→동시 적분/충돌 검사**다. 속도를 순간 0으로 바꿔 제동거리를 숨기지 않는다. 일반/비상 감속을 구분하고 급한 요청도 안전 한도를 바꾸지 않는다.

노후 관측·무효 계획·충돌 위험이면 정지하고, 안전 여유 1초 유지와 경로 재검증 후 출발한다. tick 사이 충돌도 검사한다. 물리적으로 피할 수 없는 난입은 최소위험 반응과 실패를 기록하며 무조건 충돌 0을 보장하지 않는다.

## 10. 차량 경합·교착

좁은 양방향 통로는 두 방향 edge를 하나의 resource로 묶어 1대만 점유한다. 교차로 충돌 영역도 예약하며 차량은 정지선 밖에서 기다린다. 출구 공간이 없으면 진입하지 않는다.

진입 전 lease와 실제 점유를 분리한다. **차량이 내부에 있으면 lease 만료만으로 재배정하지 않는다.** 이탈 확인 후 해제하며 내부 고장은 자원 폐쇄/우회로 처리한다. 요청 순서로 중재하고 보행자 안전이 우선한다.

정체 timeout에 대기 관계를 검사해 안전한 대피/재계획 또는 교착을 보고한다. 임의 후진·삭제는 금지한다. 최적 경로해가 아닌 예약 관리다.

## 11. 시계·동시성·통신

`dt=0.05 s`(20 Hz)를 고정한다. 배속은 처리 tick 수만 늘린다. snapshot은 10 Hz, 혼잡 예측/배차는 1 Hz, 현재 밀도·폐쇄·안전·이동은 매 tick이다.

```text
입력 적용 → 보행 생성/관측 → 혼잡·배차 → 계획 요청/결과 검증
→ 자원 중재 → 안전 제한 → 차량/보행 동시 적분
→ 충돌·거점 서비스 → 기록·상태 발행
```

상태 변경은 simulation owner 하나만 한다. handler/worker는 queue를 사용하고 CPU 계획 작업이 안전 tick/통신을 막지 않게 한다. 결과에 vehicle/mission version, map version, snapshot tick을 넣고 오래된 결과를 거부한다.

재현 모드는 고정 iteration·결과 적용 tick·독립 RNG를 사용한다. 작업이 늦으면 wall-clock만 늦추고 tick을 생략하지 않는다. 실시간 모드는 deadline 실패 시 정지/기존 유효 경로를 쓰고 결과를 기록한다. wall-clock timeout 차이를 seed만으로 재현된다고 주장하지 않는다.

`/ws/sim`은 명령/상태, `/health`는 준비 상태, `/api/scenarios`는 조회다. PC·모바일 모두 이 서버를 사용하며 계약 테스트부터 구현한다. 서버가 세션의 역할·요청 소유권을 검증한다. PC 관제만 시뮬레이션 제어/시나리오 이벤트를 허용하고 모바일은 자신의 요청 생성·조회·취소만 허용한다.

```json
{
  "schema_version": 3,
  "type": "request.create",
  "run_id": "demo",
  "message_id": "msg_1",
  "seq": 1,
  "payload": {
    "request_id": "req_1",
    "service_type": "PASSENGER",
    "pickup_landmark_id": "landmark_a",
    "dropoff_landmark_id": "landmark_b",
    "party_size": 1,
    "service_needs": {
      "requires_step_free": true,
      "wheelchair_slots": 1,
      "boarding_assistance": true
    },
    "latest_arrival_s": 900
  }
}
```

ID는 합성 예시다. 서버는 `command.ack/reject`와 applied_tick을 답한다. 출력은 `world.snapshot`, `request/route/zone.updated`, `error`다. snapshot에는 tick·sim_time·map version·구독 범위를 넣는다. PC는 전체 관제 상태, 모바일은 자신의 요청·확정 Stop·배차 차량·ETA·표시 경로·임무/운행 상태·판단 사유를 받는다. 모바일에 전체 군중/heatmap/debug나 다른 승객 요청을 보내지 않는다. 역할별 출력은 같은 WorldState의 투영이며 시뮬레이션 개체를 제거하지 않는다. `request.updated`에는 Stop 결정/변경 이유, `route.updated`에는 route_id·변경 tick·사유를 포함한다. 운행 상태/사유는 snapshot에도 넣어 이벤트 유실 시 복구한다.

통신 계약 v3의 JSON Schema와 Python/C# fixture를 함께 갱신한다. v1/v2 호환이 필요하면 경계 어댑터에서 기존 Stop/건물과 landmark_id의 검증된 대응으로 명시적으로 변환하고 미지원 버전은 거부한다. §12 설정의 schema_version은 별도 계약이다.

`run_id+message_id`로 멱등성을 보장하고 같은 ID의 다른 payload는 거부한다. 적용 tick은 서버가 정하며 클라이언트의 임의 pose 덮어쓰기는 금지한다. 재연결은 해당 역할·구독 범위의 완전한 snapshot/seq로 복구하고, 모바일은 서버가 확인한 요청 소유권으로 기존 요청을 재조회한다. Unity는 오래된 상태를 폐기하고 주 스레드에서만 객체를 수정한다.

1초간 갱신이 없으면 보간 중지/단절을 표시한다. PC 제어 클라이언트 lease 소실 시 interactive는 제동 후 정지, headless는 계속된다. 모바일은 제어 lease를 소유하지 않으며 단절이 전체 WorldState를 정지시키거나 요청을 자동 취소하지 않는다. 재접속 전 ETA/상태를 최신으로 표시하지 않는다.

## 12. 초기 설정 계약

`configs/simulation.yaml` 초기값이다. 변경하면 hash를 갱신한다. 지도·차량 상세 파일도 본 제약을 따른다.

```yaml
schema_version: 1
mode: interactive
seed: 42
clock:
  timezone: Asia/Seoul
  start_time: "08:30:00"
  fixed_dt_s: 0.05
transport:
  adapter: websocket
  host: "127.0.0.1"
  port: 8765
  snapshot_hz: 10
crowd:
  source: user_observation_and_synthetic_model
  transition_times: ["09:00", "10:30", "12:00", "13:30", "15:00"]
  window_before_s: 900
  window_after_s: 900
  pre_peak_offset_s: -300
  post_peak_offset_s: 300
  sigma_s: 240
  ema_tau_s: 5
  forecast_horizon_s: 60
  max_agents: 1000
  biryong_multiplier: 2.0
zones:
  biryong_plaza_front:
    polygon_ref: "zones.geojson#biryong_plaza_front"
    require_verified_geometry: true
    caution_density: 0.2
    avoid_density: 0.5
    close_density: 1.0
    reopen_density: 0.7
    reopen_hold_s: 10
    detour_extra_s: 90
    detour_ratio: 0.3
dispatch:
  interval_s: 1
  aging_interval_s: 120
  fairness_wait_s: 600
  fairness_every_n_assignments: 3
  battery_reserve_fraction: 0.15
planning:
  global_replan_interval_s: 5
  improvement_ratio: 0.1
  improvement_s: 15
  local_max_iterations: 1500
  local_step_m: 0.5
  local_goal_bias: 0.1
  local_radius_m: 15
safety:
  default_max_speed_mps: 2.0
  caution_max_speed_mps: 0.8
  reaction_time_s: 0.2
  normal_decel_mps2: 0.8
  emergency_decel_mps2: 2.0
  margin_m: 1.0
  resume_clear_s: 1.0
```

각 zone/vehicle의 base/peak rate, dwell, 면적, footprint/정원/회전속도는 필수다. validator가 누락을 거부하며 0으로 몰래 대체하지 않는다.

## 13. Unity·MCP 구현

도로 mesh·건물 윤곽·거점을 먼저 만들고 외관보다 연결/폭/축척을 검증한다. 개체 view는 ID 기반 pool과 렌더 보간을 사용한다. `Scripts/Common`은 DTO·네트워크·좌표 변환·MapData·VehicleView를 공유하고 PC/Mobile은 각 UI·카메라·표현을 담당한다.

`CampusWorld`에는 공통 지도·건물·Stop·차량 프리팹 참조를 두고 Additive 역할 씬에서 필요한 view를 구성한다. 네트워크 세션/상태 저장소는 앱당 하나만 생성하며 씬 전환 시 중복 접속·카메라·EventSystem·이벤트 구독을 막는다. PC/모바일 빌드 설정과 품질 프로파일을 분리하고 PC 전용 군중·heatmap·debug 자산이 모바일에서 불필요하게 로드되지 않게 한다.

- **PC_Operator:** 전체 3D 디지털트윈에서 모든 차량·보행 혼잡/heatmap, A* 전역/RRT 지역 경로, 요청/배차, 임무·ETA·배터리, 대기 이유·지표와 시간/배속/정지·승객/배송 입력·시뮬레이션 제어를 제공한다.
- **Mobile_Passenger:** 같은 3D 세계를 단순화한 승객 앱이다. 일반 이동/이동지원 선택→필요 조건 입력→출발/목적 랜드마크 선택→호출→서버가 선택한 출입구 확인→내 차량 위치·ETA·경로·상태 확인 흐름과 취소를 제공한다. 쿼터뷰 중심으로 랜드마크 선택·제한된 이동/확대와 내 차량 따라보기를 제공한다. 랜드마크 검색/목록 선택도 지원해 정밀한 3D 터치를 필수로 하지 않는다.
- 모바일은 저LOD 건물/차량과 선별 표시를 사용하고 내 차량·승하차 출입구·경로를 강조한다. 전체 군중/heatmap/RRT 샘플·debug는 렌더링하지 않는다. 생략된 개체도 서버의 밀도·충돌·안전 계산에는 유지한다.
- 두 UI는 색상 외 문자/아이콘을 제공한다. 모바일은 서버 상태를 “승객에게 이동 중 / 탑승 대기 / 목적지로 이동 중”으로 안내하고 `CROWD_AVOIDANCE`는 “혼잡 구간을 피해 경로를 변경했어요”, `YIELDING`과 보행자 원인이 함께 있을 때는 “보행자 통행을 기다리고 있어요”로 표시한다. 실제 서버의 tick·사유에 근거하며 정지 원인을 추측하거나 클라이언트에서 자율주행 판단을 생성하지 않는다.

PC의 난입·공사·고장 버튼은 서버 이벤트를 생성한다. heatmap에 단위/예상·관측 구분/비룡플라자 경계를 표시하며 실제 학생 위치 추적으로 표현하지 않는다. OSM 출처를 PC·모바일 지도 화면에 표시한다.[S9]

MCP는 외부 도구 연결 계층이다.[S10] 검증한 Unity MCP로 importer/컴포넌트 검사/테스트를 호출하되 MCP 없이도 같은 Editor 메뉴가 동작해야 한다. 개별 객체를 AI가 반복 배치하기보다 map hash 기반의 결정적·멱등적 일괄 importer를 만든다.

MCP 권한·경로를 제한하고 외부 명령/삭제/비밀키 노출을 막는다. 수정 후 diff와 테스트를 확인한다.

## 14. 검증·비교 실험

| 테스트 | 상황 | 인수 조건 |
|---|---|---|
| T01 | 승객/화물 정상 1건 | 픽업→운송→인도/하차, 상태·시간 일치 |
| T02 | 용량/접근성 부적합 | 후보 제외/거부 사유, 적재 음수 없음 |
| T03 | 정적 graph 100개 쌍 | A* 비용=Dijkstra, edge/geometry 검증 |
| T04 | 다섯 피크 전/중/후 | 생성률 증감·정확한 시각·밀도 단위 |
| T05 | 비룡플라자 AVOID | 허용 증가량 내 안전한 대안 우회 |
| T06 | CLOSED·대안 없음/내부 목적지 | 진입 금지·접근성 확인 후 대체/대기 |
| T07 | 충분한 정지거리 난입 | 충돌 없이 제동·안전 후 재출발 |
| T08 | 불가피한 난입 | 실패 기록, 순간정지 금지 |
| T09 | RRT 실패/지연/낡은 결과 | 안전 루프 유지·무효 경로 거부 |
| T10 | 대향 경합/내부 고장 | 이중 점유·만료에 의한 재배정 없음 |
| T11 | 중복/취소/연결 단절 | 멱등성·적재 보존·재접속 복구 |
| T12 | 과부하/배터리 부족 | 서비스별 대기·실패 보고 |
| T13 | 같은 manifest 재현 실행 | 상태/event digest·지표 재현 |
| T14 | 지도/폭/CRS 누락·오류 | 실제 지도 실패, silent fallback 없음 |
| T15 | 같은 건물의 일반/접근 가능 Stop·요구조건 조합 | 양 끝 Stop의 서버 결정·차량/보행 접근 검증, 대안 없음 사유, 조건 완화 없음 |
| T16 | PC·모바일 동시 접속·모바일 호출/취소/재접속 | 같은 run/tick 요청·차량 상태 일치, 중복 요청 없음, 소유권/권한 검사, 모바일 단절 시 엔진 지속 |
| T17 | 내 차량 혼잡 우회·보행자 정지 | 서버 사유·경로 변경과 승객 메시지 일치, ETA/상태 복구, 단절 표시 |
| T18 | 공통 지도 수정·Additive·모바일 단말 | 양쪽 좌표/Stop 일치, 세션/카메라 중복 없음, 저LOD·선별 표시 및 UI≥30 FPS 목표 측정 |
| T19 | 필수 랜드마크·대표 시설 커버리지·OD | 필수 목록/시설 대응 누락 없음, service_needs별 Stop·보행 연결·차량 경로 확인, 불가 사유 기록, 동일 출발/목적 거부 |
| T20 | 보행 승객의 호출→승차→하차→랜드마크 도착 | 요청/승객/차량 연계, 인원 보존·보행 중복 집계 없음, 랜드마크 도착/하차 완료 구분, 접근 실패의 성공 집계 없음 |

좌표/비용/FSM/밀도/TTC/제동/배차를 단위 검증한다. 공통 JSON을 Python/C#에서 검사하고 Unity EditMode는 importer/DTO, PlayMode는 두 역할의 표시/정지/재접속·Additive 로딩을 테스트한다. T15는 일반 이동도 접근 가능한 Stop 이용 가능, 폐쇄/접근성 unknown/휠체어 용량 부족을 포함한다. T16은 모바일의 관제 명령·타인 요청 접근 거부와 PC lease 소실을 구분한다. 모바일 표시 생략 전후 동일 manifest의 서버 상태/event digest가 유지되는지 검증한다.

### 실험 체계

`B0=거리 A*+FIFO`, `B1=혼잡 A*+FIFO`, `B2=혼잡 A*+우선/공정 배차`를 동일 지도/요청/보행 OD·난입 조건으로 비교한다. 우회 정책과 RRT 유무는 별도 실험으로 분리한다. 모든 군의 필수 안전 규칙은 같다.

수요/보행/난입/계획 RNG를 분리하고 같은 외생 이벤트 파일을 쓴다. 상호작용한 보행 궤적은 달라질 수 있다. 시나리오당 20 seed를 목표로 조정/평가를 분리한다. warm-up/초기 인구와 미완료/실패를 보고한다.

| 지표 | 정의 |
|---|---|
| 대기 | 픽업 서비스 시작−생성, 서비스별 평균/p95 |
| 이동/완료 | 목적지 도착−픽업 종료 / 인도·하차 종료−생성 |
| 정시 완료 | 마감 내 완료/마감 있는 유효 요청, 미완료 포함 |
| 처리량 | 시간당 승객 수·완료 배송 건수 구분 |
| 공차율 | 빈 차량 거리/총 거리 |
| 혼잡 노출 | 주행 중 밀도×dt 적분·zone 진입 횟수 |
| 안전 | 충돌 사건/시도·차량 km, 최소 clearance/TTC, 비상제동 |
| 접근성 | 조건 충족 완료/유효 이동지원 요청 |
| 연산 | 계획/배차/tick p50·p95, timeout, 장비 |

같은 접촉을 중복 집계하지 않으며 분모 0은 null이다. seed 원자료·분산/신뢰구간·실패를 남기고 실제 성능으로 일반화하지 않는다.

## 15. 실행 계약·개발 품질

M0에서 아래 CLI를 구현한다. **현재 존재/실행된 명령이라고 가정하지 않는다.** 개발 의존성도 선언한다.

```bash
python -m pip install -e "./backend[dev]"
python -m campus_sim.cli validate-map --map maps/inha_v1
python -m campus_sim.cli run --scenario scenarios/offpeak.yaml --seed 42
python -m campus_sim.cli serve --scenario scenarios/biryong_peak.yaml
python -m campus_sim.cli evaluate --suite scenarios/evaluation.yaml --seeds 0:20
python -m campus_sim.cli replay --run artifacts/runs/RUN_ID
python -m pytest backend/tests
python -m ruff check backend
python -m mypy backend/src
```

`0:20`은 seed 0~19다. serve 기본은 127.0.0.1:8765이며 외부 공개하지 않는다. 모바일 실기기 시연은 명시적으로 허용한 LAN 주소에 바인딩하고 단말에서 접근 가능한 서버 주소를 설정한다. 예를 들어 PC LAN IP가 192.168.0.10이면 모바일 URL은 `ws://192.168.0.10:8765/ws/sim`이며 모바일의 localhost는 PC를 가리키지 않는다. PC 방화벽은 사설망의 해당 포트만 허용하고 Wi-Fi 단말 격리 여부를 확인한다. 원격 시연 시 인증/origin/메시지 크기·빈도·역할 권한을 제한한다. 서버 worker 증가로 simulation owner를 중복 생성하지 않는다.

run별 manifest/config, events.jsonl, snapshots, metrics.json, requests/vehicles.csv를 저장한다. 이벤트에는 tick·개체 ID·이유를 넣는다. 큰 로그는 Git에서 제외한다. 다른 seed/지도/코드를 섞지 않는다.

Python 타입/예외·C# 모델/화면 책임을 분리하고 정책 숫자는 config에 둔다. PR은 요구사항 ID, 변경 이유, 정상/실패 테스트, 한계를 기록한다. 릴리스 전 VERSION·문서·서버·Unity PC/모바일의 프로젝트 버전 일치, 네 자리 형식·증가/하위 초기화 규칙, CHANGELOG 및 플랫폼 매핑을 검사한다. Editor 변경 시 두 빌드와 공통 DTO/씬 회귀 검사를 수행한다. 사용자 변경을 덮어쓰거나 테스트를 지워 통과시키지 않는다.

## 16. 단계·완료 기준·에이전트 절차

| 단계 | 구현 | 종료 조건 |
|---|---|---|
| M0 | 기존 코드 조사, 네 자리 VERSION/CHANGELOG·Unity 버전 관리·계약·CLI·합성 지도 | 버전 원본/동기화 검사·고정 시계·무창 실행·fixture 통과 |
| M1 | 차량 1대·A*·승객/배송·공통 CampusWorld/PC Unity | T01/T03, 요청→완료 흐름 |
| M2 | 실제 지도/보정·랜드마크 목록/Stop·대표 시설 커버리지·비룡플라자 | T14/T19, 최소 6개 거점 fixture에서 전체 필수 목록으로 확장·출처/검증 기록 |
| M3 | 다섯 시간대·인구/예측·heatmap·우회 | T04~T06 |
| M4 | 난입·제동·RRT·재출발 | T07~T09 |
| M5 | 3대·service_needs/Stop 결정·마감/공정 배차·충전·예약 | T02/T10~T12/T15/T20, 중복 배정·승객 중복 집계 없음 |
| M5a | Mobile 통합·Additive 역할 씬·승객 3D UI·통신 v3 | T16~T18, 실기기 호출→완료·판단 안내·재접속 |
| M6 | B0~B2·재생·측정·OSS/IOSS 증빙 | T13 및 M5a 통과, 대표 시설 커버리지 보고·PC/모바일 동시 데모·원자료·한계 보고 |
| M7 | 검증된 교외·ROS2·RRT*·합승 | M6 통과 후 개별 비교 |

M0에서 공통 DTO/역할 경계를 정하되 모바일 본 통합은 핵심 A*/RRT·안전·배차 이후 M5 전후에 붙이고 M5a에서 완료한다. 모바일 UI가 핵심 알고리즘 개발을 선행하지 않는다. 단계마다 fixture→도메인→통합→UI→실패 처리→측정 순서다. 지도/시뮬레이션, 알고리즘/배차, Unity/통신, 검증으로 분담하고 계약·시계부터 합의한다.

시연은 비혼잡 배송→이동지원→10:30 혼잡/우회→난입 제동/재탐색→다중 배차→동일 seed 비교 순서다. 이동지원은 모바일의 동일 랜드마크 선택에 대해 service_needs별 출입구 결정을 확인하고, 우회/제동은 PC의 판단 근거와 모바일의 승객 안내를 동시에 보여준다. 화면만 성공하고 로그가 없으면 완료가 아니다.

에이전트는 git status/코드를 읽고 요구사항 ID·최소 변경을 정한다. 정책/계약 변경은 ADR에 남기고 단위/통합·lint/타입·Unity 검사를 실행한다. 불가능한 검사는 미실행으로 보고한다.

`docs/implementation_status.md`에 단계·명령/결과·결함·다음 작업을 기록한다. 외부 업로드/IOSS/유료 API는 승인 범위에서만 수행한다.

## 17. OSS·IOSS·참고 근거

IOSS 인정 요건은 M0에서 담당자/수업 자료로 확인한다. OSS 사용만으로 충족했다고 쓰지 않으며 이 문서가 선행작 중복 없음을 증명하지는 않는다.

`docs/oss_usage.md`에 저장소·고정 버전·라이선스·사용 위치·수정·원저작자 표시를 기록한다. 자체 구현/참고/그대로 사용한 부분과 IOSS 선행작에서 추가한 기여를 구분한다.

OSM의 ODbL·출처/파생 데이터 조건을 확인하고 README/지도/시연 자료에 출처를 표시한다.[S9] 코드와 지도 데이터 라이선스를 동일시하지 않는다. Unity 자체를 오픈소스로 부르지 않으며 다른 지도 형상/이미지를 허락 없이 추출·배포하지 않는다.

2026-09-09 확인한 공식 근거다. Codex AGENTS 기본 합산 한도는 32 KiB이므로 확장 시 잘림을 확인한다.[S11] 이 v0.2.0.0 통합본은 32 KiB를 초과하므로 실제 에이전트 지침으로 배치할 때 적용 환경의 로딩 한도를 확인하고 필요하면 상세 설계를 별도 문서로 분리한다.

[S1]: https://osmnx.readthedocs.io/en/stable/user-reference.html
[S2]: https://pyproj4.github.io/pyproj/stable/api/transformer.html
[S3]: https://fastapi.tiangolo.com/advanced/websockets/
[S4]: https://github.com/endel/NativeWebSocket
[S5]: https://github.com/Unity-Technologies/ROS-TCP-Endpoint
[S6]: https://networkx.org/documentation/stable/reference/algorithms/generated/networkx.algorithms.shortest_paths.astar.astar_path.html
[S7]: https://ompl.kavrakilab.org/planners.html
[S8]: https://docs.nav2.org/rolling/configuration_and_development/configuration_guide/core_servers/collision_monitor/configuring_collision_monitor_node/
[S9]: https://www.openstreetmap.org/copyright
[S10]: https://modelcontextprotocol.io/docs/2026-07-28/getting-started/intro
[S11]: https://developers.openai.com/codex/guides/agents-md/
