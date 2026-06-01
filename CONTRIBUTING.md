# Внесок у проект Поличка

Дякуємо, що хочеш допомогти! Цей документ пояснює, як зібрати проект локально, які конвенції коду ми тримаємо, і як надсилати pull request-и.

> English version is below. / English version is at the bottom of this file.

## Як зібрати локально

**Вимоги:**
- Windows 10 (1809+) або Windows 11
- .NET 8 SDK ([завантажити](https://dotnet.microsoft.com/download/dotnet/8.0))
- Visual Studio 2022 / JetBrains Rider / VS Code з C# Dev Kit (за бажанням)
- Git

**Команди:**

```powershell
git clone https://github.com/bridges-net-ua/shelf.git
cd shelf
dotnet build Shelf.sln -c Debug
Start-Process bin\Debug\net8.0-windows\Shelf.exe
```

Перед перезбіркою обов'язково зупини запущений екземпляр - `.exe` блокується доки програма працює:

```powershell
Get-Process -Name Shelf -ErrorAction SilentlyContinue | Stop-Process
```

Detailed architecture documentation is in [`CLAUDE.md`](CLAUDE.md) - the same file that guides AI assistants in this repo.

## Структура проекту

```
Shelf/
├── Shelf.csproj           # Host (WPF .exe)
├── App.xaml(.cs)
├── MainWindow.xaml(.cs)
├── Models/                   # AppSettings, WidgetEntry, ...
├── Services/                 # AppBarService, VirtualDesktopPinService, ...
├── Views/                    # SettingsWindow та інші вікна
├── Resources/                # shelf.ico, shelf.png
├── Shelf.Sdk/             # Спільний контракт + теми + локалі + WindowChrome
│   ├── IWidget.cs
│   ├── WidgetBase.cs
│   ├── WindowChrome.cs
│   ├── DarkMessageBox.xaml
│   ├── Theme.cs
│   ├── Theme.xaml
│   ├── Themes/Theme.Dark.xaml
│   ├── Themes/Theme.Light.xaml
│   ├── Loc.cs
│   ├── Strings.uk.xaml
│   └── Strings.en.xaml
└── WidgetPlugins/
    ├── Shelf.Widgets.Clock/
    ├── Shelf.Widgets.Notes/
    ├── Shelf.Widgets.Todo/
    ├── Shelf.Widgets.Photos/
    ├── Shelf.Widgets.Radio/
    ├── Shelf.Widgets.Weather/
    ├── Shelf.Widgets.Timer/
    ├── Shelf.Widgets.Stopwatch/
    ├── Shelf.Widgets.Holidays/
    └── Shelf.Widgets.Nba/
```

Кожен віджет - окремий WPF class library, що компілюється у `Shelf.Widgets.<Name>.dll` поруч з `Shelf.exe`. Хост статично референсить кожен через `<ProjectReference>`. Архітектура **не** runtime-плагінна.

## Конвенції коду

### Локалізація: ЖОДЕН видимий рядок не хардкодиться

- Усі видимі користувачу рядки живуть у `Shelf.Sdk/Strings.uk.xaml` і `Shelf.Sdk/Strings.en.xaml`.
- **Обов'язково додай ключ в обидва файли** з однаковим іменем (наприклад `Notes_Placeholder`).
- У XAML: `{DynamicResource Key}` (саме DynamicResource, не StaticResource - віджети не бачать ресурси хоста статично).
- У коді: `Loc.Get("Key")` або `Loc.Format("Key", arg0, arg1)`.

### Дефіс - ASCII (`-`), а не em-dash (`—`)

Тільки в **видимих** рядках. У коментарях і документації em-dash дозволений.

### Цитати - «...» (українські), а не "..."

У видимих рядках обох локалей.

### Теми: брэші через DynamicResource

Усі віджети мають читати кольори через `{DynamicResource AccentBrush}` тощо. Список 15 brush-ключів - у `Theme.Dark.xaml` / `Theme.Light.xaml`. При додаванні нової теми додай ключі в **обидва** файли.

### DarkMessageBox замість MessageBox

`System.Windows.MessageBox` рендериться у білому кольорі і ламає темну тему. Завжди використовуй `Shelf.Sdk.DarkMessageBox.Show(...)` - той самий API.

### WindowChrome.Apply для кожного нового вікна

У конструкторі вікна після `InitializeComponent()`:

```csharp
WindowChrome.Apply(this);
```

Це призначає іконку, темну панель заголовка, ✕-кнопку і округлені кути на Win11.

### Не посилатися на хост з SDK і віджетів

`Shelf.Sdk` і `Shelf.Widgets.*` **не повинні** мати посилань на `Shelf.csproj`. Це знищить можливість збирати їх окремо і поламає Microsoft Store packaging у майбутньому. Замість прямого виклику `App.Widgets.Save()` використовуй `WidgetServices.RequestSaveStates()`.

## Як додати власний віджет

1. Скопіюй структуру наявного віджета, наприклад `WidgetPlugins/Shelf.Widgets.Clock/`.
2. Перейменуй csproj, namespace, клас.
3. У `Shelf.csproj` додай `<ProjectReference Include="WidgetPlugins\Shelf.Widgets.<Name>\Shelf.Widgets.<Name>.csproj" />`.
4. У `Shelf.sln` додай новий проект (зберігай BOM!).
5. Реалізуй `IWidget` (або наслідуй `WidgetBase`):
   - `Id`, `DisplayName`, `Description`, `HasSettings`
   - `CreateView()` повертає твій `UserControl`
   - `SaveState()` / `LoadState(json)` для персистентності
6. Локалізуй усі рядки (обидві мови).
7. Тестуй: збери проект, запусти, відкрий Налаштування → Віджети, додай свій тип.

Більше деталей у [`CLAUDE.md`](CLAUDE.md), розділ «Adding a new widget».

## Інструменти (`tools/`)

Допоміжні PowerShell-скрипти (для звичайної збірки не потрібні):

- **`make-ico.ps1`** - генерує мультирозмірний `Resources/shelf.ico` з PNG-джерела. Запусти після оновлення логотипа:

  ```powershell
  .\tools\make-ico.ps1 -SourcePng Resources\shelf.png -OutIco Resources\shelf.ico
  ```

  Розміри за замовчуванням - 16/24/32/48/64/128/256. Працює на `System.Drawing`, ImageMagick не потрібен.
- **`make-store-assets.ps1`** - генерує PNG-ассети для MSIX (Square44x44, Square150x150, Wide310x150, StoreLogo, SplashScreen) у `Shelf.Package/Assets/`.
- **`make-msix.ps1`** - збирає `.msix`-пакет для Microsoft Store (потрібен Windows SDK; прапорець `-Sign` робить self-signed підпис для локального тесту).

## Pull request workflow

1. **Перш ніж починати велику зміну** - відкрий [Issue](https://github.com/bridges-net-ua/shelf/issues) для обговорення підходу.
2. Форкни репо, створи гілку від `main`: `git checkout -b feat/my-feature`.
3. Внеси зміни, перевір локальною збіркою.
4. Якщо зміна користувацька - онови `CHANGELOG.md` (секція `[Unreleased]`).
5. Дотримайся конвенцій з цього файлу.
6. Зроби commit з осмисленим message (укр або англ - як зручніше).
7. `git push origin feat/my-feature`, відкрий PR проти `bridges-net-ua/shelf:main`.
8. У PR-описі: що змінилось, чому, як тестував.

GitHub Actions автоматично перевірить збірку. Після успіху мейнтейнер ревьюнить і мерджить.

## Кодекс поведінки

Учасники зобов'язуються дотримуватись [Кодексу поведінки](CODE_OF_CONDUCT.md) - стандартний Contributor Covenant 2.1.

## Питання?

- [GitHub Discussions](https://github.com/bridges-net-ua/shelf/discussions) - для загальних питань і обговорень.
- [GitHub Issues](https://github.com/bridges-net-ua/shelf/issues) - для багів і пропозицій конкретних змін.
- Email: [shelf@bridges.net.ua](mailto:shelf@bridges.net.ua) - для приватних звернень.

---

# Contributing (English)

Thanks for your interest in contributing! See the Ukrainian section above for full details. Key points:

- **Build**: `dotnet build Shelf.sln -c Debug`, then `Start-Process bin\Debug\net8.0-windows\Shelf.exe`. Stop running instance first - the .exe locks while running.
- **Localize everything**: never hardcode a UI string. Add a key to both `Strings.uk.xaml` and `Strings.en.xaml`, reference via `{DynamicResource Key}` or `Loc.Get`.
- **Use ASCII `-` not em-dash `—`** in user-visible strings.
- **Use Ukrainian quotes «...» not "..."** in user-visible strings.
- **Use `DynamicResource` not `StaticResource`** for theme/string keys (widget assemblies can't see host App.xaml at compile time).
- **Use `DarkMessageBox` not `MessageBox`** - the Win32 MessageBox breaks the dark theme.
- **Call `WindowChrome.Apply(this)`** in every Window constructor after `InitializeComponent()`.
- **Do not reference the host project** from SDK or widget projects - this would break Microsoft Store packaging.
- **Open an issue first** for large changes.
- **Update CHANGELOG.md** (`[Unreleased]` section) for user-visible changes.
- **Regenerate the icon** after updating `Resources/shelf.png`: `.\tools\make-ico.ps1 -SourcePng Resources\shelf.png -OutIco Resources\shelf.ico`.

Architecture details are in [`CLAUDE.md`](CLAUDE.md).

All contributors must follow the [Code of Conduct](CODE_OF_CONDUCT.md).
