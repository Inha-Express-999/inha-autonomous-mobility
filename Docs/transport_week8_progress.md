# 8주차 연결 안정성 작업 기록

2026-10-25, algorithms 로컬 작업. 커밋/푸시/팀원 브랜치 병합 없음.

## 완료한 독립 작업

1. 기존 v3 코드는 보존하고 v4 정지 전용 실험 모듈의 세션 검사를 보완했다.
   이전 구현은 명령 ID 재사용을 막았지만, 새 연결에 이전 ego를 재전송하는 경우는
   명시적인 session_id 없이 구분할 수 없었다. 이제 hello 이후 모든 입력에 welcome에서
   받은 session_id가 있어야 하며, 다른 세션의 입력은 버퍼를 변경하기 전에 거부한다.
2. 명령 만료 경계(현재 시각 == valid_until_s)도 만료로 처리했다.
3. 연결/종료 시각과 이유를 최근 256개까지 메모리에 기록한다.
4. 재접속, 이전 세션/이전 run, timeout 이후 차량 소유권 해제 테스트를 추가했다.
5. 순수 C# TransportSafetyGate를 추가했다. Unity 시간/물리 객체에 의존하지 않고
   실제 경과 시간 입력으로 명령 중단을 검사한다. BrakeRequested 이벤트와
   BrakeRequired 속성을 제공한다. 중복/역순 명령은 수신 타이머를 갱신하지 않는다.
6. 단절 후에는 새 세션, 전체 snapshot 확인, 새 관측 확인, 유효한 새 명령이 순서대로
   있어야 통신 제동 잠금을 해제한다. 해제는 안전한 경로 또는 운행 허가를 보증하지 않는다.

## 직접 실행

프로젝트 폴더 CMD에서:

```bat
python -m campus_sim.transport.recovery_demo
python -m pytest backend/tests/test_runtime_transport.py backend/tests/test_runtime_recovery.py -q -p no:cacheprovider
powershell -NoProfile -ExecutionPolicy Bypass -File AgentScripts/TestTransportSafetyGate.ps1
```

첫 명령은 별도 서버 창 없이 시험용 peer로 무응답 종료, 재접속, 이전 세션 거부,
새 입력 수락 및 연결 자원 정리를 확인한다. status ok여도 실제 차량/전체 snapshot
복구는 미검증이다. PowerShell 검사는 프로젝트 C# 소스를 실제 컴파일하고 실행하며
Unity Editor/Physics 테스트를 대신하지 않는다.

## 실행 결과

- Python 통신/복구 전용: 41개 통과, 기존 Starlette/httpx 경고 1개.
- C# gate: 37개 assertion 통과. 물리 모델 없는 독립 테스트.
- 전체 Python/MapData 회귀: 508 passed, 기존 의존성 경고 1개, 30.55초.
- 변경 Python 코드 Ruff 및 git diff --check 통과.
- 정지 왕복 demo와 recovery_demo 모두 status ok (인프로세스 ASGI).
- Unity Editor/Player/Physics는 이번 작업에서 실행하지 않았다.

## 팀 차량 연결 시 주의

확인한 원격 main 커밋: 442593a1cd95f521bd99c1c017c88e627ae9ea41.
`Assets/CampusSim/Scripts/Simulation/VehicleController.cs`의 공개 함수는
`SetDriveCommand(float targetSpeedKmh, float steeringAngleRad, bool emergencyStop)`이다.
속도 단위는 km/h, 조향각은 rad이며 기존 서버 yaw rate와 그대로 대응하지 않는다.
정지 요청은 main thread에서 `SetDriveCommand(0f, 0f, true)`로 연결할 수 있다.
팀 파일은 이번 작업에서 가져오거나 수정하지 않았다.

### 제안 연결 순서

1. 인증된 새 접속 완료 시 BeginSession 호출. run/session/map은 수신 계약에서 검증한다.
2. 실제 최신 전체 상태가 도착한 뒤 ConfirmFullSnapshot 호출.
3. 새 ego와 필요한 센서 관측의 차량/pose/시각이 검증된 뒤 ConfirmFreshObservations 호출.
4. 메시지 파싱/권한/차량/command_id 검사 후 AcceptCommand 호출.
5. Update/FixedUpdate에서 Poll(simulationNow, Time.realtimeSinceStartupAsDouble) 호출.
6. BrakeRequired이면 main thread에서 실제 차량의 비상 제동 함수 호출.
7. 네트워크 종료/큐 overflow는 Disconnect 호출. 새 명령 확인 전 제동 유지.

이 gate는 network JSON 파서가 아니다. 임의 메시지를 받고 바로 snapshotReady나
observationsReady를 true로 만들면 안 된다. accepted command에는 실제 적용할 payload와
동일한 sequence/시각을 넘겨야 하며, 네트워크 콜백에서 Unity 객체를 직접 변경하지 않는다.
모든 주행 입력 경로가 gate를 통과해야 하고 키보드 등 우회 입력도 통합 시 통제해야 한다.

현재 WheelCollider 차량은 명령 timeout에 Time.time을 사용한다. 이를 실제 시간 기반 gate와
연결하고 단절 시 제동 이벤트를 적용하는 테스트가 남았다. Time.timeScale=0이면 물리 적분도
멈추므로 '제동 요청 발생'과 '차체가 감속해 정지함'을 구분해서 기록해야 한다.

## 아직 완료하지 않은 항목

- 실제 Unity transport/Pump와 gate의 wiring, WheelCollider 제동 영상/거리 측정
- 실제 서비스 전체 snapshot/지도 버전/임무 복원
- 모바일 사용자별 요청 projection 및 HTTP 멱등 요청 통합 시험
- 실제 센서 증가에 따른 큐 지연·누락·처리량 측정
- PC/모바일 통합, 서버 재시작 후 영속 임무 복원

팀의 통합 기준은 합의 전이다. 이번 session_id 확장은 로컬 v4 초안에만 적용하며
기존 v3 HTTP/WebSocket 계약은 변경하지 않았다. 기존 시험 서버를 실행 중이었다면
종료 후 재시작하고 동일 소스의 demo를 사용해야 한다.
