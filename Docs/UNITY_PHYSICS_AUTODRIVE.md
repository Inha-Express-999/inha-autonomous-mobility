# Python–Unity 물리 자동 경로 운행 · 0.5.0.0

PC_Bootstrap의 기존 Additive CampusTerrain 차량(V01)을 사용한다. PC 차량 버튼으로 Cinemachine을 선택하고 목적 Stop 후보를 고른 뒤 자동 운행 시작을 누른다. 수동 throttle/steering UI나 키보드 운전은 없다. 사용자 요청으로 차량별 자동 운행 중지 버튼과 API를 제거했다. 추적 해제는 현재 차량 부근의 자유 지도 카메라로 전환하며 운행 명령을 보내지 않는다. 차량을 다시 선택하면 추적한다. 운영지도 보기는 캠퍼스 전체 구도로 돌아간다.

Python은 같은 demo run_id의 정적 도로에서 진행 방향에 맞는 edge 중간 시작과 A* 경로를 구성한다. 실제 위치를 노드로 옮기지 않는다. 시작 offset ≤2m·방향 차이 ≤60°를 개발 가정으로 제한하며 충족하지 않으면 거부한다. Pure Pursuit와 전방 Raycast 정지거리 조건으로 속도/조향을 계산한다. 차로 protocol 2는 직선 최대 5m/s(18km/h)이며 경로의 도로 제한값도 지킨다. 곡률 미리보기와 횡가속도 1.2m/s² 가정으로 교차로 전에 감속하고 목적지까지 남은 거리로 정지 속도를 제한한다. protocol 1은 이전 0.8m/s를 유지한다. 모든 속도/제동 값은 시뮬레이션 튜닝이며 실측 안전 제원이 아니다.

Unity는 자기 차량 위치/속도/heading, WheelCollider 위치로 측정한 wheelbase, Collider 반폭, 최대 조향각과 전방 7개 Raycast를 전송한다. 익명 hit의 ego 기준 횡/전방 위치와 센서 원점의 전방 거리를 포함해 계획 차로의 차체 통과 영역에 해당하는 장애물을 검사한다. 새 hit 필드가 없는 구형 관측은 모든 ray의 최솟값을 사용한다. raycast는 차량 Collider로 측정한 앞면에서 시작하고 자신/Trigger는 제외한다. 접지가 없거나 ray hit buffer가 포화되면 관측을 무효화한다. 타 차량/보행자 실제 Transform·이름/미래 궤적은 전송하지 않는다. 7-ray 전방 관측은 완성된 LiDAR/Radar SensorRig가 아니다.

`/v1/demo/unity/vehicles/{vehicle_id}/control` WebSocket에서 schema_version=1/2·run_id·map hash·단조 tick과 단일 물리 소유권을 검사한다. Python 명령은 0.3초 동안만 유효하며 클라이언트 자체 watchdog과 기존 VehicleController timeout을 함께 사용한다. 연결/관측/명령 이상에는 제동한다. 명령 없이 새 경로로 재개하지 않는다. 장애물 제동은 제동 시작 당시의 해제 거리를 고정해 감속 후 반복 출발하는 현상을 막는다.

같은 세션의 `/v1/demo/unity/vehicles/{vehicle_id}/mission`은 Stop 후보 경로를 시작한다. 이전 차량별 `/stop`은 제거되어 404를 반환한다. 이 API 제거로 프로젝트 버전의 B를 올렸다. 물리 운행은 1배속이며 기존 웹 시뮬레이션 일시정지와 연동한다. 물리 소유 차량은 기존 시간 기반 배차에서 제외한다. 웹은 `motion_source=unity_physics` 차량의 실제 localization을 표시하고 시간 보간을 적용하지 않는다. 배터리는 기존 가상값으로 명시한다. 나머지 가상 차량/승객 Baseline은 보존한다.

이 단계는 실제 위치를 따른 자동 경로 운행 연결이다. 승객 요청→배차→픽업→하차의 모든 상태 전이를 물리 도착으로 판정하는 통합, 동적 RRT 회피, 차량 간 예약, 접근성·도로 폭 승인 및 실차 안전은 완료 범위가 아니다. 통행·Stop 후보는 기존 `osm_geometry_simulation` 가정을 유지한다. V02/V03 실제 차량은 추가하지 않는다.

실행 검증 결과: artifacts/unity_auto_0.4.9.0 참조.
