# Meeting Translator

Windows 11의 **실시간 캡션(Live Captions)**을 무료 로컬 음성 인식기로 사용하고,
인식된 텍스트를 한국어로 번역해 회의별 SQLite·Markdown·CSV 기록으로 남기는 WPF 앱입니다.

## 동작 방식

```text
Teams / Zoom / 브라우저 / 마이크
              ↓
Windows Live Captions (로컬 STT, 무료)
              ↓ UI Automation
Meeting Translator
      ├─ Google 무료 번역(비공식)
      └─ Google Cloud Translation(공식)
              ↓
     실시간 표시 + SQLite
              ↓
       Markdown + CSV
```

Google Speech-to-Text와 오디오 업로드는 사용하지 않습니다. Windows 실시간 캡션이 PC 소리를
로컬에서 인식하므로 STT 사용료나 월 60분 제한이 없습니다.

## 주요 기능

- 실행 중인 Windows Live Captions에 안전하게 연결
- Live Captions가 없으면 자동 실행
- 사용자가 실행한 Live Captions 프로세스를 강제 종료하지 않음
- 900ms 동안 문장이 변하지 않으면 확정 문장으로 처리
- 두 가지 번역 모드
  - 무료 Google 번역: 키·결제 불필요, 비공식 엔드포인트
  - Google Cloud Translation: 공식 API와 서비스 계정 사용
- 공식 API 모드는 로컬 사용량 490,000자에서 자동 번역 중단
- 회의별 타임스탬프·원문·번역문 SQLite 저장
- 회의 종료 시 UTF-8 CSV와 Markdown 자동 내보내기

## 요구 사항

- Windows 11 22H2 이상
- .NET 8 Desktop Runtime
- Windows 실시간 캡션 언어 팩

## 빠른 시작

1. `Win + Ctrl + L`로 Windows 실시간 캡션을 켭니다.
2. 캡션 언어를 상대방이 말하는 언어(예: English (United States))로 설정합니다.
3. 내 목소리도 기록하려면 실시간 캡션 설정에서 `마이크 오디오 포함`을 켭니다.
4. Meeting Translator에서 무료 Google 번역을 선택합니다.
5. `회의 시작`을 누릅니다.
6. 끝나면 `회의 종료`를 눌러 Markdown과 CSV를 저장합니다.

결과 위치:

```text
문서\MeetingTranslator\Exports
```

설정·DB·월간 사용량은 `%LOCALAPPDATA%\MeetingTranslator`에 저장됩니다.

## 공식 Google Cloud Translation 사용

공식 API를 선택할 때만 다음 설정이 필요합니다.

1. Google Cloud 프로젝트를 선택합니다.
2. 결제 계정을 연결합니다. API 사용량에 따라 비용이 발생할 수 있습니다.
3. Cloud Translation API를 활성화합니다.
4. 전용 서비스 계정을 만들고 Translation API 사용에 필요한 최소 권한을 부여합니다.
5. JSON 키를 내려받아 저장소 밖의 안전한 위치에 보관합니다.
6. 앱에서 JSON 파일을 선택하고 `Google Cloud 공식 API` 모드를 사용합니다.

앱은 월 500,000자 무료 크레딧에 여유를 두고 **490,000자에서 번역을 중단**합니다.
이는 이 앱의 로컬 사용량 기준이므로 같은 결제 계정·프로젝트를 쓰는 다른 프로그램의 사용량까지
알 수는 없습니다. Google Cloud 예산 알림과 API 할당량도 함께 설정하세요.

서비스 계정 JSON은 비밀번호와 같은 비밀입니다. Git·메신저·화면 공유로 노출하지 마세요.

## 무료 Google 번역 모드 주의사항

무료 모드는 Chrome Dictionary 확장 프로그램 계열의 비공식 Google 번역 엔드포인트를 사용합니다.
API 키와 결제가 필요 없지만 Google이 앱용으로 안정성을 보장하지 않으며, 차단되거나 응답 형식이
변경될 수 있습니다. 중요한 업무에는 공식 Cloud Translation 모드를 권장합니다.

## 개발

```powershell
dotnet restore
dotnet build MeetingTranslator.sln -c Release
dotnet test MeetingTranslator.sln -c Release
dotnet run --project src/MeetingTranslator/MeetingTranslator.csproj
```

독립 실행 파일:

```powershell
dotnet publish src/MeetingTranslator/MeetingTranslator.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true
```

## 개인정보

- 음성 인식은 Windows Live Captions가 로컬에서 수행합니다.
- 번역 모드에서는 인식된 텍스트만 선택한 Google 번역 서비스로 전송됩니다.
- 회의 참가자의 동의와 회사 정책을 확인하세요.
- 회의 기록은 로컬 SQLite 및 내보낸 파일에 저장됩니다.

## 참고 프로젝트

Windows Live Captions UI Automation 접근 방식과 번역 파이프라인을 설계할 때
[SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator)를
참고했습니다. 해당 프로젝트는 Apache License 2.0입니다. 이 저장소의 구현은 독립적으로 작성했으며
참고 프로젝트의 소스 파일을 복사하지 않았습니다.

## 제한

- Live Captions의 비공개 UI 구조에 의존하므로 Windows 업데이트 후 조정이 필요할 수 있습니다.
- Windows Live Captions 결과에는 안정적인 화자 이름이 포함되지 않습니다.
- Teams 자체 전사 파일이나 Microsoft Graph와는 직접 연동하지 않습니다.

## 라이선스

MIT
