# Meeting Translator

Windows 11 또는 Microsoft Teams의 **라이브 캡션**을 읽어 영어 원문을 한국어로 번역하고, 대화형 회의록으로 쌓는 WPF 앱입니다. 별도 Speech-to-Text API 없이 동작하며, 회의 종료 시 Markdown과 CSV를 생성합니다.

## 캡션 소스

- **Windows 자막 (기본값)**: Teams, Zoom, 브라우저 등 PC에서 재생되는 모든 소리를 Windows 11 라이브 캡션으로 인식합니다. 범용이지만 화자 이름은 구분하지 않습니다.
- **Teams 자막**: Teams 회의의 라이브 캡션 영역을 직접 읽습니다. 화면에 화자 이름이 표시되면 이름과 발언을 함께 저장합니다. Teams 업데이트에 따른 화면 구조 변경의 영향을 받을 수 있습니다.

## 번역 엔진

- **무료 Google (기본값)**: API 키 없이 바로 사용합니다. 비공식 엔드포인트이므로 개인용·실험용에 적합하며 Google의 제공 방식이 바뀌면 중단될 수 있습니다.
- **Google Cloud Translation**: 공식 API입니다. Google Cloud 프로젝트와 서비스 계정 JSON이 필요하며 사용량에 따라 비용이 발생할 수 있습니다.
- **사내 Qwen**: OpenAI 호환 API를 사용합니다. 기본값은 `http://172.30.1.57:8400/v1`, 모델은 `qwen3.5-27b`입니다. 사고 모드는 끄며, 연결 실패 시 무료 Google로 대체할 수 있습니다.

## 요구 사항

- Windows 11 x64와 Windows 라이브 캡션 또는 Microsoft Teams 데스크톱 앱
- .NET 8 SDK(개발) 또는 .NET 8 Desktop Runtime(실행)
- 인터넷 연결(무료 Google 또는 Google Cloud 사용 시)
- 사내망 연결(Qwen 사용 시)

## 실행

PowerShell에서 다음을 실행합니다.

```powershell
cd C:\Users\user\Documents\Codex\2026-07-30\github\meeting-translator
dotnet restore
dotnet run --project src\MeetingTranslator\MeetingTranslator.csproj
```

앱이 열리면:

1. 왼쪽 위에서 `Windows 자막` 또는 `Teams 자막`을 선택합니다.
2. 그 아래에서 번역 엔진을 선택합니다. 처음에는 `무료 Google`이 선택되어 있습니다.
3. Windows 자막은 자막 언어를 영어로 설정합니다.
4. Teams 자막은 회의에서 라이브 캡션을 켜고 가능하면 캡션을 별도 창으로 팝아웃합니다.
5. `회의 시작`을 누르고, 종료할 때 `회의 종료`를 누릅니다.

결과는 다음 위치에 저장됩니다.

```text
문서\MeetingTranslator\Exports
```

설정과 SQLite DB는 `%LOCALAPPDATA%\MeetingTranslator`에 저장됩니다.

## 선택 사항: Google Cloud Translation

Google Cloud를 사용할 때만 다음 설정이 필요합니다.

1. Google Cloud Console에서 프로젝트를 만들거나 선택합니다.
2. 결제 계정을 연결하고 `Cloud Translation API`를 활성화합니다.
3. 번역 전용 서비스 계정을 만들고 필요한 최소 역할만 부여합니다.
4. JSON 키를 저장소 밖의 안전한 위치에 내려받습니다.
5. 앱에서 프로젝트 ID와 JSON 파일을 선택합니다.

서비스 계정 JSON은 비밀번호와 같은 비밀입니다. 비공개 저장소에도 커밋하지 마세요. 앱은 파일 자체가 아니라 경로만 로컬 설정에 저장합니다.

앱은 Google Cloud Translation 호출량을 로컬에서 월별로 기록하고 `490,000자`에 도달하기 전에 추가 호출을 차단합니다. 현재 Google의 NMT 무료 크레딧 범위인 월 500,000자보다 10,000자 낮게 잡은 보호 한도입니다. 이 값은 이 앱에서 보낸 문자만 계산하므로 같은 Google Cloud 계정을 사용하는 다른 앱의 사용량까지 보장하지는 않습니다.

## 빌드와 테스트

```powershell
dotnet build MeetingTranslator.sln -c Release
dotnet test MeetingTranslator.sln -c Release
```

## 개인정보 및 제한

- 무료 Google 또는 Google Cloud를 선택하면 자막 텍스트가 해당 서비스로 전송됩니다.
- Qwen을 선택하면 자막 텍스트가 설정된 사내 서버로 전송됩니다.
- Windows 라이브 캡션의 인식은 로컬에서 처리되지만, 회의 참가자 동의와 회사 정책을 확인해야 합니다.
- Windows 자막 소스는 개별 화자 구분을 지원하지 않습니다.
- Teams 자막 소스는 화면에 표시된 화자 이름을 읽습니다. 참가자가 이름 표시를 끄거나 Teams의 접근성 구조가 바뀌면 이름을 가져오지 못할 수 있습니다.
- 앱은 화자, 원문과 번역문을 로컬 SQLite에 저장합니다.

## 라이선스

MIT
