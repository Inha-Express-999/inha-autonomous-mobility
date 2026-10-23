# CampusTerrain 서버 지도 연결

프로젝트 0.4.5.0 · 2026-10-23. 이번 단계는 허브·노드 배치와 읽기 전용 서버 연결이다.

## 씬에서 확인

기존 `Assets/CampusSim/Scenes/CampusTerrain.unity`를 연다. 새 씬은 없다. Hierarchy의 `Server Map | Web-aligned Nodes and Hubs` 아래에 다음을 배치했다.

- `Road Nodes`: 웹과 같은 도로 노드 187개. 파란 마커.
- `Home Hubs (web road anchors)`: V01 정문 / V02 후문 / V03 운동장 옆 주차장. 주황 마커 3개.
- `Stop Candidates (access unverified)`: 승하차 후보 14개. 초록 마커.
- 유형별 합친 표시 mesh 3개. 마커에는 Collider/Rigidbody가 없다.

각 객체를 선택하면 Inspector에서 serverId/nodeId/원본 XZ/높이 출처/해당 차량 ID를 확인할 수 있다. Gizmos를 켜고 선택하면 ID 라벨을 볼 수 있다. Root의 `showEdges`는 방향 그래프의 연결을 Scene View에서 참고용으로 표시한다. 이동 경로나 실제 도로 메시를 새로 만드는 기능은 아니다.

V03 허브는 웹과 같은 `osm:10058484941`의 도로 앵커다. 기존에 제작된 주차장 건물/베이를 옮기거나 실제 출발 위치로 승인하지 않았다. 웹 기준과 주차 베이를 잇는 미확인 직선 도로도 생성하지 않았다.

## 서버와 연결

```powershell
.\scripts\start_web_demo.ps1
```

Unity에서 CampusTerrain을 Play하면 root의 `CampusServerMap`이 기본 주소 `http://127.0.0.1:8765`에 접속한다. 같은 서버의 `/demo/` 웹 페이지를 열면 동일한 run ID, 시간·일시정지 상태·차량 수·완료 수를 Unity 좌상단에서 확인할 수 있다. 요청·배속 조작은 기존 웹에서만 한다. Unity는 지도 및 상태를 GET으로 조회한다.

다른 PC/LAN 서버는 Inspector의 `serverUrl`을 해당 주소로 바꾼다. HTTP 지도 조회 단계이며 새 WebSocket·사용자 인증·실단말 배포를 구현한 것은 아니다. 차량 Transform, 기존 컨트롤러, WheelCollider, 스폰 및 운송 완료 판정은 연결하지 않는다.

서버가 꺼져도 저장된 노드가 남는다. 연결 실패는 Offline으로 표시하며 5초 후 재시도한다. 서버 지도가 저장된 지도와 다르면 연결을 승인하지 않으므로 아래 순서로 갱신한다.

## 지도 갱신

```powershell
$env:PYTHONPATH = "$PWD/backend/src;$PWD/.utmp/fifo-dijkstra-deps"
python -m campus_sim.map_builder
python -m campus_sim.unity_map --output Assets/CampusSim/Data/server_map.json
```

기존 개발 의존성 또는 backend를 설치한 Python 환경을 사용한다. 첫 줄의 `.utmp/fifo-dijkstra-deps`는 이 작업 환경의 기존 의존성 경로다. 다른 환경에서는 backend를 설치하고 `backend/src`를 사용한다.

Unity 메뉴 `Campus > Server Map > Import Web Nodes Into CampusTerrain`을 실행한다. 기존 씬이 수정 중이면 먼저 저장해야 한다. 배치 모드는 같은 Editor에서 `-batchmode -nographics -projectPath <repository> -executeMethod CampusServerMapAuthoring.Import -quit`으로 실행한다. 동일 지도를 다시 가져와도 root/노드가 중복되지 않는다.

## 높이·접근성·성능 범위

204개 마커 중 200개는 도로 메시에, 4개는 Terrain에 표시용 투영했다. Terrain 항목은 road node `osm:4741759648`, `osm:2643032901`, `clip:expanded:1390060991:3:0.579325503739`와 마지막 노드에 연결된 기숙사 1 Stop 후보다. 모든 항목이 Terrain 범위 안에 있다. 도로 표면 누락/매칭은 후속 확인 대상이며, 노드를 옆 도로로 임의 이동하지 않았다.

표시 높이는 실측 고도·경사·차량 통행·보행 접근성 승인으로 사용하지 않는다. 모든 후보는 operationally_verified=false다. 색상과 별도로 hierarchy/ID/Inspector metadata를 제공한다. 렌더링은 추가 mesh 3개, 전용 재질 3개, 그림자 없음이며 개별 노드의 렌더러/Collider는 없다. PC/모바일 FPS·프레임 시간·실단말 성능은 측정하지 않았다.

실제 배치 통계는 `artifacts/campus_server_nodes_validation.json`에 기록한다.

## 검증 결과

Windows / Python 3.14.4 / Unity 6000.3.21f1에서 아래를 확인했다.

- Python 전체 208개 통과. 기존 Starlette TestClient/httpx deprecation 경고 1개. 신규 3개는 전체 노드 왕복 좌표 오차 0.01m 이내, 웹 지도/허브/Stop/vehicle 바인딩 일치, 오프라인 export와 API 응답 일치, 조회의 웹 상태 불변과 hash 변경 감지를 검사한다.
- Unity EditMode 57개 통과. 추가 3개는 DTO 거부 조건, 원격 hash/좌표 불일치 거부, 실제 CampusTerrain의 204개 앵커와 X/Z 0.01m 이내 일치·3개 메시·Collider/Rigidbody 없음 검사다.
- Unity PlayMode 2개 통과. 추가 HTTP 검사는 같은 export를 제공하는 loopback TCP fixture에 UnityWebRequest로 접속해 웹 run/time/차량 수를 수신하고, 서버 단절 후 기존 배치가 유지되며 GET 외 명령이 없음을 확인했다. Python API의 실제 응답 계약은 별도 FastAPI 테스트로 검증했다. 실제 Python 서버와 Unity의 동시 화면 시연을 측정한 결과는 아니다.
- 같은 importer를 배치로 다시 실행해 씬 파일 SHA-256이 바이트 단위로 동일함을 확인했다. root/노드 중복 생성이 없다.
- 기존 씬의 YAML 문서는 삭제되지 않았다. 기존 문서 중 바뀐 것은 새 root를 추가한 SceneRoots 목록 하나뿐이며 나머지 기존 차량/도로/건물 문서는 동일하다. Unity 저장에 따른 문서 순서 변경은 있을 수 있다.
- Ruff src/tests 44개 파일 lint/format 통과. 차량 프리팹과 VehicleController 변경 없음. Unity 실행 전후 analytics define은 원래 설정과 같음을 확인했다.

재현 명령:

```powershell
python -m pytest backend/tests -vv -p no:cacheprovider --basetemp=.utmp/server-map-pytest-01 --junitxml=.utmp/server-map-python.xml
python -m ruff check backend/src backend/tests
python -m ruff format --check backend/src backend/tests
```

Unity는 `-batchmode -nographics -projectPath <repository> -runTests -testPlatform EditMode` 및 `PlayMode`로 실행하고 각각 `-testResults`/`-logFile`을 지정했다. 테스트 소켓 접근이 가능한 실행 권한을 사용했다. [결과 요약](../artifacts/server_map_0.4.5.0/summary.json)과 같은 폴더의 Python/Unity 원본 XML을 보관한다. 앞 절에서 명시한 Player·성능·실제 주행 범위는 미검증이다.
