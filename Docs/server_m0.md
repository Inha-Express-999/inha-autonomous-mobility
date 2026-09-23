# Python 서버 M0 기반

프로젝트 0.1.6.0 · 2026-09-23

`backend/`는 Unity 표현 계층과 분리된 Python 서비스의 시작점이다. 현재는 합성 지도와 메모리 상태만 사용하며, 실제 지도 승인, Unity Physics, 센서 관측, A*와 RRT는 포함하지 않는다.

## 실행

```powershell
python -m pip install -e ".\backend[dev]"
campus-sim serve
```

서버는 기본적으로 `127.0.0.1:8765`에서만 수신한다. 모바일 실기기 연결을 위해 LAN에 열지 않는다.

## 현재 API

- `GET /health`
- `GET /v1/landmarks`
- `POST /v1/requests`
- `GET /v1/owners/{owner_id}/requests`
- `POST /v1/requests/{request_id}/cancel?command_id=...&owner_id=...`

`command_id`는 요청 생성과 취소를 멱등하게 처리한다. 같은 명령을 다시 보내면 상태 전이를 반복하지 않고 처음 결과를 반환한다.

## 다음 단계

Unity WebSocket transport와 공통 JSON fixture, 실제 지도/Stop 검증, 차량 배터리·경로 비용을 반영한 배차, A*와 센서 기반 안전·RRT를 순서대로 추가한다.
