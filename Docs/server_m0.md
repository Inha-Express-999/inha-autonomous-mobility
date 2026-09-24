# Python 서버 M0 기반

프로젝트 0.2.2.0 · 문서 정합화 2026-09-24 · 서버 구현 범위는 0.1.6.0 기반

`backend/`는 Unity 표현 계층과 분리된 Python 서비스의 시작점이다. 현재는 합성 지도와 메모리 상태만 사용하며, 실제 지도 승인, Unity Physics, 센서 관측, A*와 RRT는 포함하지 않는다. `/v1/client/ws`는 schema-v3 snapshot과 모바일 요청 생성/취소 명령을 Unity에 전달하는 초기 통합 경로다.

## 실행

```powershell
python -m pip install -e ".\backend[dev]"
campus-sim serve
```

서버는 기본적으로 `127.0.0.1:8765`에서만 수신한다. 모바일 실기기 연결을 위해 LAN에 열지 않는다.

Unity Bootstrap Inspector에서 `useFixture`를 해제하면 WebSocket transport를 선택한다. PC Editor/Player는 기본 loopback 주소를 사용할 수 있다. 모바일 기기는 `serverWebSocketUrl`에 PC의 LAN IP를 지정해야 한다. 현재 endpoint는 인증·TLS가 없는 synthetic alpha이므로 신뢰 네트워크에서만 사용한다. `passengerSubscriberId`는 인증 수단이 아니다.

## 현재 API

- `GET /health`
- `GET /v1/landmarks`
- `POST /v1/requests`
- `GET /v1/owners/{owner_id}/requests`
- `POST /v1/requests/{request_id}/cancel?command_id=...&owner_id=...`
- `WS /v1/client/ws` — 첫 `subscribe` 이후 `connected` 및 0.5초 간격 `snapshot`을 전송한다.
- Mobile_Passenger 구독은 `create_request`·`cancel_request` 메시지를 보내고 `command_ack`를 받는다. 요청 소유자는 구독의 `subscriberId`에서 서버가 가져온다. Operator role은 현재 command가 허용되지 않는다.

`command_id`는 요청 생성과 취소를 멱등하게 처리한다. 같은 명령을 다시 보내면 상태 전이를 반복하지 않고 처음 결과를 반환한다.

## 다음 단계

초기 planner 개발용 합성 6-stop RoadGraph 계약, 자체 Dijkstra/A* 및 route-compare CLI가 추가되었다. Unity–Python WebSocket은 Mobile 요청 UI의 create/cancel command·ACK와 snapshot을 지원한다. 서버는 V01을 A* 경로 geometry와 edge 속도제한에 따라 이동시키며 픽업·하차 각 2초 후 상태를 진행시킨다. 위치·경로·ETA는 합성 데모값이며 실제 지도, Unity Physics, 차량 제어를 나타내지 않는다. Python 서비스·HTTP·ASGI WebSocket 테스트 20개가 통과했다. 다음은 Unity transport/UI의 실제 server 연동, 끊김/재접속·세션·부하 안정성 검증이다. 세부 근거와 한계는 [서버 안정성 메모](server_stability.md)를 따른다. 센서 기반 안전·RRT는 차량 Physics/관측 계약 이후다.

```powershell
$env:PYTHONPATH = "backend/src"
python -m campus_sim.cli route-compare --map maps/fixtures/campus-synthetic-6.json
python -m campus_sim.cli route-compare --map maps/fixtures/campus-synthetic-6.json --require-step-free
python -m campus_sim.cli serve --host 127.0.0.1 --port 8765
python -m unittest discover -s backend/tests -p "test_*.py" -v
```

Windows에서 editable install을 사용한다면 저장소 루트에서 `python -m pip install -e ".\backend[dev]"` 실행 후 `campus-sim route-compare --map maps/fixtures/campus-synthetic-6.json`으로 같은 CLI를 호출할 수 있다. 이 비교 결과는 합성 fixture만 설명한다.
