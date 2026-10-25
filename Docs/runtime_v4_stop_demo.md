# 버전 4 정지 명령 통신 시험

2026-10-25: hello 이후 envelope에 welcome의 session_id가 필요하다. 실행 중인 예전 서버를
종료하고 현재 코드로 재시작한다. 복구 시험은 `python -m campus_sim.transport.recovery_demo`.
상세 내용은 [8주차 작업 기록](transport_week8_progress.md)을 참고한다.

2026-10-18 작업. 현재 algorithms의 기존 버전 3 서버를 건드리지 않는 시험용 모듈이다.
Unity 대신 FakeSimulationPeer가 차량/센서를 보내고 정지 명령의 결과를 반환한다.
실제 차량 주행, C# 직렬화, 물리 적용 검증은 아직 하지 않았다.

## 가장 간단한 실행

기존 Python 환경을 사용하는 CMD 또는 PowerShell에서 실행한다.

```text
cd C:\Users\leeta\Documents\Codex\inha-autonomous-mobility
python -m campus_sim.transport.demo
```

이 명령은 별도 서버 창 없이 같은 프로세스의 ASGI WebSocket을 시험한다.
개발용 의존성인 pytest/httpx 등이 설치된 현재 개발 환경을 전제로 한다.

정상 출력에서 확인할 값:

```json
{
  "status": "ok",
  "vehicle_id": "V01",
  "source_observation_count": 2,
  "result": "APPLIED",
  "evidence": "FAKE_PEER_TRANSPORT_ONLY",
  "physical_application_verified": false,
  "transport": "IN_PROCESS_ASGI_WEBSOCKET"
}
```

run_id/session_id/command_id는 실행마다 달라진다.
APPLIED는 시험용 클라이언트가 보낸 결과이며, Unity의 실제 제동을 뜻하지 않는다.

## 실제 로컬 WebSocket으로 시험

다음은 **CMD용** 명령이다. 두 창에서 같은 프로젝트 폴더와 같은 Python 환경을 사용한다.
아래 토큰은 로컬 시험 전용 공개 예시다. 외부 서버 자격 증명으로 사용하지 않는다.

첫 번째 CMD 창:

```bat
set CAMPUS_RUNTIME_TEST_TOKEN=local-stop-demo-only-20261018
python -m uvicorn campus_sim.transport.gateway:local_app --factory --host 127.0.0.1 --port 8766 --ws-max-size 65536
```

두 번째 CMD 창:

```bat
set CAMPUS_RUNTIME_TEST_TOKEN=local-stop-demo-only-20261018
python -m campus_sim.transport.demo --url ws://127.0.0.1:8766/experimental/runtime/ws
```

PowerShell을 사용할 때에는 위 `set ...` 대신 아래 문법을 사용한다.

```powershell
$env:CAMPUS_RUNTIME_TEST_TOKEN = 'local-stop-demo-only-20261018'
```

서버 종료는 첫 번째 창에서 Ctrl+C. 기존 8765 서버와 별개인 8766 포트를 사용한다.
외부 접속용 배포 서버가 아니며 브라우저 Origin이 있는 접속은 거부한다.

## 왕복 순서

1. 서버 설정 토큰으로 인증 후 hello를 보내고 welcome의 run/session/차량 권한을 확인한다.
2. ego.state를 보낸다. 아직 관측이 없으므로 MISSING_OBSERVATION 정지 명령을 받는다.
3. 같은 pose/tick/time의 LiDAR와 Radar를 보낸다. TEST_STOP_ONLY 정지 명령을 받는다.
4. 최신 command_id에 VALIDATED를 보고한다.
5. 같은 command_id에 APPLIED 및 applied_tick을 보고한다.
6. 연결이 닫히면 pending 명령과 관측을 삭제한다. 재접속에서는 새 session으로 시작한다.

시험용 결과 로그는 앱의 `app.state.result_log`에 최근 256개까지 남는다.
현재 디스크 영속화와 운영자 로그 API는 없다. 데모 출력에는 실행/차량/명령 ID를 표시한다.

## 검증 명령과 결과

```text
python -m pytest backend/tests/test_runtime_transport.py -q -p no:cacheprovider
python -m pytest backend/tests AgentScripts/MapData -q -p no:cacheprovider
python -m ruff check backend/src/campus_sim/transport backend/tests/test_runtime_transport.py
```

- 전용 테스트: 30 passed
- 기존 회귀 포함: 497 passed, 1 warning, 21.99초
- Ruff: 통과
- 인프로세스 데모: status ok
- 실제 로컬 TCP WebSocket: status ok (별도 임시 포트에서 실행 후 종료)
- 경고는 기존 Starlette/httpx 사용 중단 예정 경고다.

테스트는 인증 실패, 모바일의 관측 전송 차단, 다른 차량/이전 run/역순 seq,
불일치 pose/time, 잘못된 JSON/NaN/미지원 필드, 버퍼 제한, 명령 만료,
VALIDATED/APPLIED 분리, 중복 결과, 재접속의 이전 명령 거부 및 idle 종료를 포함한다.

## 다음 작업

1. 최장현님과 C# Encode/Decode, Pump 및 연결 중단 이벤트를 합의하고 같은 예제를 C#에서 검사한다.
2. 이상엽님과 frame/Mission/ControlStep 및 단위/제어 형식을 확정한다.
3. 실제 Unity의 정지 전용 어댑터와 별도 wall-clock watchdog을 연결한다.
4. 서버 종료 중 실제 차체의 제동, 오래된 명령의 미적용을 영상과 물리 로그로 확인한다.
5. 이후 HTTP ServiceFacade와 PC/모바일 상태 projection, 수동 재접속 전체 상태 복구를 연결한다.

계약의 임시 선택과 한계는 [ADR 0004](ADR/0004-isolated-runtime-v4-stop-prototype.md)에 기록했다.
이번 작업은 커밋/태그를 생성하지 않았으므로 프로젝트 릴리스 버전은 올리지 않았다.
