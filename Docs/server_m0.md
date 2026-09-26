# Python 서버 M0 기반

프로젝트 0.2.4.0 · 문서 정합화 2026-09-25 · 서버 구현 범위는 0.1.6.0 기반

`backend/`는 Unity 표현 계층과 분리된 Python 서비스의 시작점이다. `campus-sim serve --map <path>`로 경로 그래프 fixture를 선택할 수 있으며, 현재 loader가 허용하는 것은 합성 fixture뿐이다. 실제 지도 승인 데이터와 Unity Physics 실행은 포함하지 않는다. SensorObservation 입력 경계와 제한된 synthetic localization safety gate는 구현 중이며 RRT는 포함하지 않는다. `/v1/client/ws`는 schema-v3 snapshot, 모바일 승객 명령, PC 운영자의 승객/화물 명령과 localization을 Unity에 전달한다.

## 실행

```powershell
python -m pip install -e ".\backend[dev]"
campus-sim serve
campus-sim serve --map maps/fixtures/campus-synthetic-benchmark-11.json
```

서버는 기본적으로 `127.0.0.1:8765`에서만 수신하고 6-stop synthetic graph를 사용한다. `--map`으로 다른 schema-valid synthetic fixture를 선택할 수 있다. OSM candidate JSON, unverified graph, 실제 지도용 RoadGraph는 loader에서 거부된다. 모바일 실기기 연결을 위해 LAN에 열지 않는다.

Unity Bootstrap Inspector에서 `useFixture`를 해제하면 WebSocket transport를 선택한다. PC Editor/Player는 기본 loopback 주소를 사용할 수 있다. 모바일 기기는 `serverWebSocketUrl`에 PC의 LAN IP를 지정해야 한다. 현재 endpoint는 인증·TLS가 없는 synthetic alpha이므로 신뢰 네트워크에서만 사용한다. `passengerSubscriberId`는 인증 수단이 아니다.

Unity Build Profiles에 `Synthetic Preview - Windows` 개발용 프로필을 추가했다. 이 프로필은 `RoadGraphSyntheticPreview` 한 장면만 시작 씬으로 빌드하며, PC 운영 UI에서 승객/화물 요청을 만들고 local Python 서버와 합성 V01/V02/V03 차량 follower의 상태를 관찰하기 위한 것이다. Windows/Android 기본 프로필은 변경하지 않았다. Profile asset의 Unity Editor 인식 및 실제 Player 빌드는 아직 확인되지 않았고, profile을 사용할 Unity batch test는 Licensing Client 초기화 문제로 시작되지 않았다. 실제 지도 운행·안전 동작을 제공하는 빌드가 아니다.

프로필로 Player를 빌드한 뒤 아래 launcher를 실행하면 Python 서버를 loopback `127.0.0.1:8765`에서 시작하고 `/health`에서 synthetic mapVersion을 확인한 다음 Player를 연다. Player를 종료하면 launcher가 자신이 시작한 서버 프로세스만 종료하며 로그는 임시 폴더에 남긴다. 포트가 이미 사용 중이면 다른 서버에 접속하거나 종료하지 않고 실행을 거부한다.

```powershell
.\AgentScripts\RunSyntheticPreview.ps1 `
  -PythonPath ".\.venv\Scripts\python.exe" `
  -UnityPlayerPath ".\Build\InhaSyntheticPreview.exe"
```

`-PythonPath`에는 `backend[dev]` 의존성을 설치한 Python 실행 파일을 전달한다. Player 빌드 경로는 사용자가 Build Profiles 창에서 지정한 출력 경로와 일치시킨다. 이 launcher는 합성 데이터 전용이며 외부 인터페이스에 서버를 공개하지 않는다.

## 현재 API

- `GET /health` — 프로젝트/schema 버전, 현재 `map_version`, 지도 `data_status`, node/edge 수를 반환한다. 실행한 합성 fixture를 확인하기 위한 값이며 지도 승인·Unity 연결·안전 상태를 보증하지 않는다.
- `GET /v1/landmarks`
- `POST /v1/requests`
- `GET /v1/owners/{owner_id}/requests`
- `POST /v1/requests/{request_id}/cancel?command_id=...&owner_id=...`
- `WS /v1/client/ws` — 첫 `subscribe` 이후 `connected` 및 목표 0.1초 간격 `snapshot`을 전송한다. 10Hz는 테스트 socket cadence일 뿐 실 네트워크 성능 측정은 아니다.
- Mobile_Passenger는 승객 `create_request`·`cancel_request`를 보내고 `command_ack`를 받는다. 소유자 필터는 구독의 `subscriberId`를 사용하지만 이는 인증이 아니다.
- PC_Operator는 합성 환경에서 승객/화물 요청, 운영자 취소와 `ego_localization`을 보낼 수 있다. PC 운영자는 합성 fixture의 요청 소유자 역할이며 서버 인증/세션 권한을 구현한 것은 아니다.

`command_id`는 요청 생성과 취소를 멱등하게 처리한다. 같은 명령을 다시 보내면 상태 전이를 반복하지 않고 처음 결과를 반환한다.

## 다음 단계

초기 planner 개발용 합성 RoadGraph 계약, 자체 Dijkstra/A* 및 route-compare CLI가 있다. 서버 앱은 `--map`으로 다른 합성 graph fixture를 읽어 Landmark/Stop 목록과 요청 경로를 구성한다. OSM candidate map과 실제 지도는 여전히 loader에서 거부한다. Unity–Python WebSocket은 모바일 승객 요청과 PC 운영자 승객/화물 요청·취소, localization ACK 및 snapshot을 지원한다. PC 전용 `sensor_observation` frame도 sensor별 64 detections 상한과 현재 ego pose의 map/session/tick 결합, polar/local 좌표 및 LIDAR/RADAR 필드를 검사하고 메모리에 저장한다. Python은 localization 차량에 sensor freshness/validity, forward-sector stopping-distance gate, 1초 clear resume hold를 적용하지만 Unity Raycast 실행·인지 정확도·TTC/RRT·물리 제동은 미완료다. 기본 API 서버는 V01~V03의 합성 상태를 각 A* 경로 geometry와 정적·시간대 edge 지연을 포함한 segment speed profile에 따라 독립 진행시키고 픽업·하차 각 2초 후 상태를 갱신한다. 새 WebSocket 통합 테스트가 PC 위치 보고부터 요청 생성, pickup/dropoff 상태 전이, 목적지 경로 polyline·segment speed profile 및 `COMPLETED`까지 검증한다. 위치·경로·ETA는 합성값이며 실제 지도, Unity Physics, 안전 제어를 나타내지 않는다. OSM review validator는 공식 접근 근거와 현장 측정 증거를 요구하며 source의 명시적 차량 통행 금지 태그를 KEEP 후보에서 차단한다. 현재 전체 Python backend 82개 및 지도도구 9개 테스트(총 91개) 통과(Starlette/httpx deprecation 경고 1개)이며 Ruff가 통과했다. Unity Player UI·Physics, 실지도 graph, Unity Physics Raycast 검증, full TTC/RRT/물리 안전, 인증·부하 검증은 남아 있다. 세부 근거와 한계는 [서버 안정성 메모](server_stability.md)를 따른다.

```powershell
$env:PYTHONPATH = "backend/src"
python -m campus_sim.cli route-compare --map maps/fixtures/campus-synthetic-6.json
python -m campus_sim.cli route-compare --map maps/fixtures/campus-synthetic-6.json --require-step-free
python -m campus_sim.cli serve --host 127.0.0.1 --port 8765 --map maps/fixtures/campus-synthetic-6.json
python -m unittest discover -s backend/tests -p "test_*.py" -v
```

Windows에서 editable install을 사용한다면 저장소 루트에서 `python -m pip install -e ".\backend[dev]"` 실행 후 `campus-sim route-compare --map maps/fixtures/campus-synthetic-6.json` 또는 `campus-sim serve --map maps/fixtures/campus-synthetic-6.json`으로 실행한다. 현재 두 명령 모두 합성 fixture만 다룬다.
