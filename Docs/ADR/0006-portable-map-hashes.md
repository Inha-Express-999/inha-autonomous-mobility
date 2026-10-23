# ADR 0006: checkout 줄바꿈과 독립적인 지도 무결성 검사

상태: 채택 · 2026-10-23 · 프로젝트 0.4.4.0

## 문제

decision의 지도 manifest는 Windows 작업 파일의 원시 바이트를 해시했다. Git은 일부 파일을 LF로 저장하고 checkout 시 속성에 따라 LF/CRLF로 변환한다. 저장된 manifest의 23개 항목 중 19개가 Git blob과 달랐으며 18개는 CRLF 변환으로 일치했다. 출입구 CSV 한 항목은 두 방식 모두 불일치했다. 이 때문에 새 checkout의 API 초기화가 지도 해시 오류로 실패할 수 있었다.

## 결정

- 지도 manifest를 schema_version=2로 올리고 `hash_format=sha256-utf8-lf-v1`을 명시한다. 기존 schema_version=1 또는 알 수 없는 해시 방식은 재생성 안내와 함께 거부한다.
- 지도 원본 17개와 산출물 6개는 UTF-8로 읽고 CRLF/CR을 LF로 정규화한 후 UTF-8 SHA-256을 계산한다. BOM, 공백, 마지막 개행, JSON 값 및 배열 순서는 보존한다. 잘못된 UTF-8은 거부한다.
- `map_text_digest`를 지도 전용으로 사용한다. 기존 `digest`와 과거 실험 증거의 원시 바이트 해시 의미는 변경하지 않는다.
- Map Builder는 JSON과 출처 텍스트를 명시적 LF로 기록한다. 지도 원본·씬·프리팹을 수정하지 않고 현재 출입구 CSV를 포함한 원본에서 지도 묶음을 다시 생성한다.
- 도로 위상은 바꾸지 않으므로 map_version=inha-osm-unity-v2, 운행 설정 schema_version=3 및 HTTP/Unity schema_version=3은 유지한다.

## 호환성과 마이그레이션

기존 지도 묶음은 `python -m campus_sim.map_builder`로 재생성해야 한다. 원본·설정·생성 코드·VERSION의 실제 내용 변경은 계속 무효화 조건이다. 체크아웃별 LF/CRLF 차이만 무시하며 실제 통행·접근성 승인 상태는 변경하지 않는다. 과거 artifacts의 해시와 검증 결과는 해당 실행 시점의 기록으로 보존한다.

## 검증

`test_map_portability.py`는 LF/CRLF 원본 및 산출물 로딩, 바이트 단위 동일 재생성, 실제 원본/산출물 변조 거부, 구형/미지원 해시 방식 거부, 공백/BOM 보존 및 잘못된 UTF-8 거부를 검사한다. 실행 결과와 Unity 검증 범위는 `Docs/INTEGRATION_MAIN_DECISION.md`에 기록한다.
