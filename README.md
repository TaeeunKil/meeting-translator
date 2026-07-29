# Meeting Translator

Windows에서 **시스템 소리(Teams·Zoom·브라우저)**와 **내 마이크**를 각각 캡처해
Google Cloud Speech-to-Text로 실시간 전사하고, Cloud Translation으로 한국어 번역한 뒤
로컬 SQLite에 저장하는 WPF 앱입니다. 회의 종료 시 Markdown과 CSV 회의록을 자동 생성합니다.

## 주요 기능

- WASAPI loopback 시스템 오디오 캡처
- 기본 통신 마이크 캡처
- 시스템 소리는 `상대방`, 마이크는 `나`로 구분
- Google Cloud 실시간 스트리밍 전사와 중간 자막
- 영어 원문 → 한국어 번역
- 타임스탬프·원문·번역·신뢰도 로컬 SQLite 저장
- 회의 종료 시 UTF-8 CSV와 Markdown 내보내기
- 서비스 계정 파일과 로컬 데이터의 Git 제외

## 요구 사항

- Windows 10/11 x64
- .NET 8 SDK(개발) 또는 .NET 8 Desktop Runtime(실행)
- 결제가 연결된 Google Cloud 프로젝트
- 활성화된 Speech-to-Text API와 Cloud Translation API

> Google Cloud API는 사용량에 따라 비용이 발생합니다. 결제 계정 연결, API 활성화,
> 할당량·예산 알림 설정은 본인이 Google Cloud Console에서 직접 확인하고 승인하세요.

## Google Cloud 설정

1. [Google Cloud Console](https://console.cloud.google.com/)에서 프로젝트를 만들거나 선택합니다.
2. 결제 계정을 연결합니다. 이 단계부터 API 사용량에 따라 비용이 청구될 수 있습니다.
3. `Speech-to-Text API`와 `Cloud Translation API`를 활성화합니다.
4. IAM 및 관리자 → 서비스 계정에서 전용 서비스 계정을 만듭니다.
5. 최소 권한으로 `Cloud Speech Client`, `Cloud Translation API User` 역할을 부여합니다.
6. JSON 키를 내려받아 저장소 **밖의 안전한 위치**에 보관합니다.
7. 권장: 결제 → 예산 및 알림에서 월 예산 알림을 설정하고 API 할당량을 제한합니다.

서비스 계정 JSON 파일은 비밀번호와 같은 비밀입니다. Git, 메신저, 화면 공유에 노출하지 마세요.
노출되었다면 즉시 키를 폐기하고 새 키를 발급하세요.

## 실행

```powershell
dotnet restore
dotnet run --project src/MeetingTranslator/MeetingTranslator.csproj
```

앱에서 Google Cloud 프로젝트 ID와 서비스 계정 JSON 경로를 선택하고 `회의 시작`을 누릅니다.
회의가 끝나면 `회의 종료`를 누르세요. 결과는 기본적으로 다음 폴더에 저장됩니다.

```text
문서\MeetingTranslator\Exports
```

설정과 SQLite DB는 `%LOCALAPPDATA%\MeetingTranslator`에만 저장됩니다.

## 빌드와 테스트

```powershell
dotnet build MeetingTranslator.sln -c Release
dotnet test MeetingTranslator.sln -c Release
```

배포 파일 만들기:

```powershell
dotnet publish src/MeetingTranslator/MeetingTranslator.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true
```

## 개인정보 및 보안

- 오디오는 전사를 위해 Google Cloud로 스트리밍되며, 전사 텍스트는 번역을 위해 Google Cloud에 전송됩니다.
- 회의 참가자의 동의와 회사 보안 정책을 확인하세요.
- 앱은 원문과 번역문을 로컬 SQLite에 저장합니다.
- 서비스 계정 JSON 자체는 앱 설정에 경로만 저장되며 저장소에는 포함하지 않습니다.
- 운영 환경에서는 서비스 계정 키 대신 Workload Identity Federation 등 키 없는 인증을 검토하세요.

## 현재 MVP 제한

- 상대방 여러 명의 개별 화자 이름 분리는 하지 않습니다.
- 시스템 기본 출력 장치와 기본 통신 마이크를 사용합니다.
- 네트워크 또는 API 오류 시 잠시 후 스트리밍 세션을 다시 연결합니다.
- Google API 자격 증명이 없으면 실제 전사·번역 통합 테스트는 실행할 수 없습니다.

## 라이선스

MIT
