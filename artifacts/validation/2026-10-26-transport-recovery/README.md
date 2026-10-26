# 재접속 후 제동 유지 검증 — 2026-10-26

Unity 6000.3.21f1, 실제 Editor PlayMode, 8 passed / 0 failed.
`editor-source-manifest.json`의 122개 소스 SHA-256과 실행 후 작업 파일이 일치했다.
`editor-results.xml`에 각 시험 결과가 있다. 문서 변경은 이후 기록 작업이다.

새 시험: ServerRestartRestoresNewSnapshotButKeepsVehicleBraked.
실제 v3 Python 서버를 종료 후 같은 loopback 포트로 재시작하고 새 run/snapshot 수신을 확인한다.
옛 snapshot은 새 연결의 복구 완료를 만족하지 못한다. 복구 후에도 WheelCollider 제동을 유지하며
옛 명령과 retired run snapshot을 거부한다.

시험용 Physics 차량과 합성 준비 상태/명령을 사용한다. 실제 캠퍼스 씬 운행, v4 정상 제어,
서비스 상태 영구 복구 또는 Player/모바일 실기기 검증을 의미하지 않는다.
