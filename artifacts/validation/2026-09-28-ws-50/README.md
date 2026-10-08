# 50개 WebSocket 구독 로컬 부하 실행

2026-09-28 현재 작업 트리의 합성 서비스와 Python ASGI 서버를 Windows localhost에서 단일 프로세스로 실행했다. 재현 명령:

```powershell
./tmp/mvp-venv/Scripts/python.exe AgentScripts/LoadTest/ws_snapshot_load.py --clients 50 --snapshots 10 --output artifacts/validation/2026-09-28-ws-50/report.json
```

빈 구독 실행 결과는 [report.json](report.json)에 있다. 모바일 역할로 서로 다른 50개 `subscriberId`를 구독하고 각 10개씩 총 500개 snapshot을 받았다. 450개 연속 수신 간격 표본의 p50은 95.44ms, p95는 111.06ms였다. Python 20Hz clock의 예약 시각 대비 실행 지연 p95는 12.586ms, `advance` 작업 시간 p95는 0.063ms였다(28 tick 표본).

승객 요청을 연결마다 1건씩 동시에 보낸 실행은 다음 명령과 [requests-report.json](requests-report.json)에 기록했다.

```powershell
./tmp/mvp-venv/Scripts/python.exe AgentScripts/LoadTest/ws_snapshot_load.py --clients 50 --snapshots 10 --requests-per-client 1 --output artifacts/validation/2026-09-28-ws-50/requests-report.json
```

50건의 요청 ACK가 모두 수락됐고 ACK 지연 p95는 19.79ms였다. 측정 종료 시 요청은 `ASSIGNED` 1건, `PICKUP_SERVICE` 1건, `QUEUED` 48건이었다. 각 모바일 snapshot의 요청 ID 집합이 그 연결에서 생성한 요청 ID 하나와 일치했고, 총 500개 snapshot의 수신 간격 p95는 110.57ms였다. Python clock 실행 지연 p95는 13.034ms, `advance` 작업 시간 p95는 0.154ms였다(29 tick 표본). 두 실행 모두 51번째 연결은 오류 `connection_limit_reached`와 close code 1013으로 거부됐고 연결 종료 후 활성 슬롯은 0개였다. 보고서에는 Python·플랫폼·라이브러리 버전과 실행 소스 SHA-256을 기록했다.

각 실행은 약 1.5초 localhost 확인이다. 50개 요청 실행도 같은 출발·목적지의 짧은 합성 입력으로, 완료까지 운송하지 않았다. PC 관제·Unity 차량/보행자·센서 입력·LAN/실기기·재접속 폭주를 함께 가하지 않았다. clock 표본은 Python `advance`만 포함하며 센서·WebSocket 처리·Unity Physics를 합친 loop p95가 아니다. 네트워크 왕복 지연, CPU·메모리, PC/모바일 FPS를 측정하지 않았으므로 M5a/M6 성능 목표 달성으로 판정하지 않는다. `subscriberId`는 아직 인증된 세션이 아니므로 snapshot 분리 관측은 보안 격리의 증거가 아니다.
