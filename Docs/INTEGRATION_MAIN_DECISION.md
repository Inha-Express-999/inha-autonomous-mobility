# main / decision 통합 검증

프로젝트 0.4.4.0 · 2026-10-23 · 통합 브랜치 `codex/integrate-decision-main`

## 범위와 결과

원격 fetch 후 로컬 main/decision이 origin과 각각 일치함을 확인했다. main `442593a`에서 통합 브랜치를 만들고 decision `6e980df`를 `--no-ff --no-commit`으로 병합한 뒤 수정·검증했다. 공통 조상은 `ea99f17`이며 양쪽의 고유 커밋은 각각 4개다. 파일 충돌은 없었다. 원래 main/decision 브랜치는 변경하지 않으며 원격 push는 수행하지 않는다.

main의 차량 제어/메타/프리팹/씬 5개 파일을 그대로 보존했다. WheelCollider 구동·조향·제동·Anti-Roll·명령 timeout 기반과 decision의 OSM 지도·Dijkstra/A*·FIFO/Greedy/Hungarian·배터리/충전·웹 시연을 함께 유지한다. 통합은 Python–Unity 주행 연결 구현을 의미하지 않는다.

## 수정과 호환성

- 지도 해시의 CRLF/LF 의존성을 `map_text_digest`로 수정했다. UTF-8 개행만 LF로 정규화하며 공백/BOM/실제 내용 변경은 계속 검출한다. 기존 원시 `digest`와 과거 artifacts는 유지한다.
- 지도 manifest schema_version=2, hash_format=sha256-utf8-lf-v1로 재생성했다. 구형/미지원 해시 형식은 명시적 재생성을 요구한다. [ADR 0006](ADR/0006-portable-map-hashes.md)에 결정 근거를 남겼다.
- JSON 및 지도 출처 텍스트 산출물은 LF로 기록한다. `.gitattributes`에서 해당 지도 txt도 LF로 고정해 새 checkout의 산출물 비교를 보장한다. 원본 지도·CSV·씬은 변경하지 않았다.
- 현재 원본으로 재생성한 graph/stops/landmarks/policy/build_report의 JSON 내용은 decision과 동일하다. 지도 해시 23개를 모두 확인했다. 스테이징 후 Git index의 실제 저장 내용으로도 23개 해시와 main의 5개 파일 바이트 일치를 재검증했다. 과거 CSV 해시 불일치의 당시 원인은 확정하지 않으며 현재 원본에서 재생성한 해시를 사용한다.
- 프로젝트 버전은 기존 최고 태그 v0.4.3.0 이후의 미사용 마이너 번호 0.4.4.0으로 동기화했다. VERSION·Unity bundleVersion·Python package/API·현재 문서·CHANGELOG·지도 생성 버전에 적용한다. 다른 브랜치의 0.4.3.0 기능을 포함했다는 의미는 아니다.
- Editor 6000.3.21f1, map_version=inha-osm-unity-v2, HTTP/Unity 및 운행 설정 schema_version=3은 유지한다. 지도 manifest의 독립 스키마만 변경된다. 이전 지도 묶음은 아래 builder 명령으로 재생성해야 한다.

## 실제 검증

Windows / Python 3.14.4 / pytest 8.4.2 / FastAPI 0.143.1 / pyproj 3.8.0에서 기존 `.utmp/fifo-dijkstra-deps` 의존성을 사용했다. 전역 Python 패키지는 변경하지 않았다.

```powershell
$env:PYTHONPATH = "$PWD/backend/src;$PWD/.utmp/fifo-dijkstra-deps"
python -m campus_sim.map_builder
python -m pytest backend/tests -vv -p no:cacheprovider --basetemp=.utmp/integration-pytest-full-20261023 --junitxml=.utmp/integration-python.xml
python -m ruff check backend/src backend/tests
python -m ruff format --check backend/src backend/tests
node --check backend/src/campus_sim/web/app.js
node --check backend/src/campus_sim/web/avoidance.js
node --check backend/src/campus_sim/web/comparison.js
```

| 검사 | 결과 | 실제 범위 |
|---|---|---|
| Python 전체 | 205 통과, 경고 1 | 기존 196개 + 신규 9개. 지도·경로·배차·배터리·API·웹·센서/회피 회귀 |
| 신규 지도 회귀 | 8 통과 | LF/CRLF 원본/산출물 로딩과 동일 재생성, 실제 내용 변조, 구형 해시 거부, BOM/공백/UTF-8 |
| 버전 회귀 | 1 통과 | VERSION·package/API·Unity·현재 문서·지도 생성 버전 및 독립 계약/Editor 버전 |
| Ruff | 통과 | src/tests 전체 42파일의 lint/format |
| JavaScript 구문 | 3파일 통과 | app.js, avoidance.js, comparison.js |
| Unity EditMode | 54 통과, 실패 0 | 기존 DTO·좌표·상태·fixture 클라이언트 검사 및 Editor 컴파일 |
| Unity PlayMode | 1 통과, 실패 0 | 기존 fixture presenter lifecycle 회귀 |
| 지도 보존 | 통과 | 23개 해시 유효, 주요 지도 JSON 내용 5개 동일 |
| main 보존 | 통과 | 고유 변경 5개 파일이 main과 동일 |

Unity는 설치된 `6000.3.21f1/Editor/Unity.exe`를 다음 인자로 각각 실행했다. EditMode 로그의 `Test run completed. Exiting with code 0` 및 두 NUnit 결과의 Passed를 확인했다. EditMode 실행 후 남은 PowerShell 자식 프로세스 대기는 테스트 결과 기록 완료 뒤 중단했다.

```text
-batchmode -nographics -projectPath <repository> -runTests -testPlatform EditMode -testResults <repository>/.utmp/integration-editmode.xml -logFile <repository>/.utmp/integration-editmode.log
-batchmode -nographics -projectPath <repository> -runTests -testPlatform PlayMode -testResults <repository>/.utmp/integration-playmode.xml -logFile <repository>/.utmp/integration-playmode.log
```

Unity 실행 중 패키지가 자동 변경한 Standalone analytics define은 기존 설정으로 복원했다. 일반 권한 Python 실행은 API 테스트 중 진행이 멈춰 중단하고, 소켓/임시 파일 접근이 가능한 권한으로 전체를 다시 실행했다. 위 결과는 재실행 결과다. 캐시 디렉터리 접근 오류를 피하기 위해 Ruff는 backend/src 및 backend/tests로 범위를 명시했다.

구조화된 결과와 원본 테스트 XML은 [검증 요약](../artifacts/integration_0.4.4.0/summary.json), [Python](../artifacts/integration_0.4.4.0/python.xml), [Unity EditMode](../artifacts/integration_0.4.4.0/unity_editmode.xml), [Unity PlayMode](../artifacts/integration_0.4.4.0/unity_playmode.xml)에 보관한다. 이전 artifacts를 이번 실행 결과로 바꾸지 않았다.

## 경고와 미검증

- 기존 Starlette TestClient/httpx deprecation 경고 1개가 있다. 수신 브랜치 폰트 라이선스 OFL.txt 21행의 기존 trailing whitespace는 원문 보존을 위해 유지했다.
- PC/Mobile Player 빌드·실단말·렌더링·FPS·카메라/AudioListener 중복·실제 차량 Physics 성능은 미검증이다. 설치된 플랫폼 모듈은 Windows Standalone뿐이며 Android/iOS 모듈은 없다. 위 Unity 테스트는 전체 캠퍼스 주행 인수 검사가 아니다.
- Python 3.11 실행은 검증하지 않았다. mypy는 이 브랜치의 개발 의존성/설정으로 제공되지 않아 실행하지 않았다.
- Python 운송 완료는 여전히 가상 시간 기반이다. WebSocket·SensorRig·실제 도착/정지 판정·다중 차량 경합·현장 접근성·안전 및 성능 목표 달성은 포함하지 않는다.
- Unity SetDriveCommand는 km/h, Python 기본 속도는 m/s다. 향후 어댑터에서 단위와 AEQD/Unity 좌표·heading·tick/pose를 함께 검증해야 한다. 웹 demo와 일반 API의 독립 서비스 상태도 연결 대상 결정이 필요하다.
