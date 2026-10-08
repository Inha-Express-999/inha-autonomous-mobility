# 출발지·목적지 최소 시연

2026-09-28 현재 작업본. **합성 6거점 Python 시연**이며 실제 캠퍼스 지도와 Unity Physics 검증 결과가 아니다. 비룡플라자·혼잡구역 정책은 기본 비활성이다. 동일한 `synthetic-campus-6stop-v1`을 쓰는 Unity PC 미리보기 씬과 연결할 수 있다.

## 바로 실행

저장소 루트의 PowerShell에서:

```powershell
./AgentScripts/RunMinimalDemo.ps1
```

브라우저가 열리지 않으면 `http://127.0.0.1:8765/demo`으로 접속한다. 출발지와 목적지를 고르고 **시연 시작**을 누르면 서버가 승객 요청을 접수해 차량을 배정하고, 지도 위 차량 위치·경로·상태를 갱신한다. 같은 터미널에서 `Ctrl+C`로 종료한다. 스크립트는 현재 작업용 `tmp/mvp-venv`가 있으면 사용하고, 없으면 `python`을 사용한다. 필요한 패키지가 없다면 `python -m pip install -e ".\backend[dev]"`를 먼저 실행한다.

2026-10-08 사용자 지시에 따라 합성 Unity preview 씬과 전용 빌드 프로필을 삭제했다. 이 안내의 실행 경로는 브라우저 시연이다. Unity 클라이언트는 PC_Bootstrap과 Mobile_Bootstrap을 사용하며, 실제 CampusTerrain에 합성 차량 경로를 배치하지 않는다.


## 확인한 범위

브라우저 전용 3거점 fixture로 실제 localhost 서버에서 `/demo` HTTP 200, 시작점 `fixture_landmark_2`→목적지 `fixture_landmark_3` 요청 201/`ASSIGNED`, 이후 `COMPLETED`를 확인했다. 현재 기본 실행은 합성 6거점 fixture를 사용한다. C# 소스 WebSocket smoke는 PC·모바일 동시 접속, 요청·snapshot, 위치/센서 ACK, 재접속·중복 명령 방지를 통과했다. Unity 씬/Player와 모바일 실기기에서의 UI 흐름은 미검증이다.

## 이 시연 이후 남는 작업

1. 실제 Bootstrap의 서버 snapshot 수신과 PC·모바일 UI 흐름을 Editor/Player에서 확인한다.
2. 실제 캠퍼스 도로·Stop·출입구의 현장 검토 자료를 승인해 RoadGraph로 연결한다. 현재 OSM 후보는 모두 미승인이다.
3. 실제 지도에서 Unity Physics·센서 안전·3대 경합 및 PC·모바일 통합을 검증한다.
4. 300명 보행자·50개 연결, 재생·비교 실험과 단말 성능을 측정한다.

비룡플라자 우회와 혼잡구역 정책은 이 시연 및 MVP 이후 작업이다.
