# 릴리스 기록: v0.5.3.1

아래 미커밋 표시는 개발 당시 기록이다. 이번 커밋에 통합본 재연결 변경을 포함한다.

# 통합본 재연결 위치 전달 — 2026-10-29

기준: origin/main 88550ec5 (v0.5.3.0), 별도 inha-reconnect-integration 작업 폴더의 미커밋 변경. 기존 algorithms 작업 폴더는 보존했다.

## 변경
- 실제 PC protocol 5 UnityAutonomousClient에 연결 세대별 관측 버퍼를 적용했다.
- 소켓 연결마다 기존 관측을 비운다. 새 연결 이후 Unity Update가 캡처한 위치만 전송한다.
- 캡처 도중 연결이 바뀌면 이전 세대 관측의 늦은 게시를 거부한다.
- 차량 Transform/Rigidbody 위치를 서버의 과거 값으로 덮어쓰지 않는다. 기존 제어 경로를 유지한다.

## 확인 결과
- ConnectionObservationBuffer 독립 C# 검사: 7개 통과.
- 관련 Unity 클라이언트 및 신규 PlayMode 시험 C# 소스 컴파일 통과. 전체 프로젝트 빌드 결과는 아니다.
- python -m pytest backend/tests/test_pc_lidar.py -q: 22 passed, 1 warning (2026-10-29 재실행).
- check_physical_reconnect_loopback.py: 실제 TCP WebSocket 2회 연결, 새 합성 위치의 서버 반영 및 정지 명령 확인. status=ok. Unity 물리 시험은 아니다.
- 신규 Python 시험 도구 Ruff 검사 통과.
- PhysicalPoseReconnectTests: 구현 및 컴파일 완료, 실행 결과 미확보. Unity Licensing Client 연결 실패 후 120초 시간 제한으로 소유한 시험 프로세스를 종료했다. 통과로 집계하지 않는다.

## Unity 시험 내용 및 재실행
격리된 최소 Unity 프로젝트에 실제 통합 클라이언트를 복사한다. 첫 응답 후 서버가 소켓을 끊고, 시험이 kinematic Rigidbody 위치를 바꾼 다음 두 번째 연결의 첫 수신 위치를 검사한다. 이는 통신·새 위치 샘플링 시험이며 실제 차량 제동 거리, WheelCollider 주행, 3대 운행 또는 운송 상태 복원 증거가 아니다.

프로젝트 폴더의 PowerShell에서:

```powershell
powershell -ExecutionPolicy Bypass -File .\AgentScripts\RunPhysicalPoseReconnectTests.ps1 -PythonExecutable C:\Users\leeta\anaconda3\python.exe
```

정상 결과는 Passed 1/1이다. tmp/pose-unity-latest.txt에 시험 폴더가 기록되고 results.xml이 통과 근거다. editor.log만으로 통과 판정하지 않는다. Unity Hub에서 해당 Editor의 라이선스가 정상 사용 가능한 상태여야 한다.

Python 검사는 다음 환경으로 실행했다 (pyproj는 작업 폴더에만 설치):

```powershell
$env:PYTHONPATH="$PWD/tmp/python-deps;$PWD/backend/src"
python -m pytest backend/tests/test_pc_lidar.py -q
python AgentScripts/check_physical_reconnect_loopback.py
```

## 남은 단계
1. Unity PlayMode 시험 1/1 실제 실행 결과 확보.
2. 실제 CampusTerrain 차량에서 연결 단절 → 현재 위치 유지 → 재연결 후 새 위치 전달을 확인.
3. 그 후 서버 재시작, 오래된 명령 거부, 다중 차량 복구 범위를 확장.

커밋/태그/푸시는 수행하지 않았다. 커밋 시 저장소의 버전 동기화 규칙을 적용해야 한다.

## 2026-10-30 후속 검증

- `check_physical_reconnect_loopback.py --forced-disconnect` 추가 및 실행 통과.
- 실제 Python 서버가 첫 제어 응답 직후 WebSocket을 코드 1012로 닫는다.
- 클라이언트가 종료 코드를 확인하고 재접속한다. 서버가 기록한 두 번째 연결의 첫 관측이 tick=2, x=70.57, z=-159.19인지 확인한다.
- 새 위치의 서버 상태 반영 및 정지 명령을 확인했다. status=ok, server_forced_disconnect=true, unity_physics_verified=false.
- 시험 프로세스 종료가 지연되면 해당 자식 프로세스만 종료하도록 정리 처리를 보완했다.
- Ruff 검사 통과. 합성 위치 기반 통신 검증이며 Unity 물리 주행 검증은 아니다.
- Unity 재실행은 Licensing Client 초기화 실패 후 크래시로 results.xml이 생성되지 않았다. 해당 실행 폴더: tmp/pose-unity-889a23a0fa394542a4da64c322463c3e.
- Python의 초기 로컬 소켓 생성 대기는 실행 환경의 네트워크 권한 부여 후 해소됐다. 제품 결함으로 집계하지 않는다.

재실행 (통합 폴더 PowerShell):

```powershell
$env:PYTHONPATH="$PWD/tmp/python-deps;$PWD/backend/src"
python AgentScripts/check_physical_reconnect_loopback.py --forced-disconnect
```

다음 필수 단계는 Unity Hub에서 Editor가 정상 실행되는 환경에서 위 PlayMode 시험의 Passed 1/1 결과를 확보하는 것이다. 아직 커밋/푸시하지 않았다.
