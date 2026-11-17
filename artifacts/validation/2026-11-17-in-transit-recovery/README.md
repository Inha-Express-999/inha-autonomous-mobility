# 승객 운송 중 서버 재시작·차고지 복귀 시험 — 2026-11-17

결과: PASS. 실제 Unity V03 승객 운송 중 사용자 CMD에서 서버를 종료하고 같은 DB로 재실행했다. 같은 요청 복원, 새 mission 주행 재개, 운송 완료 1건, 차고지 복귀·정지·충전을 확인했다.

- 기준 소스: v0.5.13.0 / 875ddda8, 로컬 manifest 버전 해시 수정 적용. 기존 별도 씬/메시 변경이 있는 로컬 Unity 환경이므로 깨끗한 체크아웃 재현 결과는 아니다.
- 요청: req-00000001-4bfb21e8eb11, 60주년 → 비룡플라자, V03, 승객 1명.
- 종료 시 SQLite: IN_TRANSIT / TO_DROPOFF. 이전 mission edd6129fbbf840a4b3b86fd7265bf131.
- run: 11d384f0157c41ad877118b101220758 → afedd466f7d141cfa9c9dbaff1ceb5dd.
- 복원: 같은 요청/V03, 새 mission 6a663dde04ee402f94b4782c388a2e86, 신선한 차량 관측, 약 17.86 km/h 주행.
- 최종: COMPLETED, 완료 집계 1, V03 CHARGING, transport_phase/request_id 해제, 속도 약 0, Reached Stop candidate. DB job.returned=true 추가 확인.
- 별도 DB: tmp/in-transit-recovery-20261117.db. 이전 시험 DB는 유지했다.
- 원자료: stopped-checkpoint.json, restarted.json, home-return.json, final-job.json, 전후 timeline JSONL.
- verify_evidence.py: 저장된 원자료 9개 조건 PASS. Unity 재실행 시험이 아니라 기록 검증이다.

한계: 단일 요청/차량/경로, 사용자 Ctrl+C 종료·수동 재시작이다. 자동 서버 재시작·전원 차단·다중 차량 동시 복구·정밀 정지 시간/거리 검증이 아니다. 5초 폴링으로 기록한 시각은 실제 전이 시각과 다를 수 있다. 서버와 Unity는 실행 상태로 유지했다.

시험 준비 중 VERSION 변경 후 지도 manifest 미갱신 때문에 서버 시작이 실패했다. 임시 디렉터리에서 지도 재생성/검증 후 모든 payload 해시가 동일함을 확인하고 manifest만 갱신했다. 제 환경의 시험 서버는 loopback timeout으로 종료했고, 실제 시험 서버는 사용자 CMD에서 실행했다.
