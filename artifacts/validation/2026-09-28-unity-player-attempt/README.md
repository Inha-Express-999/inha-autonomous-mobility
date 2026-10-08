# Unity Windows Player 재검증 시도

2026-09-28 현재 작업 트리에서 다음 명령을 실행했다.

```powershell
./AgentScripts/RunUnityPhysicsIntegration.ps1 -Player -PythonPath ./tmp/mvp-venv/Scripts/python.exe -Port 18768 -TimeoutSeconds 600
```

스크립트는 격리된 소스 복사 프로젝트와 로컬 Python 통합 서버를 생성했다. Unity 6000.3.21f1 Editor가 라이선스 IPC 연결을 반복 실패했고, Package Manager는 라이선스가 허용하지 않아 Test Framework, Physics, Newtonsoft JSON 등의 필수 패키지를 등록하지 못했다고 기록했다([로그 발췌](license-log-excerpt.txt)). 실행 시작 때 복사한 파일의 hash는 [source-manifest.json](source-manifest.json)에 있다. Editor에서 테스트 XML이나 Windows test Player가 생성되기 전에 이 실행을 중단했다. 따라서 **테스트 통과·실패 판정은 없다**. 이전 2026-09-26 Player 결과는 당시 소스의 이력이며 현재 작업 트리의 결과로 대체하지 않는다.

이 실행 뒤 `RunUnityPhysicsIntegration.ps1`은 위와 같은 패키지 등록 거부 로그를 감지하면 즉시 명확한 오류를 반환하도록 변경했다. PowerShell parser 검사에서는 구문 오류가 없었다. 라이선스 서비스가 복구되면 같은 명령으로 현재 소스의 Player 통합 검증을 다시 실행해야 한다.
