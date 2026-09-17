# 구현 현황

기준 버전 0.1.0.0 · 2026-09-17

- M0 준비 일부: VERSION/CHANGELOG 추가, 지도 개선 계획과 오픈소스 가공 도구/고정 의존성 기록.
- M2 사전 조사: 확장 OSM, Copernicus DSM 타일/메타데이터/라이선스 확보. 필수 랜드마크 11개로 사용자 정정 반영.
- 실행: `Temp/map-research-venv/Scripts/python.exe AgentScripts/prepare_elevation_research.py` 성공. 983개 OSM node/way feature, 19개 랜드마크 관련 후보. 후보 개수는 필수 랜드마크 완료 개수가 아님.
- 검증: 고도 결측 없음, 좌표 기준점 3개 왕복, 16비트 RAW 왕복 오차 약 0.00077m(데이터의 지형 정확도 아님), 비교 PNG 육안 확인.
- 미완료: Unity Terrain 씬 적용/재베이크·에셋 렌더 검증·성능 측정. 이후 설치된 Unity MCP relay에 직접 연결해 프로젝트/Editor/씬/콘솔 읽기 성공. `MapResearch/MCP_READY.md` 참조.
- 사용자 요청으로 VERSION·문서·Unity bundleVersion을 0.1.0.0으로 재설정. Unity Editor는 6000.3.21f1 유지. 서버 버전 연동 및 M0 엔진/계약/CLI는 미구현.
- 사용자 변경 보존: Packages/manifest.json, Packages/packages-lock.json, ProjectSettings/Packages 등 작업 전 변경을 수정하지 않음.
- 다음 작업: `MapResearch/PLAN.md`의 좌표 통합과 Terrain 대표 구역 시범 적용.
