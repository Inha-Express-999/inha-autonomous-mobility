# 통신 중단과 WheelCollider 차량 제동 연결

2026-10-25 로컬 작업. 기존 씬/프리팹/사용자 설정을 덮어쓰지 않고 추가했다.

## 추가한 코드

- 팀원의 `VehicleController.cs`와 meta를 main의 고정 커밋
  `442593a1cd95f521bd99c1c017c88e627ae9ea41`에서 가져왔다. 원본 코드는 수정하지 않았다.
- `TransportVehicleBrakeBridge`: TransportSafetyGate의 제동 요구를 실제
  `VehicleController.SetDriveCommand(0f, 0f, true)`에 연결한다.
- `ClientConnectionBrakeRelay`: 기존 v3 PC ClientRuntimeHost 연결 종료 및 snapshot
  정체를 감지해 bridge에 전달한다. 최신 snapshot 여부는 WorldStateStore가 판단하므로
  중복 snapshot은 freshness를 연장하지 않는다.
- Simulation/Presentation assembly 참조를 보완했다. 기존 격리 client 테스트 복사 대상에도
  Simulation 소스를 포함했다.

## 동작과 경계

bridge는 Awake부터 제동 상태다. 인증된 세션 연결, 전체 상태, 새 관측 확인을 거친 뒤
ApplyValidatedCommand가 성공해야 전달된 명령을 차량에 적용한다. 수신 JSON, 역할/차량,
pose/센서 참조, command_id 중복 검증은 호출하는 통신 어댑터 책임이다.

네트워크 스레드에서 단절이 발생하면 QueueDisconnected만 호출한다. 이것은 원자적 플래그만
설정하며 Unity 객체를 조작하지 않는다. Unity의 다음 Update/FixedUpdate에서 제동한다.
같은 프레임 전에 새 입력이 들어와도 pending 단절을 먼저 처리한다. 브리지 비활성화도
제동을 요청한다. 중복/역순 명령은 수신 타이머를 연장하지 않는다.

명령 속도 단위는 km/h, 조향각은 rad다. Python 기존 yaw rate를 조향각으로 단순 대입하지 않는다.
연결 복구를 이유로 자동 출발시키지 않는다. 기존 v3 relay는 정지 전용이며 주행 명령을
변환하거나 세션/전체 상태/새 관측 확인을 대신하지 않는다.

## Unity에서 연결할 위치

원본 씬 대신 별도 시험 씬/복사한 차량에서 먼저 확인한다.

1. 네 WheelCollider와 Rigidbody가 있는 팀 차량 오브젝트를 사용한다.
2. VehicleController와 TransportVehicleBrakeBridge를 추가한다.
3. 기존 v3 PC 연결을 사용하는 경우 ClientConnectionBrakeRelay를 추가하고 Runtime 필드에
   실제 PC ClientRuntimeHost 오브젝트를 지정한다. fixture는 정상 연결로 인정하지 않는다.
4. 서버를 종료하거나 snapshot 전송을 멈추면 bridge가 비상 제동을 요청하는지 확인한다.
5. Inspector의 키보드 입력이나 다른 차량 스크립트가 별도로 명령을 주지 않도록 한다.
   모든 운행 명령은 bridge를 거치도록 통합해야 한다.

**이 컴포넌트를 기존 원본 씬에 자동 부착하지 않았다.** 정지 전용 relay만 붙이면 정상
주행을 시작할 수 없다. 실제 v4 Unity socket/decoder와 서비스 상태 복원은 아직 미연결이다.

## 독립 물리 시험

프로젝트 폴더 CMD에서:

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File AgentScripts/RunTransportBrakeTests.ps1 -TimeoutSeconds 180
```

이 명령은 tmp 아래 새 Unity 시험 프로젝트를 만들어 실제 VehicleController, bridge, gate
소스를 복사한다. 원본 Unity 씬을 열거나 수정하지 않으며, 자신이 생성한 Editor 프로세스만
종료한다. 실험용 평면과 차량/바퀴를 코드로 생성하는 테스트이며 캠퍼스 씬 검증은 아니다.

시험 1: 움직이는 Rigidbody에 단절 신호 전달, motorTorque=0/brakeTorque>0 확인 후 감속 확인.
시험 2: timeScale=0에서도 중복 명령으로 타이머가 연장되지 않는지, 재접속 뒤 오래된 명령과
새 관측 없는 명령을 거부하는지 확인.

## 현재 검증 결과

- 순수 C# gate: 37개 assertion 재통과.
- 실제 Unity 라이브러리를 참조한 bridge/차량 소스 컴파일 통과.
- Unity Roslyn C# 9로 Domain/Networking/Simulation/Presentation/PC/Mobile 전체 소스와
  새 PlayMode 테스트 소스를 컴파일 확인했다. Unity assembly별 import/플랫폼 빌드를 대신하지 않는다.
- 최초 Mono compiler의 전체 소스 검사는 기존 코드의 C# 9 표현식을 지원하지 못했다.
  소스는 바꾸지 않고 Unity Roslyn compiler로 재확인했다.
- 최초 실행은 라이선스 초기화에서 막혔으나, 사용자 재실행 결과 **PlayMode 2/2 통과**를
  `tmp/transport-brake-9850bcbae4464d68b1314643ed22af13/results.xml`에서 확인했다.
  source-manifest의 7개 파일 SHA256도 변경 전 작업본과 모두 일치했다.
- 실제 WebSocket 강제 종료부터 차량 정지까지의 전체 통합, 캠퍼스 씬, Player/Android는 미검증.

서버의 APPLIED 응답은 이번 컴포넌트에서 자동 생성하지 않는다. 실제 물리 적용 tick을
확인할 때만 통신 모듈에서 보고해야 한다.

## 실제 소켓 단절 시험 추가 (2026-10-25)

동일 실행 명령에 세 번째 시험을 추가했다. 성공 기준은 이제 **Passed 3/3**이다.
Python 경로가 자동으로 잡히지 않으면 `-PythonExecutable C:/Users/leeta/anaconda3/python.exe`를
명령 끝에 붙인다. Python 환경에는 프로젝트 의존성인 websockets가 필요하다.

시험은 127.0.0.1의 OS 할당 포트에 자체 Python WebSocket 서버를 실행하고 Unity
ClientWebSocket으로 연결한다. 연결 확인 후 시험 차량에 합성 준비 상태와 1m/s 초기 속도를
주고 **이 시험이 생성한 서버 프로세스만 강제 종료**한다. receive 종료 → QueueDisconnected
→ bridge → VehicleController → WheelCollider 경로에서 SOCKET_DISCONNECTED 사유,
motorTorque=0, brakeTorque>0, 속도 0.15m/s 이하 및 옛 세션 명령 거부를 검사한다.

이것은 **시험용 소켓 어댑터와 합성 준비 상태**를 사용하는 장애 주입 시험이다.
실제 runtime-v4 hello/센서/제어 decoder, 서비스 snapshot 복구, 원본 씬 연결을 검증하지 않는다.
기존의 2/2 통과 기록은 이 새 시험의 성공 증거가 아니다. 최신 실행 XML을 별도로 확인해야 한다.

추가 검증: 새 C# 테스트는 Unity Roslyn 컴파일 통과, 순수 gate 37개 검사 통과(PowerShell 7),
Python fixture는 실제 loopback 접속/서버 강제 종료 감지 통과, Ruff 통과.
최초 자동 Unity 실행은 LicenseClient IPC 연결 거부/초기화 시간 초과로 막혔다.
로그: `tmp/transport-brake-6ca5fa2d69f34fc8951d946fb82a3c79/editor.log`.
기존 2/2 XML과 소스 manifest는 `artifacts/validation/2026-10-25-transport-brake/`에도 보존했다.

사용자 재실행 `transport-brake-b4b48b7680e546a3b1d3f5b263c91cdd` 결과 XML에서
**3/3 Passed, failed=0**을 확인했다. 실제 소켓 접속 후 시험 Python 서버 강제 종료,
WheelCollider 제동/감속 및 옛 세션 명령 거부 시험을 포함한다.
증거는 같은 artifacts 폴더의 `socket-results.xml`, `socket-source-manifest.json`이다.
이는 시험용 어댑터를 사용하는 격리 통합 검증이며 운영 v4 및 실제 캠퍼스 씬 완료를 의미하지 않는다.

## PC Bootstrap과 기존 v3 수신 경로 연결

`ClientBootstrap`은 live PC host를 초기화한 뒤 world/role 씬 로드 시
`ClientVehicleSafetyBinding.BindScene`을 호출한다. 해당 씬의 `VehicleController`마다
bridge와 relay를 연결하고 초기 비상 제동을 요청한다. fixture/mobile은 자동 연결하지 않는다.
차량은 dynamic Rigidbody와 WheelCollider 4개가 필요하다. 기존 `VehicleRouteFollower` 또는
`VehicleCommandActuator`와 함께 있으면 명시적으로 거부한다. 다른 host 소유 차량도 거부한다.
비활성 차량은 활성화될 때 bridge가 초기 제동을 유지한다. host 비활성/단절/stale은 제동 사유다.

실행 중 새로 Instantiate한 차량은 생성 코드에서
`ClientVehicleSafetyBinding.BindVehicle(host, vehicleController)`를 명시적으로 호출해야 한다.
자동 주기 검색이나 모든 차량에 대한 주행 명령 생성은 하지 않는다.
현재 로컬 씬/prefab에는 팀 `VehicleController`가 배치되어 있지 않아, 이 변경만으로 기존
kinematic 합성 actor를 WheelCollider 차량으로 바꾸지는 않는다.

이 경로는 기존 `/v1/client/ws`의 **정지 전용 연결**이다. snapshot의 targetSpeed/yawRate를
임의로 steering으로 변환하거나 v4 full snapshot/관측 확인으로 간주하지 않는다.
주행 허가는 별도 검증된 제어 어댑터가 필요하다.

확장 검증 명령 (프로젝트 폴더 CMD):

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File AgentScripts/RunTransportBrakeTests.ps1 -IncludeClientIntegration -TimeoutSeconds 180
```

성공 기준은 **Passed 7/7**이다. 기존 3개에 실제 `campus_sim.api` 서버/실제
`WebSocketClientDataSource`/host/relay의 프로세스 종료 제동, 비활성 차량·host 처리,
fixture 연결 거부, 합성 driver 중복 거부 4개를 추가한다. 움직임/준비 상태는 합성으로 주며
서버는 연결과 snapshot을 제공한다. 명령 watchdog이 대신 멈추는 오탐을 줄이기 위해 시험 중
명령을 갱신하고 relay 정지 사유를 확인한다. 복사한 Python backend·설정·합성 지도와 C#
소스 hash를 manifest로 기록한다. 원본 서버/씬을 종료하거나 수정하는 시험은 아니다.

검증: 변경된 전체 관련 C# 및 7개 테스트 Roslyn 컴파일, Python 실제 v3
connected/snapshot/프로세스 종료 감지, 복사 backend 초기화, Ruff 통과.
최초 자동 실행은 LicenseClient 초기화에서 막혔으나, 이후 사용자 실행
`transport-brake-03eb4c0d566f484a9e2c6b2c242ca2fc` XML에서 **7/7 Passed**를 확인했다.
manifest와 현재 소스 불일치는 0이다. artifacts 폴더의 `client-v3-results.xml`과
`client-v3-source-manifest.json`에 보존했다. 별도로 열린 Editor의 Play 진입/종료도
직접 확인했다. 당시 합성 preview 화면은 서버 연결 실패 상태였으므로 주행 검증은 아니다.
