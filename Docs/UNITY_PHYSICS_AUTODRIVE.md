# Python–Unity 물리 자동 경로 운행 · 0.4.9.0

PC_Bootstrap의 기존 Additive CampusTerrain 차량(V01)을 사용한다. PC 차량 버튼으로 Cinemachine을 선택하고 목적 Stop 후보를 고른 뒤 자동 운행 시작을 누른다. 수동 throttle/steering UI나 키보드 운전은 없다. 자동 운행 중지와 웹 일시정지는 제동 명령으로 이어진다.

Python은 같은 demo run_id의 정적 도로에서 진행 방향에 맞는 edge 중간 시작과 A* 경로를 구성한다. 실제 위치를 노드로 옮기지 않는다. 시작 offset ≤2m·방향 차이 ≤60°를 개발 가정으로 제한하며 충족하지 않으면 거부한다. Pure Pursuit와 전방 Raycast 정지거리 조건으로 속도/조향을 계산한다. 초기 최대 0.8m/s·lookahead 3m·제동 여유는 튜닝 가정이며 실측 제원이 아니다.

Unity는 자기 차량 위치/속도/heading, WheelCollider 위치로 측정한 wheelbase, 설정된 최대 조향각과 전방 7개 Raycast 거리만 전송한다. raycast는 차량 Collider로 측정한 앞면에서 시작하고 자신/Trigger는 제외한다. 접지가 없거나 ray hit buffer가 포화되면 관측을 무효화한다. 타 차량/보행자 실제 Transform·이름/미래 궤적은 전송하지 않는다. 7-ray 전방 관측은 완성된 LiDAR/Radar SensorRig가 아니다.

`/v1/demo/unity/vehicles/{vehicle_id}/control` WebSocket에서 schema_version=1·run_id·map hash·단조 tick과 단일 물리 소유권을 검사한다. Python 명령은 0.3초 동안만 유효하며 클라이언트 자체 watchdog과 기존 VehicleController timeout을 함께 사용한다. 연결/관측/명령 이상에는 제동한다. 명령 없이 새 경로로 재개하지 않는다.

같은 세션의 `/v1/demo/unity/vehicles/{vehicle_id}/mission`은 Stop 후보 경로를 시작하고 `/stop`은 중지한다. 물리 운행은 1배속이며 웹 일시정지와 연동한다. 물리 소유 차량은 기존 시간 기반 배차에서 제외한다. 웹은 `motion_source=unity_physics` 차량의 실제 localization을 표시하고 시간 보간을 적용하지 않는다. 배터리는 기존 가상값으로 명시한다. 나머지 가상 차량/승객 Baseline은 보존한다.

이 단계는 실제 위치를 따른 자동 경로 운행 연결이다. 승객 요청→배차→픽업→하차의 모든 상태 전이를 물리 도착으로 판정하는 통합, 동적 RRT 회피, 차량 간 예약, 접근성·도로 폭 승인 및 실차 안전은 완료 범위가 아니다. 통행·Stop 후보는 기존 `osm_geometry_simulation` 가정을 유지한다. V02/V03 실제 차량은 추가하지 않는다.

실행 검증 결과: artifacts/unity_auto_0.4.9.0 참조.
