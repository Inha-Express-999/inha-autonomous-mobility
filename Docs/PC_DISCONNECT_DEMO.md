# PC 연결 단절 시연 준비

## 최초 1회 준비
Python 3.11 이상을 설치한 Windows PC에서 저장소 폴더의 PowerShell을 열고 실행한다.

    powershell -ExecutionPolicy Bypass -File scripts/setup_pc_server.ps1

Python 실행 파일을 지정할 때는 다음 형식을 사용한다.

    powershell -ExecutionPolicy Bypass -File scripts/setup_pc_server.ps1 -PythonExecutable "C:/경로/python.exe"

저장소 루트 .venv에 backend[dev]를 설치하고 의존성 충돌과 서버 import를 검사한다. 패키지 버전 범위는 backend/pyproject.toml을 따른다. 인터넷 연결이 필요하다. 실패 후 원인을 해결하고 같은 명령을 다시 실행할 수 있다.

이전 실행에서 CAMPUS_SIM_PYTHON을 별도로 지정했다면 그 환경변수를 해제한다. Unity Editor와 Hub를 완전히 종료한 후 다시 열어 이전 프로세스의 임시 환경변수를 지운다. 기존 런처는 루트 .venv/Scripts/python.exe를 자동으로 선택한다.

## 시연
1. Unity Hub에서 통합 프로젝트를 연다.
2. Assets/CampusSim/Scenes/PC_Bootstrap.unity를 열고 Play를 누른다.
3. 서버 연결됨과 제어 연결 차량 3/3을 확인한다.
4. 승객 1명, 출발 60주년, 도착 비룡플라자를 선택하고 요청을 접수한다.
5. 배정된 차량 카드를 선택하고 주행 중 ‘연결 끊김 시연’을 누른다.
6. 차량 제동, 2/3 표시, 정지 유지 및 복구 가능 표시를 확인한다.
7. ‘차량 연결 복구’를 누르고 3/3 복구와 요청·배정 차량 유지를 확인한다.
8. 복구 후 대기와 실제 재출발은 따로 관찰한다. 최종 운송 완료와 차고 복귀까지 확인하면 요청 ID와 화면을 함께 기록한다.

시연을 종료하려면 Unity의 Play를 끈다. Unity가 시작한 서버는 함께 종료된다. 이미 켜져 있던 서버를 재사용했다면 그 서버는 계속 실행된다.

## 검증 범위
기존 연결 단절·정지·복구 시연은 artifacts/validation 폴더에 기록돼 있다. 단계별 운송 완료 회귀 시험은 합성 관측을 사용하는 Python 시험이며, 이번 환경 준비 작업은 Unity 전체 운송 완료 검증과 별개다.
