---
name: shelf-update
description: Внести зміни в код проекту Shelf (Поличка) за описом користувача. Тригериться, коли користувач пише «ВНЕСТИ ЗМІНИ: <опис>» (велики літерами або звичайними), або іншими формулюваннями про правки в коді Shelf: «внести зміни», «правки в shelf», «онови shelf», «відредагуй код», «зміни в Поличці», «зміни в коді», «допиши в shelf», «виправ у shelf». Виконує git pull, зупиняє Shelf.exe, читає релевантні файли, редагує згідно конвенцій з CLAUDE.md, збирає через dotnet build, повертає звіт. **НЕ комітить** - комміт окремим скіллом shelf-commit на команду «ЗРОБИ КОМІТ».
---

# Shelf Update Workflow

Цей skill активується, коли користувач хоче внести зміни в код проекту Shelf без негайного коміту. Користувач сам потім перевіряє результат руками і викликає `shelf-commit` коли все ок.

## Преконтролі (виконати ДО будь-яких дій)

1. **Перевір cwd**: має бути `D:\project\Polychka` (фізичний шлях проекту). Якщо інша - зупинись і скажи користувачу: «Цей skill працює тільки в D:\project\Polychka, поточна тека: <pwd>». Не запускати решту кроків.

2. **Перевір опис змін**: користувач має дати конкретний опис ПІСЛЯ «ВНЕСТИ ЗМІНИ:» або в наступному реченні. Якщо тригер є, а опис відсутній або занадто розмитий («зроби кращим», «оптимізуй»), спитай уточнень перш ніж робити що-небудь.

3. **Перевір чистоту робочої теки**:
   ```bash
   cd /d/project/Polychka && git status --short
   ```
   Якщо вже є незакомічені зміни - попередь користувача:
   - Покажи їх
   - Спитай: продовжувати правки поверх або спершу зробити коміт (через shelf-commit)?
   Не нашаровуй зміни без явного «так» від користувача - в одному коміті потім буде мікс з різних задач.

## Етапи виконання

### 1. Підтягнути найновіше з GitHub

```bash
cd /d/project/Polychka
git pull --ff-only
```

Якщо є конфлікт або не fast-forward - зупинись, скажи користувачу, що щось дивне (можливо, він правив через Web UI). Чекай інструкцій.

### 2. Зупинити запущену Shelf.exe

```powershell
Get-Process -Name Shelf -ErrorAction SilentlyContinue | Stop-Process
```

Або через bash:
```bash
taskkill.exe //F //IM Shelf.exe 2>/dev/null || true
```

Без цього `dotnet build` впаде з file-locked error на наступному кроці.

### 3. Зрозуміти, які файли потрібно правити

Використай Glob/Grep, щоб знайти релевантні місця. Для типових задач:
- **Зміни у віджеті X**: дивись `WidgetPlugins/Shelf.Widgets.X/` (XAML + cs)
- **Зміна в Settings UI**: `Views/SettingsWindow.xaml(.cs)`
- **Зміна теми**: `Shelf.Sdk/Theme.xaml` (стилі) або `Shelf.Sdk/Themes/Theme.Dark.xaml`/`Theme.Light.xaml` (палітра)
- **Локалізація / нові рядки**: `Shelf.Sdk/Strings.uk.xaml` + `Shelf.Sdk/Strings.en.xaml` (обидва!)
- **Загальні служби**: `Services/` (AppBar, SettingsService, WidgetManager, TrayIcon, VirtualDesktop)
- **Архітектура чи невідомо що**: спочатку прочитай `CLAUDE.md` - там детальний опис

Якщо не впевнений - прочитай файли, не вгадуй структуру.

### 4. Внести зміни з ОБОВ'ЯЗКОВИМ дотриманням конвенцій CLAUDE.md

**Найважливіше:**

- ✅ **Жоден видимий користувачу рядок не хардкодиться.** Кожен новий рядок (заголовок, плейсхолдер, повідомлення, tooltip) - **обов'язково в обох файлах**: `Shelf.Sdk/Strings.uk.xaml` і `Shelf.Sdk/Strings.en.xaml`, з однаковим x:Key. У XAML: `{DynamicResource Key}`. У C#: `Loc.Get("Key")` або `Loc.Format("Key", arg)`.

- ✅ **DynamicResource, не StaticResource** - у віджетах StaticResource не бачить ресурси з SDK на compile time.

- ✅ **ASCII дефіс `-`, не em-dash `—`** у видимих користувачу рядках. У коментарях і документах em-dash OK.

- ✅ **Українські лапки `«»`**, не `""` у видимих рядках.

- ✅ **DarkMessageBox.Show(...) замість MessageBox.Show(...)** - Win32 ламає темну тему.

- ✅ **Нове вікно**: у конструкторі після `InitializeComponent()` обов'язково `Polychka.Sdk.WindowChrome.Apply(this);` (або `Shelf.Sdk.WindowChrome.Apply(this);`).

- ✅ **SDK і віджети НЕ посилаються на хост-проект.** Замість `App.Widgets.SaveStates()` - `WidgetServices.RequestSaveStates()`.

- ✅ **Widget csproj НЕ має `<UseWindowsForms>true</UseWindowsForms>`.** Не використовуй `System.Windows.Forms.Clipboard` у віджетах - тільки WPF `System.Windows.Clipboard`.

Якщо вагаєшся - перевір `CLAUDE.md` (розділи «UI text conventions», «Architecture», «Adding a new widget» тощо).

### 5. Зібрати і перевірити

```bash
cd /d/project/Polychka
"/c/Program Files/dotnet/dotnet.exe" build Shelf.sln -c Debug 2>&1 | tail -15
```

Має бути:
- `Build succeeded.`
- `0 Error(s)`
- 1 Warning (WFAC010 про DPI) - **це нормально, відомий false-positive, ігноруй**.

Інші warnings - прочитай, оціни. Якщо релевантні до твоїх змін - виправ перш ніж казати «готово».

### 6. Якщо build впав - НЕ ПРОБУВАТИ ВИПРАВИТИ САМ

Покажи користувачу:
- Що саме впало (помилка, рядок, файл)
- Гіпотеза, чому
- Варіанти: (а) виправити так-то, (б) відкотити (`git checkout -- <файл>`), (в) інше

Чекай рішення користувача. Не лізь у файли більше без явного «спробуй варіант А».

### 7. Звіт користувачу (коли build зелений)

Стислий і структурований:

```
✅ Готово. Внесено зміни:

Змінені файли (N):
- WidgetPlugins/Shelf.Widgets.Clock/ClockSettingsDialog.xaml - додано ComboBox 12/24h
- WidgetPlugins/Shelf.Widgets.Clock/ClockWidget.xaml.cs - переключення формату
- Shelf.Sdk/Strings.uk.xaml - +2 ключі: Clock_Format_12h, Clock_Format_24h
- Shelf.Sdk/Strings.en.xaml - +2 ключі (англійські переклади)

Збірка: 0 Error, 1 Warning (відомий WFAC010)

Перевір вручну:
1. Запусти Shelf.exe з bin/Debug/net8.0-windows/
2. Правий клік на годиннику → Налаштування віджета
3. Перевір ComboBox: 12-год / 24-год
4. Перемкни, дивись формат у віджеті - має змінитись наживо
5. Перезапусти Shelf, переконайся, що вибраний формат зберігся

Якщо все ОК - напиши «ЗРОБИ КОМІТ».
Якщо щось не так - «ВНЕСТИ ЗМІНИ: <правка>».
```

## Що НЕ робити

- ❌ Не запускати `dotnet publish` чи `dotnet pack` - потребує команди користувача «ЗІБРАТИ EXE» згідно з глобальним правилом.
- ❌ Не запускати Shelf.exe після build - користувач сам запускає для тестів. Можна лише підказати команду.
- ❌ Не комітити, не пушити, не створювати теги. Цим займається skill shelf-commit.
- ❌ Не правити CHANGELOG.md за замовчуванням - він оновлюється під час підготовки релізу, не на кожен коміт. Виняток - якщо користувач явно попросив у описі.
- ❌ Не правити версію в Shelf.csproj - то частина release-процесу.
- ❌ Не торкатись `WidgetPlugins/Помічник.Widgets.*/` - це легасі, у gitignore, не активно компілюється.
- ❌ Не змінювати `.gitignore`, `LICENSE`, `CODE_OF_CONDUCT.md` без явного запиту.

## Особливі випадки

### Якщо треба додати новий віджет
Це не «ВНЕСТИ ЗМІНИ», це більше архітектурне. Активуй стандартний 99% → ГОТОВИЙ → ВИКОНУЙ протокол. Покрокова інструкція - в `CLAUDE.md` розділ «Adding a new widget».

### Якщо треба додати нову мову UI
Скопіюй `Strings.uk.xaml` → `Strings.<code>.xaml`, додай у `AppLanguage` enum, оновлюй `Loc.Initialize` switch, додай RadioButton у SettingsWindow. Скажи користувачу що це повний цикл, який потім потребує перезапуску додатка.

### Якщо треба додати нову тему
Створи `Themes/Theme.<Name>.xaml` з 15 brush-ключами, додай в `AppTheme` enum, в `Theme.Apply` switch, в `Settings_Theme_<Name>` в обох локалях, в ComboBox у SettingsWindow, в `TrayPalette.<Name>()` у `TrayIconService`. Деталі - в CLAUDE.md розділ «Themes».
