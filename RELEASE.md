# Release Process - Shelf

Цей документ описує, як випустити нову версію Shelf. Процес автоматизовано через GitHub Actions: розробник створює git-тег `vX.Y.Z`, GitHub у хмарі збирає `.exe` і прикріплює до сторінки Releases.

> Якщо ти питаєш «як випустити», «release», «installer», «signed exe» - цей файл є посиланням першого вибору.

---

## Що вже вирішено

- **Канал розповсюдження**: GitHub Releases (`github.com/bridges-net-ua/shelf/releases`). Безкоштовно, інтеграція з GitHub Actions.
- **Виконуваний файл**: один `Shelf.exe` (host) + DLL поряд (SDK + 9 widgets), запакований у single-file через `-p:PublishSingleFile=true`. Кілька WPF-нативних DLL залишаються поза bundle - вони лежать поруч.
- **Target framework**: `net8.0-windows`. Збираємо `--self-contained true` - bundle ~70-80 MB, користувач не повинен встановлювати .NET.
- **Architecture**: лише `win-x64`. ARM64 поки не плануємо.
- **Versioning**: семантичне ([SemVer](https://semver.org/lang/uk/)), джерело - `<Version>` у `Shelf.csproj`. Вкладка «Про програму» читає з `Assembly.GetExecutingAssembly().GetName().Version` через `SetupAbout()`.
- **Ліцензія**: MIT (див. [LICENSE](LICENSE)).
- **Git/GitHub**: репо на `github.com/bridges-net-ua/shelf`, гілка `main`.

## Що ще `[TODO]` (поза скоупом першого релізу)

- **Code signing**: чи купуємо Authenticode-сертифікат (~$200-400/рік) - відкладено. Деталі нижче в розділі «Підпис коду».
- **Microsoft Store** (MSIX): окреме завдання, dev-акаунт $19 одноразово.
- **Auto-update mechanism**: Velopack / Squirrel / власний - не зараз. Користувач завантажує нову версію вручну з Releases.
- **Installer (Inno Setup)**: не плануємо - portable zip достатньо для нашого випадку. Якщо знадобиться - деталі нижче.
- **winget / Chocolatey**: майбутнє, не для v1.0.

---

## Перед кожним релізом - чек-лист

### 1. Підняти версію у `Shelf.csproj`

```xml
<Version>1.2.0</Version>
<FileVersion>1.2.0.0</FileVersion>
<AssemblyVersion>1.2.0.0</AssemblyVersion>
```

Семантика:
- **MAJOR** (1.x → 2.0) - перелом сумісності: видалення віджета, переробка settings.json без міграції, зміна вимог до системи.
- **MINOR** (1.2 → 1.3) - новий віджет, нова мова/тема, нова функція.
- **PATCH** (1.2.0 → 1.2.1) - bug fix, переклади, дрібні UI-поліпшення.

### 2. Оновити `CHANGELOG.md`

- Перенеси записи з `[Unreleased]` під новий `[1.2.0] - YYYY-MM-DD`.
- Додай новий порожній `[Unreleased]` зверху.
- Внизу файлу онови посилання порівняння:

```markdown
[Unreleased]: https://github.com/bridges-net-ua/shelf/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/bridges-net-ua/shelf/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/bridges-net-ua/shelf/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/bridges-net-ua/shelf/releases/tag/v1.0.0
```

### 3. Локальний smoke test

```powershell
Get-Process -Name Shelf -ErrorAction SilentlyContinue | Stop-Process
dotnet build Shelf.sln -c Release
Start-Process bin\Release\net8.0-windows\Shelf.exe
```

Перевір:
- Запустити, додати кілька різних віджетів, перезапустити - стан зберігся.
- Перемкнути тему Dark ↔ Light без рестарту.
- Перемкнути мову Uk ↔ En (потребує рестарту).
- **Holidays**: додати свято і ДН у редакторі з усіма полями (Year, Emoji, Note).
- **Holidays**: експорт JSON → видалити запис → імпорт JSON → запис на місці з усіма полями.
- **Holidays**: експорт TXT → отримати попередження про втрату іконок → продовжити → імпорт TXT.
- **Photos**: вибрати папку, побачити зміну фото.
- **Radio**: обрати станцію, відтворити, гучність.
- **Weather**: переконатися, що кеш малюється одразу при старті.
- **Timer**: дочекатись завершення відліку → почути алярм.
- **Stopwatch**: Start → Lap → Pause → Reset.

### 4. Коміт версійних змін

```powershell
git add Shelf.csproj CHANGELOG.md
git commit -m "Release v1.2.0"
git push origin main
```

Дочекайся, поки `build.yml` workflow стане ✅ - це підтверджує, що збірка з `main` не поламана.

---

## Сам реліз

### Варіант A - через GitHub Web UI (рекомендовано)

1. У репо: **Releases → Draft a new release**.
2. **Choose a tag** → ввести `v1.2.0` → **Create new tag: v1.2.0 on publish**.
3. **Release title**: `Shelf v1.2.0`.
4. **Description**: скопіюй запис з `CHANGELOG.md` (без квадратних дужок навколо версії).
5. **Set as the latest release** - залишити галочку.
6. **Publish release**.

GitHub автоматично:
- Створить тег `v1.2.0` на поточному `main`.
- Запустить `release.yml` workflow.
- Через 3-5 хв прикріпить `Shelf-v1.2.0-win-x64.zip` (~70 МБ) до релізу.

### Варіант B - через командну стрічку

```powershell
git tag v1.2.0
git push origin v1.2.0
```

Потім зайди в **Releases** → знайди auto-створений реліз → **Edit** → встав опис з CHANGELOG → **Publish**.

---

## Що робить `release.yml` workflow

Деталі в `.github/workflows/release.yml`. Коротко:

```powershell
dotnet publish Shelf.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish\win-x64
```

Результат - папка `publish\win-x64\` з `Shelf.exe` плюс WPF-нативними DLL, що не злилися в single-file. Workflow пакує всю папку в `Shelf-vX.Y.Z-win-x64.zip` і прикріпляє до релізу.

> **Локально не запускати!** Згідно з глобальним правилом користувача, `dotnet publish` дозволено тільки після команди `ЗІБРАТИ EXE`. У хмарі GitHub Actions виконується автоматично без обмежень.

---

## Після релізу

- Перевір на сторінці Releases, що zip прикріпився і завантажується.
- Завантаж його на чисту Windows-машину, розпакуй, запусти - переконайся, що працює.
- Закрий релевантні issues з мітками `fixed-in-next-release`, додавши коментар з посиланням на реліз.
- (Опційно) Анонс у GitHub Discussions, на сайті, у соцмережах.

---

## Hotfix-релізи

Якщо в опублікованій версії виявлено критичний баг:

1. Створи гілку від тегу: `git checkout -b hotfix/1.2.1 v1.2.0`.
2. Виправ баг.
3. Підніми версію в csproj до `1.2.1`, додай запис у CHANGELOG.
4. PR в `main`, мердж.
5. Створи реліз `v1.2.1` за вищеописаною процедурою.

---

## Підпис коду `[TODO]`

Зараз .exe не підписаний. Це означає:

- **SmartScreen** показує «Windows protected your PC» при першому запуску → потрібен клік «More info → Run anyway».
- **Smart App Control** (Win11) повністю блокує запуск (Event ID 3077 у `Microsoft-Windows-CodeIntegrity/Operational`). Шляхи обходу: SAC=Off (one-way), або справжній підпис.

Коли купуватимемо сертифікат:

- **OV (Organisation Validation, ~$200/рік)** - звичайний, репутацію SmartScreen набираєш за 2-4 тижні; SAC - не гарантовано.
- **EV (Extended Validation, ~$300-400/рік + hardware token)** - миттєва репутація з першого дня, SAC точно прийме.
- Постачальники: Sectigo, DigiCert, SSL.com.

Скрипт підпису після `dotnet publish`:

```powershell
$signTool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
$cert = "thumbprint_or_pfx"
$timestamp = "http://timestamp.digicert.com"

Get-ChildItem publish\win-x64\*.exe, publish\win-x64\*.dll | ForEach-Object {
    & $signTool sign /sha1 $cert /fd SHA256 /tr $timestamp /td SHA256 $_.FullName
}
```

В GitHub Actions - покласти сертифікат у GitHub Secrets, додати крок `azure/trusted-signing-action` або власний PowerShell-step з `signtool`.

---

## Installer (Inno Setup) `[TODO]`

Не плануємо - portable zip достатньо. Якщо знадобиться:

- `tools\shelf.iss` (Inno Setup script).
- Вхід: вміст `publish\win-x64\`.
- Вихід: `Shelf-vX.Y.Z-setup.exe`.
- Шлях встановлення: `{userappdata}\Shelf` (per-user, без admin) або `{commonpf}\Shelf` (per-machine).
- Ярлики: на робочому столі + у меню Пуск.

---

## MSIX (Microsoft Store) `[TODO]`

Окреме завдання. Потребує:

- `Windows Application Packaging Project` у `.sln`.
- Microsoft Partner Center dev-акаунт ($19 одноразово).
- Privacy Policy URL (обов'язково для Store - Shelf робить network-запити до Open-Meteo).
- Store certification (~3-7 днів).

---

## Корисні посилання

- [Keep a Changelog](https://keepachangelog.com/uk/1.1.0/)
- [Semantic Versioning](https://semver.org/lang/uk/)
- [Velopack (auto-update)](https://github.com/velopack/velopack)
- [Microsoft Defender SmartScreen](https://learn.microsoft.com/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/)
- [Smart App Control](https://learn.microsoft.com/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/smart-app-control)
- [winget-pkgs](https://github.com/microsoft/winget-pkgs)
- [Microsoft Store for developers](https://developer.microsoft.com/microsoft-store/)
