# ADR 0007: 웹 지도와 CampusTerrain의 읽기 전용 연결

상태: 채택 · 2026-10-23 · 프로젝트 0.4.5.0

사용자 요청에 따라 새 씬을 만들지 않고 CampusTerrain에 웹과 같은 허브·노드를 배치한다. 차량 조작은 보류한다.

- 기존 웹 `DemoSession`의 service에서 `/v1/demo/unity-map`을 투영한다. 별도 시뮬레이션 owner를 만들거나 `/v1/requests`의 독립 서비스를 섞지 않는다. Unity는 같은 `/v1/demo/state`의 run/time/차량 수/완료 수를 읽는다.
- 이 단계는 정적 지도 HTTP 조회와 읽기 전용 상태 폴링이다. 최종 WebSocket·센서·제어 명령 계약을 대체하지 않으며 기존 Unity snapshot schema_version=3도 변경하지 않는다.
- Unity 지도 projection은 독립 schema_version=1이다. AEQD 그래프를 기존 Unity legacy X/Z로 명시 변환한다. 187개 노드/402개 방향 간선/14개 Stop 후보/3개 허브의 ID와 연결을 보존한다.
- 3대의 허브는 정문·후문·운동장 옆 주차장의 웹 home node와 같다. V03는 주차 베이가 아니라 기존 OSM 도로 node10058484941이며 기존 CampusVehicleHub 구조물과 구분한다. 검증되지 않은 베이 연결선은 만들지 않는다.
- 지형 높이는 Unity 도로 MeshCollider/기존 Terrain에서 표시용으로만 투영한다. 서버의 y=0은 높이 미제공 계약이며 평면 주행/경사 검증 결과가 아니다. 도로 메시에 투영되지 않은 항목은 terrain_visual_only로 기록한다.
- 메뉴 기반 importer는 기존 CampusTerrain의 소유 subtree만 생성/갱신한다. geometry hash와 marker가 같으면 기존 marker를 재생성하지 않는다. 별도 씬·Collider·차량·제어·자동 배차 입력은 추가하지 않는다.
- 마커 204개는 메타데이터 Transform이며 렌더링은 유형별 합친 mesh 3개/전용 재질 3개로 구성한다. 공유 원본 재질 변경, 마커별 Renderer/Collider, 무조건적인 instancing·static batching 중복은 피한다. FPS 개선을 측정했다고 주장하지 않는다.
- 내장 TextAsset은 서버가 꺼져도 배치 검토를 유지한다. 원격 지도의 버전/hash/노드 좌표/허브·Stop 바인딩이 다르면 연결을 거부하고 재생성·재가져오기를 안내한다. 단절 시 배치와 차량 상태를 조작하지 않는다.

사용·검증 방법과 한계는 [CampusTerrain 서버 지도 안내](../CAMPUS_SERVER_MAP.md)에 기록한다.
