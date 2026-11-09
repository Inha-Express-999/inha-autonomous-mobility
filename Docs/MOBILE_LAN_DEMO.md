# Unity 모바일 클라이언트와 PC 서버 LAN 시연

2026-10-29 · 작업 트리 0.5.6.0 기반 미커밋 변경

시연은 기존 `PC_Bootstrap`과 `Mobile_Bootstrap`을 사용한다. 모바일은 `CampusWorld → CampusTerrain`과 `Mobile_Passenger`를 Additive로 로드하며, 기존 공통 Unity uGUI(`WebClientHud`라는 기존 클래스 이름)를 사용한다. 별도 모바일 웹 화면은 추가하지 않는다.

## 실행

1. PC와 휴대폰을 서로 통신할 수 있는 같은 LAN에 연결한다. PC 유선 + 휴대폰 Wi-Fi도 같은 라우터/서브넷이면 가능하다. 게스트 Wi-Fi나 대학 무선망의 단말 격리는 별도 확인한다.
2. PC에서 `PC_Bootstrap` Play 또는 최신 PC Player를 실행한다. PC가 Python 서버를 `0.0.0.0:8765`에 시작하며 PC 자신은 `127.0.0.1:8765`로 접속한다. 기존 서버 재사용과 부모 종료 시 소유 서버 종료 규칙을 유지한다.
3. Unity PC 관제 우상단 버전 왼쪽의 서버 주소를 확인한다. 기본 게이트웨이가 있는 네트워크의 IPv4 주소를 우선해 하나만 표시한다. 자동 선택이 휴대폰과 다른 네트워크이면 PC의 실제 LAN 주소를 확인한다. `0.0.0.0`은 입력 주소가 아니다. VPN/가상 어댑터의 주소 대신 실제 LAN 어댑터 주소를 사용한다.
4. 모바일 앱의 서버 주소 칸에 `192.168.0.10:8765`와 같이 PC IP와 포트를 입력하고 **연결**을 누른다. 주소는 해당 기기에 저장된다. `127.0.0.1`은 휴대폰 자신이므로 사용하지 않는다.
5. **서버 연결됨**을 확인한 뒤 서로 다른 출발·목적지 거점을 선택하고 필요한 경우 **계단 없는 접근·승하차 도움**을 선택한 뒤 **차량 호출**을 누른다.
6. PC 관제에서 운행을 시작한다. 모바일은 자기 요청의 배정·승차·운송·하차·완료 상태를 표시한다. 대기/배정 단계에서는 취소할 수 있다. PC가 정지 상태이면 요청을 접수해도 운행은 진행되지 않는다.

PC에는 Python 3.11 이상 호환 런타임이 필요하다. 배포 PC에 Python이 없거나 서버 의존성이 빠져 있으면 자동 설치하지 않으므로 `PC_SERVER_STARTUP.md`의 준비 절차를 따른다. 기존 PC 실행 파일은 새 서버 코드로 다시 패키징해야 한다.

모바일 클라이언트는 Unity 모바일 씬을 빌드한 네이티브 앱이다. 서버 주소는 브라우저 페이지가 아니라 PC의 Python 서비스 접속 위치다. 앱의 요청 접수·배차·운송 상태 조회와 취소는 같은 서버를 사용하므로 PC의 IP/포트가 필요하다. 입력한 주소는 기기에 저장되며, 현재 자동 검색이나 QR 연결은 구현하지 않았다.

## 모바일 빌드

- Unity Editor는 PC와 같은 **6000.3.21f1**을 유지한다.
- Android: `InhaExpress > Build > Mobile Android APK`. 해당 Editor의 Android Build Support, SDK/NDK, OpenJDK가 필요하다. 결과는 `artifacts/mobile-client/InhaMobil.apk`다.
- Windows에서 동일 모바일 씬/로직 검증: `InhaExpress > Build > Mobile Windows Preview`. 결과는 `.utmp/mobile-client/InhaMobilMobile.exe`다. 이 파일은 Android APK가 아니다.
- PC 배포: `InhaExpress > Build > PC LAN Demo`. 결과는 `artifacts/pc_lan_demo/InhaMobil.exe`와 같은 폴더의 `CampusServer/`다. 기존 빌드 프로필의 씬 목록/서브타깃을 바꾸지 않고 네 PC 씬을 지정해 빌드한다.
- 빌드는 기존 네 씬을 포함하며 모바일 산출물에만 HTTP LAN 접속 허용·인터넷 권한·세로 방향을 명시한다. 빌드 후 Editor의 이전 설정을 복구한다.
- Windows 미리보기는 `--server-url=http://<PC-IP>:8765 -screen-width 390 -screen-height 844 -screen-fullscreen 0`으로 초기 주소와 크기를 지정할 수 있다.
- iOS 단말의 최종 앱 빌드/서명/로컬 네트워크 권한은 이 Windows 환경에서 검증하지 않았다.

현재 Android APK는 생성하지 못했다. 동일 Editor 버전의 공식 Android 모듈과 SDK/NDK/OpenJDK 다운로드 및 Android 설치 파일의 공급자 MD5 대조는 완료했지만, Windows 관리자 승인이 완료되지 않아 설치가 오류 1223으로 종료됐다. 취소 후 설치를 다시 요청하지 않았다. Unity Hub에서 해당 버전의 Android Build Support 설치를 관리자 권한으로 완료한 후 위 APK 빌드를 실행해야 한다. [Unity 공식 모듈 설치 안내](https://docs.unity.com/en-us/hub/hub-cli-reference#install-modules).

## 연결 문제 진단

```powershell
Get-NetTCPConnection -LocalPort 8765 -State Listen
Invoke-RestMethod http://127.0.0.1:8765/v1/desktop/info
Invoke-RestMethod http://<PC-IP>:8765/v1/desktop/info
```

수신 주소는 `0.0.0.0`이어야 하며 두 조회의 `run_id`가 일치해야 한다. `127.0.0.1`만 수신하면 기존 서버를 종료한 뒤 최신 PC를 다시 실행한다. 재사용한 구버전 서버를 클라이언트가 강제로 종료하지 않는다.

PC 안에서는 접속되지만 휴대폰이 실패하면 PC의 네트워크 프로필, 방화벽, Wi-Fi 단말 격리 및 VPN 경로를 확인한다. 필요할 때 관리자 PowerShell에서 아래 스크립트를 실행하면 **Private 네트워크·동일 서브넷·TCP 8765**에 한정해 허용한다. Public 전체 허용이나 방화벽 끄기는 하지 않는다.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\enable_mobile_lan.ps1
```

규칙 제거: `Remove-NetFirewallRule -Name InhaMobil-Mobile-LAN-8765`.

## 상태와 경계

- 지도 hash·좌표 검증 후 공유 상태를 HTTP로 주기 조회한다. 기존 PC 물리 주행 WebSocket은 그대로 유지한다. 모바일이 새 Physics 제어 세션이나 Python 프로세스를 만들지 않는다.
- 모바일의 차량 점 표시는 서버의 표시용 Unity 좌표를 사용한다. 모바일의 로컬 차량 Rigidbody는 kinematic/메시는 숨김 처리해 별도 물리 시뮬레이션을 차량 위치로 오인하지 않는다. 이 좌표는 UI 출력이며 Python 센서 입력 계약은 바꾸지 않는다.
- 기기별 합성 승객 ID를 저장하고 본인 요청만 상태 화면에 선택한다. 이는 LAN 시연용 구분이며 사용자 인증/서비스 보안 완료를 뜻하지 않는다.
- 호출 중 네트워크 오류에는 같은 command ID로 재확인한다. 미확인 호출은 저장하고, 서버 run이 바뀌면 재접수를 막아 이전 세션 호출이 새 시연에 섞이지 않게 한다.
- PC의 일시정지와 출발 정책, 접근성/차량 능력 검증은 서버가 결정한다. 모바일 화면이 임의로 운송 완료를 만들지 않는다.

## 검증 근거

`artifacts/validation/2026-10-29-mobile-lan/`에 결과를 보관한다.

- Unity EditMode 주소 검증 10개 통과.
- Unity PlayMode 2개 통과: 모바일 거점 선택 → 실제 HTTP 호출 → 자기 요청 상태 → 동일 주 버튼의 취소 전환(새 호출 중복 없음), 기존 지도 HTTP 연결/단절 회귀. 최종 실행 XML의 시간은 2026-10-29 08:28:34–08:28:39 UTC다.
- Python LAN/세션/요청/재시도/취소/좌표 및 기존 서버·배차 관련 35개 통과. 전체 backend는 401개 통과/3개 실패다. 실패는 재생성 `policy.json`의 `hitech` Stop X/Z 끝자리(377.02397003392076 대 377.0239700339208 등)로 인한 지도 번들 바이트 일치 검사이며 LAN 변경 범위 밖의 현재 지도 원본/생성물 정합성 한계로 남겼다. 안전/지도 정책을 임의로 완화하지 않았다.
- Unity PC Editor가 소유한 `0.0.0.0:8765` 서버와 독립 Windows 모바일 Player를 실제 함께 실행했다. 모바일 UI에서 PC LAN IP를 입력하고 `60주년 → 비룡플라자`를 호출해 같은 run의 V03 배정/신선한 센서 관측/픽업 이동과 모바일 상태 표시를 확인했다. 동일 UI의 취소도 PC에서 `CANCELLED`로 확인했다. 근거: `native-pc-shared-state.json`, `native-cancel-state.json` 및 대화의 직접 UI 조작 기록.
- 최종 PC 배포 Player도 실제 실행했다. 서버 PID의 부모가 해당 Player임을 확인했고(`pc-player-startup.json`), 최신 모바일 Windows Player에서 같은 요청을 V02에 배정한 뒤 큰 주 버튼을 이용한 취소를 확인했다(`final-native-call-state.json`, `final-native-cancel-state.json`). PC 종료 시 소유 Python/8765 수신도 함께 종료됐으며, PC 재시작 후 모바일은 새 run에 자동 재접속하고 이전 취소 요청을 재전송하지 않았다(`pc-player-shutdown.json`, `pc-player-restart.json`, `final-player-sessions.log`).
- 해당 경로의 V03은 기존 LiDAR의 연속 clear scan 대기로 정지했다. 실제 물리 운송 완료를 검증했다고 주장하지 않는다. 모바일에는 서버가 보고한 물리 안전 대기 상태를 추가 표시한다.
- `scripts/validate_mobile_lan.py --lan-address <PC-IP> --output <json>`은 독립 소유 서버를 띄워 loopback/LAN 주소의 같은 run, 호출 재시도, PC 조회와 가상 시간 완료 및 정상 종료를 검사한다. 기존 운행 세션을 초기화하지 않는다.
- 같은 PC의 LAN IP 조회는 다른 휴대폰과의 Wi-Fi 통신을 증명하지 않는다. Android/iOS 실기기 접속·운송 상태 표시·성능은 별도 확인이 필요하다. 가상 시간 완료와 Unity Physics 실제 완료를 구분한다.

2026-10-29 · 주소 표시 정리 검증: `PcEnvironmentRegressionRunner.Run()` Unity PlayMode 5/5 통과. 지도 하단 주소 제거와 우상단의 단일 주소를 검사하고 3840×2160 캡처를 확인했다. 이 PC에서는 기본 게이트웨이를 가진 주소가 선택됐다. 이번 UI 변경에서 모바일 실기 접속/Player 빌드는 재실행하지 않았다. 근거: `artifacts/validation/2026-10-29-header-server-address/`.
