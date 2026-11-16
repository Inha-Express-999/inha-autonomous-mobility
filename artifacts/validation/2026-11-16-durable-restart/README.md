# SQLite 복원 적용 후 실제 Unity 서버 재시작 시험

2026-11-16, v0.5.11.1 기준 소스 69ef457c에 미커밋 복원 변경 적용. Unity 6000.3.21f1, Python 3.12.4, protocol 6. V01, 승객 1명, 60주년 → 비룡플라자, 실제 WheelCollider 주행 1회.

**결과: 같은 요청 복원 → 새 명령으로 운송 재개 → 완료 1건 → 정문 차고지 복귀/충전.** 저장 기능 없이 요청이 사라졌던 [이전 시험](../2026-11-16-server-restart/README.md)과 구분한다.

| 관측 시점 | 내용 |
|---|---|
| 149.4초 | IN_TRANSIT, 4.983m/s에서 시험 소유 Python 프로세스 트리 강제 종료 |
| 150.7초 | 0.0256m/s로 정지 확인 |
| 155.7초 | 같은 SQLite DB를 사용하는 서버를 시험 도구가 재실행 |
| 160.1초 | 새 run ID와 연결 3/3, 같은 요청 IN_TRANSIT 확인 |
| 175.1초 | 새 mission으로 4.9995m/s 주행 기록 |
| 349.1초 | 같은 요청 COMPLETED, 정문 CHARGING, 예약 해제 |
| 351.1초 | 최종 PASS 기록 후 Play 종료 |

요청 ID: `req-00000001-9b27622582fa`. run ID는 `dec2935448684db3b40b8d22010ee016`에서 `856eca1e96084a61a2af5c5665b6bff8`로 바뀌었다. 최종 목표점 오차 약 0.571m, 완료 집계 1건. `verify_evidence.py`의 원자료 조건 11개 모두 통과.

시간은 Editor wall time 및 폴링/프레임 지연을 포함한 관측값이다. 서버 종료→정지 확인 1.3초, 재실행→연결 확인 4.4초를 정확한 물리 제동 시간/네트워크 지연으로 해석하지 않는다. 연결 직후 관측기는 15초 대기했으므로 175.1초는 최초 기록된 재주행 표본이며 실제 재출발 시각을 의미하지 않는다. 연결 순간의 state snapshot은 명령 설치보다 늦게 갱신될 수 있어 새 mission은 후속 timeline으로 확인했다.

## 코드와 시험 근거

- `driver.cs.txt`: 임시 Editor 관측기. 이 세션이 시작한 서버만 종료하고 같은 실행 설정으로 재시작했다. 자동 서버 재기동 제품 기능은 아니다. 환경변수는 시험용 DB 경로로 설정했다가 종료 시 원래 값으로 복원했다. 실행 소스는 Assets에서 제거했다.
- 단계별 PNG/state JSON, events.txt, timeline.txt, restart-timeline.txt, latest-state.json, latest-observation.json, result.txt를 보존했다.
- `summary.json`, `verify_evidence.py`: 저장 근거 재검사. Unity를 다시 실행하지 않는다.
- `recovery-regression.xml`: 신규 복원 12개와 기존 물리 배차/웹 회귀 포함 46개 통과.
- `recovery-control.xml`: Unity 제어/Python 데스크톱 서버 관련 12개 통과. 합계 58개. 기존 Starlette httpx deprecation 경고는 남아 있다.
- 초기 HTTP 시험은 제한 환경의 asyncio socketpair에서 멈춰 중단했으며, 로컬 통신 권한 부여 후 위 시험을 완료했다.
- `source-sha256.json`: 검증 변경 파일의 해시. 기존 커밋만으로 재현할 수 없는 미커밋 결과다.

## 범위

SQLite 저장 옵션을 켠 단일 서버·한 경로·한 차량의 결과다. 전체 서비스/모바일/다중 서버·디스크 장애 중 실제 Unity 제동·장시간 장애를 검증한 것은 아니다. 복구 모드는 기본 비활성이며 [설정과 한계](../../../Docs/SERVER_RECOVERY.md)는 저장소의 Docs/SERVER_RECOVERY.md를 참고한다. 같은 DB를 여러 서버가 동시에 쓰면 안 된다. 관측기 실행 시 사용한 DB는 tmp/durable-restart-20261116.db이며 이 시험의 완료 상태가 남아 있으므로 새 독립 시험에는 새 DB를 사용한다.
