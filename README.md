# STOOM AI for 한글

한글 2024의 작업 창과 우클릭 메뉴에서 로컬 Codex 계정으로 대화하고, 문서 본문과 표를 편집하는 실험적 한애드온즈 확장입니다. 설치된 한글의 확장 API와 Codex CLI 연결을 사용합니다.

## 할 수 있는 일

- 우측 채팅과 우클릭 요청에서 문서 작성·선택 영역 편집을 요청합니다.
- 첫 번째 표 또는 커서가 있는 표를 기준으로 다른 표의 디자인을 분석하고 적용합니다. 셀 내용과 구조를 검사합니다.
- 한글의 쪽 텍스트와 표 안 문맥을 읽어 앞쪽 요약을 뒤쪽 상세 내용에 맞춰 갱신합니다. 세부 내용의 역할을 구분하고, 명확한 자료는 요청한 기준에 맞춰 반영합니다.
- 판단에 필요한 사실이 실제로 충돌하면 확인할 내용을 보여 주고, 사용자가 답하면 원래 갱신 요청을 이어갑니다.
- 모델과 추론 강도를 선택하고 일반 생성 요청에 추가 지시를 보낼 수 있습니다.

## 필요한 환경

- Windows와 한글 2024 설치본. 한글에서 한애드온즈 개발자 모드를 사용해야 합니다.
- Node.js와 로그인된 Codex CLI.
- STOOM `stoom-ar`의 로컬 `codex-native-host.mjs` 모듈. 이 저장소에는 모듈을 포함하지 않으므로 빌드할 때 경로를 직접 지정합니다.
- 한글 설치 폴더의 `HwpAddonHelper.dll`과 한컴 SDK 인터페이스 DLL. 이 저장소는 해당 DLL을 배포하지 않습니다.

## 빌드와 설치

PowerShell에서 이 저장소의 루트로 이동한 뒤, 실제 모듈 경로를 지정합니다.

```powershell
.\build.ps1 -StoomModule 'C:\경로\stoom-ar\system\codex-native-host.mjs'
.\install-context.ps1            # 설치 계획 확인
.\install-context.ps1 -Install   # 사용자별 한애드온즈 폴더에 설치
```

한글 설치 위치가 기본 경로와 다르면 `-HancomBin`을, Node.js 경로를 직접 지정하려면 `-NodePath`를 함께 사용합니다. 설치 스크립트는 같은 STOOM 확장의 이전 manifest를 백업합니다. 열려 있는 문서를 저장하고 한글을 모두 종료한 뒤 다시 실행하면 확장이 로드됩니다. 우측 창의 버전 표시로 확인할 수 있습니다.

이 빌드는 별도 설치 프로그램이 아니며, 컴퓨터마다 한글·Node.js·Codex 및 STOOM 모듈을 준비해야 합니다.

## 검증과 현재 범위

선택 문단 갱신과 합성 10쪽 문서의 쪽 간 자동 갱신을 실제 한글과 실제 Codex로 시험했습니다. 표 서식 계획·위치 확인과 충돌 검사는 저장된 문서에서 만든 합성 인터페이스 시험도 사용합니다. 실제 문서의 페이지 나눔, 병합 셀과 개체에 따라 결과가 다를 수 있으므로 새 기능은 복사본에서 확인하세요.

표 작업 중 다른 셀의 기존 텍스트가 바뀐 경우 재검증해 계속하는 경로는 실험적입니다. 텍스트 실행이나 셀 구조·서식이 동시에 바뀌는 경우 작업이 멈출 수 있으며, 실제 한글에서의 동시 입력은 추가 검증이 필요합니다. 진행 중 적용된 여러 작업을 한 번의 실행 취소로 되돌리는 기능은 보장하지 않습니다.

이미지의 시각적 내용은 쪽 텍스트 분석에 포함되지 않습니다. 현재 문단 위치·서식 검증이 가능한 범위에서 자동 갱신합니다.

일부 회귀 검사는 별도의 한글 설치나 로컬 시험 문서를 필요로 합니다. Node.js만으로 실행할 수 있는 예시는 다음과 같습니다.

```powershell
node bridge/tests/intent-route.test.mjs --stoom-module (Resolve-Path .\dist\StoomCodexContext\bridge\vendor\stoom-codex-native-host.mjs).Path
node bridge/tests/page-edit-plan.test.mjs
node bridge/tests/editor-host.test.mjs
```

## 구성

- `addin/`: 한글 작업 창, native 편집 및 문서 검증
- `bridge/`: Codex 연결, 요청 분류와 편집 계획
- `bridge/templates/`: 표·이미지 삽입용 HWPX 템플릿과 출처
- `build.ps1`, `install-context.ps1`: 로컬 빌드와 사용자별 설치

STOOM SVG·ICO 원본과 비율을 유지한 파생 아이콘은 `addin/assets/`에 포함되어 있습니다.
