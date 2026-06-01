# План публікації Поличка (Shelf) як Open-Source

> Робочий журнал релізу. Усі 6 етапів плану виконано, додатково випущено hotfix v1.0.1.
> Документ далі підтримуємо як список побажань і майбутніх покращень.
> Останнє оновлення: 2026-05-27

## 🟢 Фінальний стан

| Що | Де |
|---|---|
| **Код** | https://github.com/bridges-net-ua/shelf |
| **Сайт** | https://shelf.bridges.net.ua/ (UA) · [/en/](https://shelf.bridges.net.ua/en/) |
| **Завантаження** | https://github.com/bridges-net-ua/shelf/releases/latest (v1.0.1) |
| **Issues / спільнота** | https://github.com/bridges-net-ua/shelf/issues |
| **CI** | https://github.com/bridges-net-ua/shelf/actions |
| **Витрати** | **$0** (домен `bridges.net.ua` уже був, усе решта в безкоштовних лімітах GitHub) |


## Мета

Розмістити додаток Поличка як open-source проект на GitHub з мінімальними фінансовими витратами ($0 зараз). Підготувати інфраструктуру так, щоб у майбутньому можна було без переробок опублікувати додаток у Microsoft Store.

## Прийняті рішення

| Питання | Рішення |
|---|---|
| Хостинг коду | GitHub, організація `bridges-net-ua`, репозиторій `shelf` |
| Ліцензія | MIT |
| Дистрибуція `.exe` | GitHub Releases + GitHub Actions автозбірка на тег `v*` |
| Хостинг сайту | GitHub Pages з теки `/docs` того ж репо |
| Власний домен сайту | `shelf.bridges.net.ua` (піддомен наявного `bridges.net.ua`) |
| GitHub-акаунт | Особистий: `BridgesCom`. Організація: `bridges-net-ua`. |
| Мова сайту і README | Двомовний: українська (основна) + англійська |
| Початкова версія | `1.0.0` → `1.0.1` (hotfix) |
| Легасі-теки (`WidgetPlugins/Помічник.Widgets.*`, `WidgetPlugins/Polychka.Widgets.*`, `bin/`, `artifacts/`, `*.log`) | Додано в `.gitignore` без видалення з диска |
| Скріншоти | Заглушки SVG, користувач додасть реальні файли пізніше |
| Контактний email | `shelf@bridges.net.ua` (вже в `App_About`) |
| MSIX / Microsoft Store | Пізніше, окремим завданням |
| Тип збірки `.exe` | ~~Self-contained single-file~~ → **Self-contained folder**, win-x64 (~64 МБ zip, після розпакування ~200 файлів). Single-file ховав widget DLL всередину `.exe`, через що `WidgetRegistry` не знаходив їх — виправлено в v1.0.1. |
| Технологія сайту | Чистий HTML + CSS, без фреймворків і build-кроку |
| Платформа збірки | Тільки win-x64 (Shelf — Windows-only через AppBar API) |

## Витрати

| Зараз | Майбутнє (опціонально) |
|---|---|
| $0 | Microsoft Store dev account: $19 одноразово |
| | Code signing certificate (EV): ~$200-400/рік |
| | Власний домен 2-го рівня (`shelf.app`): ~$10/рік |

---

# Етапи виконання

Кожен етап — окрема одиниця роботи з власною перевіркою. Перед переходом до наступного етапу обов'язково проходимо чек-лист «Перевірка перед наступним етапом».

Статуси: ⬜ заплановано · 🟡 в роботі · ✅ виконано

---

## ✅ Етап 1 — Базові файли проекту

**Хто робить:** Claude локально.
**Очікуваний час:** ~5 хв роботи.

### Що буде створено в корені `D:\project\Shelf\`

- `LICENSE` — повний текст MIT-ліцензії, рік 2026, правовласник «Bridges Community».
- `.gitignore` — стандартний шаблон для .NET + специфіка:
  - `bin/`, `obj/`, `*.user`, `.vs/`, `artifacts/`
  - `WidgetPlugins/Помічник.Widgets.*/` (легасі, локально не видаляється)
  - `*.log`, `shelf-*.json`, `shelf-*.txt`, `birth.txt`, `dates.txt`, `out.log`, `err.log`
  - `Resources/shelf.ico.bak`
- `README.md` — головна сторінка репо, українською. Секції: hero (логотип + назва + бейджі), опис, скріншоти, віджети, як завантажити, як зібрати з джерел, ліцензія, посилання на англійську версію.
- `README.en.md` — повна англійська версія, з посиланням на українську вгорі.
- `CHANGELOG.md` — у форматі Keep a Changelog. Перший запис — `[1.0.0] - 2026-05-26` зі списком усіх віджетів і ключових можливостей як стартового набору.
- `CONTRIBUTING.md` — як зібрати локально, конвенції коду (двомовні рядки, DynamicResource у XAML, ASCII-дефіс у видимих рядках, `DarkMessageBox` замість Win32), як надсилати PR.
- `CODE_OF_CONDUCT.md` — Contributor Covenant 2.1 з контактом `shelf@bridges.net.ua`.
- `RELEASE.md` — інструкція для випуску нової версії: оновити `<Version>` у `Shelf.csproj`, оновити `CHANGELOG.md`, створити git tag `vX.Y.Z`, push tag, GitHub Actions автоматично збере і опублікує реліз.
- `SETUP.md` — покрокова інструкція для користувача на етапи 4-6 (реєстрація, push, DNS, Pages, перший реліз).

### Перевірка перед наступним етапом

- [x] 9 файлів існують у корені.
- [x] `README.md` коректно відображається у будь-якому Markdown-переглядачі.
- [x] `.gitignore` містить правильні патерни (перевірено).

---

## ✅ Етап 2 — Автоматизація GitHub Actions

**Хто робить:** Claude локально.
**Очікуваний час:** ~3 хв.

### Що буде створено

- `.github/workflows/build.yml` — тригер `push` і `pull_request` на гілку `main`. Runner `windows-latest`, кроки: `actions/checkout@v4`, `actions/setup-dotnet@v4` (.NET 8), `dotnet build Shelf.sln -c Debug`. Мета: швидко ловити поламані коміти.
- `.github/workflows/release.yml` — тригер `push` тега `v*`. Кроки: checkout, setup-dotnet, `dotnet publish Shelf.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true`, zip артефактів, `softprops/action-gh-release@v2` створює реліз з прикріпленим `Shelf-vX.Y.Z-win-x64.zip`.
- `.github/ISSUE_TEMPLATE/bug_report.md` — форма для бага: версія Windows, версія Поличка, кроки відтворення, очікувана vs фактична поведінка.
- `.github/ISSUE_TEMPLATE/feature_request.md` — форма для пропозиції.
- `.github/ISSUE_TEMPLATE/config.yml` — посилання на GitHub Discussions для питань.
- `.github/PULL_REQUEST_TEMPLATE.md` — мінімум: опис, чек-лист (тестував локально, оновив CHANGELOG якщо треба, дотримався конвенцій з CONTRIBUTING).

### Перевірка перед наступним етапом

- [x] 6 файлів існують у `.github/`.
- [x] YAML-файли валідні синтаксично.
- [x] Логіка release.yml використовує тег як версію у назві артефакту.
- [x] В hotfix v1.0.1 з release.yml прибрано `-p:PublishSingleFile=true`, щоб widget DLL ішли як окремі файли.

---

## ✅ Етап 3 — Сайт-візитівка

**Хто робить:** Claude локально.
**Очікуваний час:** ~10 хв (найбільший з моїх).

### Що буде створено в `docs/`

- `index.html` — українська головна. Секції:
  - **Hero**: великий логотип, «Поличка», слоган, кнопка «Завантажити Поличку» (→ `https://github.com/bridges-net-ua/shelf/releases/latest`).
  - **Що це**: 2-3 абзаци про док-панель на правому краю екрана.
  - **9 віджетів**: сітка з іконками і назвами (Годинник, Замітки, Список задач, Слайд-шоу, Радіо, Погода, Таймер, Секундомір, Свята).
  - **Скріншоти**: галерея 3-4 заглушок.
  - **Завантаження**: посилання на Releases, системні вимоги (Windows 10+, x64).
  - **Про автора**: «Bridges Community», посилання на репо, email.
  - **FAQ**: 4-5 типових питань (чи безкоштовно, чи можна на Win7, чи буде в Microsoft Store, як вимкнути автозапуск, як видалити).
  - **Контакти**: email, GitHub Issues.
  - **Перемикач мови**: посилання `EN ↗` на `/en/`.
- `en/index.html` — англійська версія тієї ж сторінки.
- `style.css` — темний дизайн в тон додатку. Палітра з `Theme.Dark.xaml` (`#2A2A30` background, `#5C5C62` accent, `#E8E8E8` text), шрифт Segoe UI, responsive (mobile-friendly).
- `assets/logo.png` — копія `Resources/shelf.png`.
- `assets/favicon.ico` — копія `Resources/shelf.ico`.
- `assets/og-image.png` — превʼю для шерингу 1200×630, генерую з логотипу.
- `assets/screenshot-1.png`, `screenshot-2.png`, `screenshot-3.png` — кольорові SVG-заглушки з підписом «Скріншот буде доданий».
- `CNAME` — один рядок: `shelf.bridges.net.ua`.
- `.nojekyll` — порожній файл, щоб GitHub Pages не запускав Jekyll.

### Перевірка перед наступним етапом

- [x] Усі файли існують у `docs/`.
- [x] Сайт виглядає як треба (темна тема, логотип, секції, кнопки, навігація — перевірено наживо на https://shelf.bridges.net.ua/).
- [x] Кнопка «Завантажити» веде на `github.com/bridges-net-ua/shelf/releases/latest` (тепер працює, v1.0.1).
- [x] Перемикач мови UA ↔ EN працює (`/en/` версія коректна).

---

## ✅ Етап 4 — Реєстрація GitHub + перший push

**Хто робить:** користувач за `SETUP.md`. Claude допомагає підказками.
**Очікуваний час:** ~20 хв.

### Дії користувача

1. **Реєстрація на GitHub** (`github.com/signup`) — email `lextiks@gmail.com` або інший, ім'я користувача (нік) — на твій вибір (буде твій особистий профіль).
2. **Створення безкоштовної організації** `bridges-net-ua` (`github.com/account/organizations/new`, план «Free»). Власник = твій особистий нік.
3. **Створення публічного репо `shelf`** в межах організації:
   - Owner: `bridges-net-ua`
   - Repository name: `shelf`
   - Visibility: **Public**
   - **Не** додавати README, .gitignore, ліцензію через UI (вони вже є локально).
4. **Локально в PowerShell**:
   ```powershell
   cd D:\project\Shelf
   git init -b main
   git add .
   git commit -m "Initial commit: Shelf v1.0.0"
   git remote add origin https://github.com/bridges-net-ua/shelf.git
   git push -u origin main
   ```
   При першому push GitHub попросить авторизацію — через браузер або PAT.
5. **(Опціонально) Налаштування профілю організації**: завантажити логотип, додати опис і посилання `https://shelf.bridges.net.ua/`.

### Перевірка перед наступним етапом

- [x] `https://github.com/bridges-net-ua/shelf` відкривається і показує файли проекту.
- [x] У вкладці **Actions** видно, що `build.yml` запустився і завершився зеленим ✅ за 59 секунд.
- [x] `README.md` коректно відображається на головній сторінці репо.

### Як насправді сталося

- Користувач створив особистий акаунт **BridgesCom** (замість запропонованого нікнейму) і організацію `bridges-net-ua` (замість `bridges-community`, бо те ім'я не сподобалось).
- Репо випадково створив під особистим акаунтом (`BridgesCom/shelf` замість `bridges-net-ua/shelf`); виправили через **Transfer ownership** у Settings → Danger Zone.
- Перший push у Git Bash з gh CLI: 133 об'єкти, 1.14 MiB, гілка `main`, commit `c1cd086`.

---

## ✅ Етап 5 — Підключення власного домену через DNS + GitHub Pages

**Хто робить:** користувач.
**Очікуваний час:** 10 хв роботи + 10-60 хв очікування DNS/SSL.

### Дії користувача

1. **У DNS-панелі реєстратора `bridges.net.ua`** додати запис:
   - Тип: `CNAME`
   - Ім'я (subdomain): `shelf`
   - Значення (target): `bridges-net-ua.github.io`
   - TTL: `3600` або «за замовчуванням»
2. **Увімкнути GitHub Pages**:
   - У репо: **Settings → Pages**
   - Source: **Deploy from a branch**
   - Branch: `main`, folder `/docs`
   - Натиснути **Save**
3. **Перевірити Custom domain**: GitHub повинен автоматично побачити `CNAME` файл і прописати `shelf.bridges.net.ua` у поле Custom domain. Якщо не прописав — вписати вручну.
4. **Увімкнути Enforce HTTPS** (галочка з'явиться через 10-30 хв після видачі сертифіката).

### Перевірка перед наступним етапом

- [x] `https://shelf.bridges.net.ua/` відкривається, показує сайт-візитівку.
- [x] У браузері замок (HTTPS, валідний Let's Encrypt сертифікат).
- [x] Версія `/en/` теж відкривається.
- [x] DNS поширився за лічені хвилини (значно швидше прогнозованих 10-30 хв).

### Як насправді сталося

- Користувач має активний хостинг у Hostiq, тому DNS-записи додавалися через **cPanel Zone Editor** (а не панель реєстратора).
- CNAME-запис: `shelf` → `bridges-net-ua.github.io.` (з крапкою в кінці, як вимагає cPanel).
- GitHub автоматично прочитав `docs/CNAME`, підставив домен у Settings → Pages.
- Let's Encrypt видав сертифікат через ~2 хв; чекбокс **Enforce HTTPS** увімкнено.

---

## ✅ Етап 6 — Перший реліз v1.0.0 + hotfix v1.0.1

**Хто робить:** користувач створює tag, далі GitHub Actions автоматично.
**Очікуваний час:** 2 хв роботи + 3-5 хв збірки.

### Дії користувача

1. Перевірити, що у `Shelf.csproj` стоїть `<Version>1.0.0</Version>` (якщо немає — додати в розділі `<PropertyGroup>`).
2. У репо: **Releases → Draft a new release**.
3. **Choose a tag** → ввести `v1.0.0` → **Create new tag: v1.0.0 on publish**.
4. Title: `Shelf v1.0.0 — перший публічний реліз`.
5. Description: скопіювати з `CHANGELOG.md` запис для `[1.0.0]`.
6. **Publish release**.
7. Перейти у вкладку **Actions** — побачити, що workflow `release.yml` запустився. Через 3-5 хв він прикріпить `Shelf-v1.0.0-win-x64.zip` до релізу.

### Перевірка перед завершенням

- [x] На сторінці Releases висить `v1.0.1` з прикріпленим `Shelf-v1.0.1-win-x64.zip` (~64 МБ).
- [x] Файл завантажується, розпаковується, `Shelf.exe` запускається.
- [x] Кнопка «Завантажити» на сайті веде сюди і працює.
- [x] Віджети завантажуються — реєстр `WidgetRegistry` коректно знаходить 9 типів.
- [x] Міграція налаштувань з `%APPDATA%\Polychka\` → `%APPDATA%\Shelf\` спрацювала.
- [ ] (Опційно) Створити issue «Welcome / Зворотний зв'язок» у репо.

### Як насправді сталося — v1.0.0 → v1.0.1

- **v1.0.0** опубліковано через GitHub Web UI, workflow зібрав zip за 1 хв 37 сек, 64.1 МБ.
- **Виявлений баг**: при запуску `Shelf.exe` меню «+ Додати віджет» було порожнє. Причина — `-p:PublishSingleFile=true` запихав widget DLL всередину `.exe`, а `WidgetRegistry.Initialize()` шукає `Shelf.Widgets.*.dll` як окремі файли на диску.
- **Hotfix v1.0.1**: прибрав single-file з `release.yml`. Тепер zip — звичайна self-contained тека з `Shelf.exe` + ~200 DLL поруч. Розмір майже не змінився. Реліз v1.0.1 успішно зібраний і перевірений.

---

## Майбутні етапи (опційно, за бажанням)

Усі 6 базових етапів закрито — проект повноцінно живе. Нижче — список побажань на майбутнє, згрупований за зусиллями і пріоритетом.

### 🔮 Невеликі поліпшення сайту (тривіально, ~10-30 хв кожне)

- [ ] **Реальні скріншоти замість SVG-заглушок.** Запусти Поличку, зроби 3 скріншоти: (1) панель з кількома різнотипними віджетами; (2) вікно Налаштувань; (3) приклад зі слайд-шоу+календарем свят. Збережи як `docs/assets/screenshot-1/2/3.png` (та сама пропорція ~16:9). В обох HTML заміни `.svg` на `.png` у тегах `<img>`.
- [ ] **PNG-версія `og-image.svg` для соцмережевих превʼю.** Facebook/Twitter/LinkedIn не рендерять SVG у Open Graph. Відкрий `docs/assets/og-image.svg` у браузері (Edge/Chrome), зроби скріншот 1200×630, збережи як `docs/assets/og-image.png`. У обох HTML заміни `og:image` content на `.png`-URL. Перевір через [opengraph.xyz](https://opengraph.xyz).
- [ ] **Реальний логотип у README.** Зараз там посилання на `docs/assets/logo.png`, який бачать лише ті, хто переглядає README на GitHub. Можна додати inline-альтернативу або base64-варіант, щоб логотип був видний навіть у клоні без `docs/`.

### 📢 Поділитися проектом (~10 хв)

- [ ] **GitHub topics.** Settings репо → ⚙ біля «About» → у поле Topics додай: `widgets`, `windows`, `dotnet`, `wpf`, `dock-bar`, `desktop`, `ukrainian`, `open-source`. Це покращить пошук на GitHub.
- [ ] **Профіль організації `bridges-net-ua`.** Завантаж логотип, додай опис «Open-source software by Bridges Community», лінк на `https://shelf.bridges.net.ua/`.
- [ ] **Pin репо** у профілі організації як основний проект.
- [ ] **Увімкнути GitHub Discussions.** Settings репо → секція «Features» → галочка «Discussions». Дасть людям місце для запитів і фідбеку, що не є багами.
- [ ] **Перший Discussion-пост** «👋 Welcome — фідбек, питання, ідеї» — щоб ентрі-поінт був.
- [ ] **Анонс у соцмережах** — пост у X/Mastodon/Threads з посиланням на `shelf.bridges.net.ua` і скріншотом. Українська tech-спільнота.

### 🚀 Серйозніші майбутні кроки (підетапні плани)

Реліз стабільний на v1.1.1, але є три великі напрямки для розширення. Кожен — окремий етап. Виконуються незалежно, але рекомендований порядок: **7** (Store + автопідпис + auto-update в одному пакеті), потім **9** (auto-update для portable, якщо знадобиться), потім **8** (SignPath для portable, коли репо набере reputation), паралельно — **10** (тести).

> **Розвідка проведена 2026-05-28** (SignPath Foundation + Microsoft Partner Center). Microsoft скасував комісію за реєстрацію Partner Center: раніше $19 individual / $99 company → тепер **$0** (новина 7 травня 2026). Це різко змінило баланс на користь Етапу 7 — він тепер закриває code signing і auto-update для Store-користувачів безкоштовно.

---

#### Етап 7 (🟡 у сертифікації — submission подано 2026-06-01) — Публікація в Microsoft Store через MSIX

**Чому варто:** Microsoft автоматично підписує MSIX-пакети своїм CA при публікації — **повністю знімає попередження SmartScreen і розблоковує Smart App Control на Win11** (та сама проблема з [CLAUDE.md](CLAUDE.md), що блокує дебаг-збірки на чистій Win11 24H2+). Бонусом — безкоштовний auto-update через Store кожні 8 годин (закриває Етап 9 для Store-користувачів), офіційний канал розповсюдження, дозволено паралельно з portable zip на GitHub Releases.

**Витрати:** $0 (Partner Center реєстрація безкоштовна з травня 2026).
**Зусилля:** ~1-2 дні розробки + ~3 робочі дні Microsoft certification.

##### Фактичні дані (Partner Center, акаунт `bridges@bridges.net.ua`)

| Параметр | Значення |
|---|---|
| Зарезервований product name | **ShelfDesk** (+ `Поличка` як additional name для uk-UA) |
| Package Identity Name | `BridgesCommunity.ShelfDesk` |
| Publisher ID | `CN=01B4C228-C24C-45F3-AF31-805FFA0F72FF` |
| Package Family Name (PFN) | `BridgesCommunity.ShelfDesk_a09jnsmnpx15r` |
| Store ID | `9NFC2DKPQDLJ` |
| Store URL | `https://apps.microsoft.com/detail/9NFC2DKPQDLJ` |
| MSA App Id (WNS, поки не використовуємо) | `33599eba-4409-448e-9931-9e65df7ad2bd` |
| Account type | **Company** (Entra ID tenant `bridges.net.ua` — Microsoft визнав без бізнес-документів) |
| Submission 1 | подано на certification **2026-06-01**, auto-publish після проходження |

##### Червоні прапори (вирішити ПЕРЕД першим submit)

1. **`VirtualDesktopPinService` використовує undocumented COM** (`IVirtualDesktopPinnedApps` через ImmersiveShell). Microsoft Store Policy 10.2.2 явно забороняє undocumented APIs — гарантований reject у Technical Compliance.
   - **Рішення:** додати compile-time symbol `STORE_BUILD` і вирізати PinService повністю для Store-збірки (`#if !STORE_BUILD`). Store-версія падає на існуючий fallback `VirtualDesktopService` (polling-mover, нічого приватного не викликає, повністю функціональний).
2. **`AutoStartService` пише в `HKCU\...\Run\Shelf`** — у MSIX цей розділ реєстру редиректиться через registry virtualization, запис ігнорується Windows. Функція автозапуску в Store-збірці тихо не працює.
   - **Рішення:** замінити на `windows.startupTask` extension у `Package.appxmanifest` + API `Windows.ApplicationModel.StartupTask` для програмного enable/disable. HKCU-шлях лишити через `#if !STORE_BUILD` для portable.
3. **Міграція `settings.json`** — у Store-збірці шлях редиректиться під `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Roaming\Shelf\`. Користувач, який переходить з portable на Store-версію, не побачить старі налаштування.
   - **Рішення:** one-time copy-in при першому запуску Store-збірки — якщо в "реальному" `%APPDATA%\Shelf\settings.json` щось є, скопіювати в редиректнутий шлях і поставити маркер `migrated.flag`.

##### Підетап 7.1 — Підготовка коду

- Додати у `Shelf.csproj` `<Configurations>Debug;Release;Store</Configurations>` і `<DefineConstants Condition="'$(Configuration)' == 'Store'">$(DefineConstants);STORE_BUILD</DefineConstants>` для Store-конфігурації.
- `Services/VirtualDesktopPinService.cs` — обгорнути весь файл у `#if !STORE_BUILD`. Перевірити, що `MainWindow.OnSourceInitialized` коректно компілюється і працює без PinService (mover з polling).
- `Services/AutoStartService.cs` — додати окремий бранч під `#if STORE_BUILD` через `Windows.ApplicationModel.StartupTask.GetAsync` + `RequestEnableAsync()` / `Disable()`. Для Debug/Release лишити поточний HKCU-шлях. Knigger: треба підтягнути winmd-reference на `Windows.SDK.NET`.
- `Services/SettingsService.cs` — додати метод `MigratePortableToPackaged()`, який викликається лише в Store-збірці перед `Load()`. Перевіряє маркер `migrated.flag` у редиректнутому шляху; якщо нема — пробує скопіювати з "реального" `%APPDATA%\Shelf\settings.json` (через `KnownFolders.RoamingAppData` + non-virtualized path).
- Зібрати в трьох конфігураціях: `dotnet build Shelf.sln -c Debug`, `-c Release`, `-c Store`. Усі три — 0 помилок.

**Перевірка перед наступним підетапом:**
- [x] Усі три конфігурації компілюються без помилок (commit `1f2e082`).
- [x] У Store-збірці `VirtualDesktopPinService` під `#if !STORE_BUILD` — не компілюється у Store.
- [x] AutoStart: HKCU гілка (Debug/Release) + `Windows.ApplicationModel.StartupTask` гілка (Store).

> ✅ **Виконано 2026-06-01.** Замість окремого winmd-reference використано `TargetFramework=net8.0-windows10.0.19041.0` — WinRT-проєкція активується автоматично, StartupTask API доступне без NuGet. Settings-міграція — `SHGetKnownFolderPath` + `KF_FLAG_NO_PACKAGE_REDIRECTION`, маркер `migrated.flag`.

##### Підетап 7.2 — Privacy Policy

- Створити `docs/privacy/index.html` (українською) і `docs/privacy/en/index.html` (англійською). Зміст: які дані Shelf шле в мережу — Open-Meteo (геокодинг + поточна погода за координатами міста), стрімінги радіо-станцій (HTTP-запити до сторонніх URL з користувацького списку). Жодних даних на сервери Bridges Community не йде. Не збираємо телеметрію.
- Контактний email — `shelf@bridges.net.ua`.
- Додати лінк на Privacy Policy в About-вкладку (`Views/SettingsWindow.xaml.cs` → `SetupAbout`): Hyperlink поряд із наявним email-лінком.
- Підняти живу сторінку на `https://shelf.bridges.net.ua/privacy/` (GitHub Pages підхопить автоматично після push у `main`).

**Перевірка:**
- [x] `docs/privacy/index.html` (uk) + `docs/privacy/en/index.html` (en) створені, на GitHub Pages.
- [x] About-вкладка: Hyperlink `About_PrivacyPolicy` → `https://shelf.bridges.net.ua/privacy/`.

> ✅ **Виконано 2026-06-01** (commit `1f2e082`).

##### Підетап 7.3 — Створити Package-проект

- Додати в `Shelf.sln` (зберегти UTF-8 BOM!) новий проект `Shelf.Package` типу **Windows Application Packaging Project** (.wapproj).
- У `Shelf.Package` додати `<ProjectReference Include="..\Shelf.csproj" />`.
- Створити `Shelf.Package/Package.appxmanifest`:
  - `<Identity Name="..." Publisher="CN=..." Version="1.1.1.0" />` — значення Name і Publisher отримуються з Partner Center після резервування імені (підетап 7.4).
  - `<Properties>` — DisplayName з ресурсів ("Поличка" uk / "ShelfDesk" en), `PublisherDisplayName="Bridges Community"`.
  - `<Dependencies>` — `MinVersion="10.0.19041.0"` (Windows 10 2004+), `MaxVersionTested` — поточна Win11.
  - `<Capabilities>` — `<rescap:Capability Name="runFullTrust" />` (обов'язково для WPF+WinForms hybrid).
  - `<Extensions>` — `<desktop:Extension Category="windows.startupTask" Executable="Shelf.exe" EntryPoint="Windows.FullTrustApplication">` з `<desktop:StartupTask TaskId="ShelfAutoStart" Enabled="false" DisplayName="Поличка" />`.
  - `<Application>` — `Executable="Shelf.exe"`, `EntryPoint="Windows.FullTrustApplication"`.
- Підготувати assets з `Resources/shelf.png` у потрібні розміри: `Square44x44Logo`, `Square150x150Logo`, `Wide310x150Logo`, `StoreLogo` (50×50), `SplashScreen` (620×300). Зберегти в `Shelf.Package/Images/`.
- Зібрати локально: `msbuild Shelf.Package\Shelf.Package.wapproj /p:Configuration=Store /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true`. Результат — `.msix` у `Shelf.Package\AppPackages\`.

**Перевірка:**
- [x] `.msix` збирається без помилок (`tools/make-msix.ps1`, 75.6 MB).
- [x] Підписаний self-signed cert (`make-msix.ps1 -Sign`), встановлюється через `Add-AppxPackage`.
- [x] Після встановлення Shelf запускається з меню Start як «Поличка», працює AppBar, трей, Settings.
- [x] StartupTask видно у Settings → Apps → Startup (джерело — Microsoft Store, не реєстр).

> ✅ **Виконано 2026-06-01, але інакше ніж планувалось.** Visual Studio на машині немає, тому **без `.wapproj`** — пакет збирається standalone-скриптом `tools/make-msix.ps1` (через `makeappx`/`signtool` з Windows SDK) + `tools/make-store-assets.ps1` для 5 PNG-ассетів у `Shelf.Package/Assets/`. `Shelf.sln` НЕ містить Package-проєкту. Manifest — `Shelf.Package/Package.appxmanifest` (commit `e52e621`).

##### Підетап 7.4 — Partner Center реєстрація

- Зайти на `partner.microsoft.com/dashboard`, увійти Microsoft-акаунтом, обрати "Microsoft Store program" → "Get started".
- Account type: **Individual** (безкоштовно, без бізнес-документів). Реєстрація через Entra ID + government-issued ID + selfie з мобільного.
- Publisher display name: **"Bridges Community"** (бренд; технічно individual account, але name відображається як Bridges Community у Store).
- Зарезервувати три імені застосунку: `Shelf`, `Поличка`, `ShelfDesk` (одна Identity з трьома aliased назвами для різних локалізацій).
- Записати в безпечне місце:
  - **Publisher Identity** (формат `CN=...`)
  - **Package Identity Name** (формат `BridgesCommunity.Shelf` або подібне — Partner Center видасть)
  - **Seller ID** (знадобиться для CI: `msstore-cli reconfigure`)
- Підставити отримані Publisher і Identity в `Package.appxmanifest` (з підетапу 7.3), перезібрати локально.

**Перевірка:**
- [x] Акаунт активований (Company через Entra ID tenant `bridges.net.ua`, $0).
- [x] `ShelfDesk` зарезервовано (`Shelf` було зайнято) + `Поличка` як additional name.
- [x] Реальні Publisher/Identity підставлені в manifest (commit `f144328`), MSIX перезібраний.

> ✅ **Виконано 2026-06-01.** Реальні дані — у таблиці «Фактичні дані» вище. Account type вийшов **Company** (не Individual) — Microsoft визнав tenant `bridges.net.ua` без вимоги паперових документів. DisplayName у manifest → `ShelfDesk` (узгоджено з product name); «Поличка» локалізується через uk-UA Store listing.

> ⚠️ **Account type незмінний:** змінити Individual → Company пізніше неможливо, потрібен новий акаунт. Якщо є хоч мінімальна перспектива зареєструвати "Bridges Community" як юр. особу (ФОП тощо) — варто це зробити ДО створення Partner Center акаунту і реєструватися як Company.

##### Підетап 7.5 — WACK + перший submission

- Завантажити **Windows App Certification Kit** (WACK) з Windows SDK.
- Запустити `Cert.exe` (графічний WACK) → обрати `.msix` → профіль "Store Validation" → Run.
- Звіт — XML + HTML. Виправити всі **fail**. Типові warning'и (про non-Store-API-usage у P/Invoke секціях) — допустимі для Desktop Bridge додатка, але ретельно прочитати кожен.
- У Partner Center: створити новий submission для зарезервованого `Shelf`.
  - **Properties**: Category = "Productivity > Personalization", Age rating = заповнити IARC анкету (Shelf → "3+ / Everyone").
  - **Pricing and availability**: Free, доступний у всіх ринках (або обмежити країнами куди можемо легально продавати free apps).
  - **Properties → Privacy policy URL**: `https://shelf.bridges.net.ua/privacy/`.
  - **Properties → Support contact info**: `shelf@bridges.net.ua`.
  - **Packages**: завантажити `.msix` файл (Microsoft re-підпише при публікації).
  - **Store listings** (uk-UA, en-US): DisplayName, short description, full description, скріншоти (мінімум 1, рекомендовано 3-5 розміром 1366×768 або більше), іконка 300×300.
- Submit. Чекати certification ~3 робочі дні. Якщо reject — прочитати звіт у Notifications, виправити, re-submit.

**Перевірка:**
- [x] Production unsigned MSIX зібраний (`make-msix.ps1`, Microsoft re-підпише при публікації).
- [x] Submission 1 поданий — усі 6 секцій Complete, статус **In certification** (2026-06-01).
- [ ] Certification passed → статус "In the Store" (**чекаємо email ~до 3 робочих днів**).
- [ ] Shelf шукається через Store на чистій Win11.
- [ ] Встановлення через Store → AppBar, автозапуск, всі віджети.

> 🟡 **Подано 2026-06-01, чекаємо вердикт.** WACK локально **не запускали** (опційно — пропустили). Store listings заповнені обома мовами з `Shelf.Package/StoreListings/{en-US,uk-UA}.md`, 5 screenshots з `ScreenShots/`. runFullTrust обґрунтовано в Submission Options. Вибрано «Publish as soon as it passes certification».

##### Підетап 7.6 — CI publish (опційно, можна відкласти на наступний реліз)

- Налаштувати GitHub Action для автоматичного push нової версії в Store при тегу `v*`.
- Інструменти: `msstore-cli` (Microsoft Store Developer CLI) + офіційний action `microsoft/microsoft-store-app-publisher@v1.1`.
- Створити Azure AD app registration → отримати tenantId / clientId / clientSecret для Partner Center API. Покласти у GitHub Secrets як `MS_STORE_TENANT_ID`, `MS_STORE_CLIENT_ID`, `MS_STORE_CLIENT_SECRET`, `MS_STORE_SELLER_ID`.
- Розширити `.github/workflows/release.yml` додатковим job `publish-store`, що залежить від нового `build-store` job: будує `.msix` через `msbuild`, потім `msstore publish` через action.

**Перевірка:**
- [ ] Локальний прогон `msstore reconfigure` + `msstore publish` працює.
- [ ] GitHub Action на тестовому тегу `v1.1.2-test` виконується успішно.
- [ ] Нова версія з'являється в Store автоматично через ~3 дні після push тега.

##### Підетап 7.7 — Перший Store-реліз

- Bumpнути `<Version>` у `Shelf.csproj` і `<Identity Version>` у manifest узгоджено: наприклад **`v1.2.0`** (minor-bump, бо новий канал розповсюдження).
- Оновити `CHANGELOG.md` — `[1.2.0]` з пунктом "Публікація в Microsoft Store" і списком red-flag фіксів.
- Створити тег → GitHub Actions збирає portable zip і паралельно публікує MSIX у Store через job з 7.6.
- На сайті `shelf.bridges.net.ua` додати кнопку "Завантажити з Microsoft Store" (badge `https://developer.microsoft.com/store/badges/...`) поряд із наявною кнопкою GitHub Releases.

**Перевірка:**
- [ ] У Store доступна нова версія (через "Library" → "Get updates").
- [ ] Bump попередньої версії → Store auto-update протягом 8-24 год без участі користувача.
- [ ] Кнопка з сайту веде в правильний Store-listing (deep link).
- [ ] Portable zip на GitHub Releases теж випущений (паралельно).

---

#### Етап 8 (відкладено до ~2026-11-28) — Підготовка до SignPath Foundation

**Чому відкладено:** SignPath Foundation Program безкоштовно підписує OSS .exe (OV-сертифікат від Sectigo / GlobalSign / SSL.com через спільний publisher "SignPath Foundation"), але приймає **лише проекти з певною reputation**: 0 stars + 1 місяць існування + один автор — гарантований refusal. Microsoft Store (Етап 7) тимчасово закриває проблему підпису для Store-юзерів. Portable zip з GitHub Releases залишається непідписаним — SmartScreen вимагає "Run anyway", Smart App Control блокує жорстко.

**Що дасть прийняття:** SignPath OV-підпис на `Shelf-vX.Y.Z-win-x64.zip` (вміст: `Shelf.exe`, `Shelf.Sdk.dll`, `Shelf.Widgets.*.dll` — native runtime DLL з self-contained .NET 8 вже підписані Microsoft, не торкати). **Не дає миттєвого Smart App Control bypass** (з 2024 Microsoft зрівняв OV і EV щодо репутації — обом потрібно accumulate downloads). Але SignPath publisher вже накопичив репутацію з підпису десятків відомих OSS-проектів (vim, transmission, тощо) — ми наслідуємо її, стартова репутація вища за нуль.

**Витрати:** $0 (якщо приймуть).
**Зусилля:** ~1 день на форму + 2-4 тижні очікування review + накопичення reputation за 6 місяців.

##### Що зробити ДО подачі (за наступні 6 місяців: травень-листопад 2026)

- [ ] **Накопичити GitHub reputation** — мінімум **50-100 stars** (SignPath не публікує жорстких цифр, але "verifiable reputation" — обов'язкове). Способи:
  - Анонс на Reddit (r/Windows10, r/Ukraina, r/opensource), Hacker News (Show HN), X/Mastodon
  - GitHub Topics (вже в плані: `widgets`, `windows`, `dotnet`, `wpf`, `dock-bar`)
  - Сторінка проекту в українських tech-ресурсах (DOU, Habr)
  - Submission в `awesome-windows-apps` репозиторій
- [ ] **Code Signing Policy сторінка** — обов'язково для SignPath. Створити `docs/code-signing/index.html` з фіксованим текстом: "Free code signing provided by SignPath.io, certificate by SignPath Foundation". Опублікувати на `https://shelf.bridges.net.ua/code-signing/`.
- [ ] **Privacy Policy** — вже зроблено в Етапі 7.2.
- [ ] **OpenHub профіль проекту** — створити на `openhub.net`, синхронізувати з GitHub. Один із сигналів legitimacy для SignPath reviewers.
- [ ] **MFA на всіх членах команди** — і SignPath-акаунті (коли буде), і GitHub. Зараз — увімкнути на основному акаунті.
- [ ] **External contributors** — хоча б 1-2 merged PR від не-засновника (переклад, документація, мінорний bugfix). Підготувати "good first issue" labeled, привабити через Reddit.
- [ ] **Зрілість релізів** — мінімум 4-5 версій з активним maintenance, без покинутості. На 2026-11-28 буде ~6-8 версій.

##### Власне подача (приблизно листопад 2026)

- Завантажити OSS Request Form v4 (.xlsx) з `signpath.org/foundation`. Заповнити 9 розділів: project metadata, ліцензія, reputation evidence (stars, downloads, contributors, media), team (3 ролі — Author / Reviewer / Approver), policies.
- Відправити на `info@signpath.io`. Очікувати: acknowledgment 1-3 дні, initial review 1-2 тижні, follow-up rounds 1-2 тижні, рішення через 2-4 тижні.
- Після прийняття — налаштувати GitHub Action `signpath/github-action-submit-signing-request@v2`: завантажує zip → SignPath підписує deep (через Artifact Configuration XML з wildcards `Shelf.exe`, `Shelf.Sdk.dll`, `Shelf.Widgets.*.dll`) → повертає підписаний артефакт. Зберегти секрети `SIGNPATH_API_TOKEN`, `SIGNPATH_ORG_ID`.
- **Manual approval кожного релізу** через SignPath UI (це частина процесу, обійти не можна).

**Перевірка перед подачею (~2026-11-28):**
- [ ] ≥50 GitHub stars.
- [ ] `https://shelf.bridges.net.ua/code-signing/` і `/privacy/` обидві живі.
- [ ] OpenHub профіль існує і не deprecated.
- [ ] MFA увімкнено.
- [ ] Хоча б один external PR з'явився.

> 💡 Якщо стане очевидно ще до листопада, що reputation не набирається — варіант B: купити Sectigo OV (~$220/рік) для portable, або взагалі лишити portable непідписаним і покладатися на Store як основний канал.

---

#### Етап 9 (нижчий пріоритет) — Auto-update для portable-збірки

**Чому нижчий пріоритет:** Microsoft Store (Етап 7) дає auto-update автоматично кожні 8 годин для Store-користувачів — а це після виходу 7.7 буде основним каналом для пересічного користувача. Portable zip з GitHub Releases — користувачі технічні, самі стежать. Тому Етап 9 — поліпшення UX, не критична функція.

**Витрати:** $0.

##### Варіант A — проста кнопка «Перевірити оновлення» (рекомендую, ~2-3 год)

- Додати в About-вкладку `SettingsWindow` кнопку "Перевірити оновлення".
- Виклик: `HttpClient.GetAsync("https://api.github.com/repos/bridges-net-ua/shelf/releases/latest")` → парсити `tag_name` → порівняти з `Assembly.GetExecutingAssembly().GetName().Version`.
- Якщо є новіша → показати `DarkMessageBox` з посиланням "Завантажити" → `Process.Start(new ProcessStartInfo("https://github.com/bridges-net-ua/shelf/releases/latest") { UseShellExecute = true })`.
- Якщо актуальна → "У вас остання версія".
- Опційно: автоматична перевірка раз на тиждень при запуску, з тихим badge у About якщо є оновлення (без notification).
- Локалізувати рядки в `Strings.uk.xaml` / `Strings.en.xaml`.

**Перевірка:**
- [ ] Кнопка відображається в About.
- [ ] Тест на старішій версії → відкриває браузер на Releases.
- [ ] Тест на актуальній → показує "У вас остання версія".
- [ ] Network-failure (offline) → graceful error message, без crash.

##### Варіант B — Velopack (повноцінне фонове оновлення, ~1 день)

- Інтегрувати [Velopack](https://github.com/velopack/velopack) — MIT, активно розробляється, з коробки тягне новий exe з GitHub Releases і застосовує оновлення без участі користувача.
- Замінити portable zip-формат на Velopack `.exe` installer + delta-updates.
- Перевага: користувач завжди на актуальній версії, як у Store.
- Недолік: ще одна залежність (~5 МБ), складніша збірка, треба окремі тести.

##### Рекомендація — почати з варіанту A. Якщо Store-канал виявиться домінантним (>80% завантажень) — Варіант B не варто. Якщо багато portable-юзерів просять auto-update — мігрувати на B.

---

#### Етап 10 — CI test pipeline

- **Зусилля:** ~2-3 дні.
- **Витрати:** $0 (хмарні runners безкоштовні для public репо).
- **Що зробити:**
  - Юніт-тести через `xUnit` для `SettingsService`, `Loc`, `WidgetRegistry`, міграційного коду.
  - UI-тести через [WinAppDriver](https://github.com/microsoft/WinAppDriver) або [FlaUI](https://github.com/FlaUI/FlaUI): запустити Shelf, додати віджет, перезавантажити, переконатися, що state збережений.
  - Інтеграційний smoke test у `release.yml` після `dotnet publish`: розпакувати zip, запустити `Shelf.exe`, дочекатися появи вікна, зробити screenshot, прикріпити як artifact.

### 🛠 Технічний борг і поліпшення коду (не блокують, але корисно)

- [ ] **`logodesk.png` у корені** — невідомий файл; з'ясувати призначення і або задокументувати, або видалити.
- [ ] **Експорт-файли `polychka-*-2026-05-26.{json,txt}`** у корені — старі експорти з тестування v1.0.0. Видалити локально (вони вже в `.gitignore`, у репо не потрапили).
- [ ] **Легасі-теки `WidgetPlugins/Помічник.Widgets.*/`** — фізично залишаються на диску, в репо ігноруються. Якщо точно не знадобляться — видалити.
- [ ] **WFAC010 warning** про DPI manifest — false-positive (`app.manifest` правильно описує PerMonitorV2 для гібридного WPF+WinForms). Можна або задокументувати ще явніше, або переключитись на `ApplicationHighDpiMode`, щоб warning зник.
- [ ] **`tools/make-ico.ps1`** — скрипт для генерації `.ico`. Перевірити, чи актуальний, додати в `CONTRIBUTING.md` як «як оновити іконку».
- [x] **Перейменування фізичної теки `D:\project\Polychka` → `D:\project\Shelf`** на машині розробника заплановано як остання дія сесії; skill-файли і доки вже path-agnostic / використовують новий шлях.
- [ ] **Видалити з історії плану старі застарілі чек-листи** і консолідувати в один компактний документ — коли проект стане публічним і люди читатимуть `plan_reliz.md`.

---

## Журнал змін плану

- **2026-05-26 v1** — початкова версія плану.
- **2026-05-26 v2** — додано `shelf.bridges.net.ua` як власний піддомен сайту.
- **2026-05-26 v3** — план розбито на 6 послідовних етапів з чек-листами перевірки перед переходом до наступного.
- **2026-05-26 v4** — виконано Етапи 1-3 (локальні файли проекту, GitHub Actions, сайт-візитівка). Готовий до переходу на Етап 4 (реєстрація GitHub + push).
- **2026-05-26 v5** — користувач створив org `bridges-net-ua` (замість `bridges-community`) і обрав репо `shelf` (замість `polychka`). Усі URL оновлено: 75 згадок `bridges-community` → `bridges-net-ua`, потім 60 згадок `bridges-net-ua/polychka` → `bridges-net-ua/shelf` у 12 файлах.
- **2026-05-26 v6** — повний рефакторинг технічної назви проекту: `Polychka` → `Shelf` (`polychka` → `shelf` у lowercase). Зачеплено: 10 тек перейменовано, 11 csproj/sln/resource-файлів перейменовано, 93 текстові файли оновлено через sed. «Поличка» (кирилиця) збережена як `App_Name` у `Strings.uk.xaml`. «ShelfDesk» збережено в `Strings.en.xaml`. Додано міграційний код у `SettingsService.cs` (`%APPDATA%\Polychka\` → `%APPDATA%\Shelf\`) і `AutoStartService.cs` (HKCU\\…\\Run\\Polychka → Shelf). Збірка `dotnet build Shelf.sln -c Debug` пройшла з 0 помилок.
- **2026-05-27 v7** — виконано Етапи 4-6. Створено організацію `bridges-net-ua` на GitHub, репо `shelf`. Перший push (133 об'єкти, 1.14 MiB). DNS CNAME `shelf → bridges-net-ua.github.io` через cPanel Hostiq, GitHub Pages деплоїть `/docs`, SSL від Let's Encrypt автоматично виданий. Сайт живий на `https://shelf.bridges.net.ua/`. Випущено **v1.0.0** (тег → workflow → `Shelf-v1.0.0-win-x64.zip`, 64.1 МБ).
- **2026-05-27 v8** — hotfix **v1.0.1**. У v1.0.0 виявлено критичний баг: `-p:PublishSingleFile=true` запаковував усі `Shelf.Widgets.*.dll` всередину `Shelf.exe`, а `WidgetRegistry.Initialize()` шукає їх через `Directory.EnumerateFiles` як окремі файли — реєстр був порожній, меню «+ Додати віджет» не показувало жодного типу. Виправлено: прибрано `PublishSingleFile` з `release.yml`, тепер zip містить теку з `Shelf.exe` + ~200 DLL поруч. v1.0.1 опубліковано, перевірено на чистій теці — віджети завантажуються, додаються, працюють.
- **2026-05-27 v9** — фінальне оновлення плану після завершення всіх етапів. Проставлено чекбокси, оновлено таблицю прийнятих рішень (single-file → folder), переписано розділ статусу як «фінал», структуровано розділ «Майбутні етапи» з конкретними побажаннями: малі поліпшення сайту, action items для просування, серйозніші майбутні етапи 7-10 (MSIX, code signing, auto-update, CI tests), технічний борг.
- **2026-05-27 v10** — додано два project-local skill в `.claude/skills/`: `shelf-update` (тригер «ВНЕСТИ ЗМІНИ: ...» → редагує код, збирає, не комітить) і `shelf-commit` (тригер «ЗРОБИ КОМІТ» → формулює conventional commit message, пушить). Skills path-agnostic — перевіряють наявність `Shelf.sln` у cwd замість літерального шляху, тому переживуть перейменування теки `D:\project\Polychka` → `D:\project\Shelf`. Skill `shelf-update` додатково має auto-cleanup stale bin/obj, якщо виявить старі шляхи в кеші.
- **2026-06-01 v12** — **виконано Етап 7.1-7.5 за один день, submission подано на certification.** Код: додано конфігурацію `Store` зі `STARTUP_BUILD`-розгалуженням (PinService вирізано, AutoStartService на StartupTask API, settings-міграція через `SHGetKnownFolderPath`), `TargetFramework` піднято до `net8.0-windows10.0.19041.0`; 3 конфігурації білдяться чисто (commit `1f2e082`). Privacy Policy сторінки uk+en на GitHub Pages. **Збірка MSIX без Visual Studio** — Windows SDK 10.0.22621 + standalone `tools/make-msix.ps1` (makeappx/signtool) + `tools/make-store-assets.ps1` (5 PNG); `Shelf.Package/Package.appxmanifest` без `.wapproj` (commits `e52e621`, `041db83`). Partner Center: акаунт `bridges@bridges.net.ua` (вийшов **Company** через Entra ID tenant, $0), зарезервовано **ShelfDesk** (+ «Поличка» additional name), отримано Publisher `CN=01B4C228-...`, Identity `BridgesCommunity.ShelfDesk`, Store ID `9NFC2DKPQDLJ`; manifest оновлено реальними даними (commit `f144328`). Store listings обома мовами + 5 screenshots + IARC рейтинг (3+/Everyone скрізь) + runFullTrust обґрунтування. **Submission 1 → In certification.** Залишилось: дочекатись pass (Етап 7.5 фініш), потім 7.6 CI publish + 7.7 кнопка Store на сайті. Дані збережено в project-memory `store-submission-data.md`.
- **2026-05-28 v11** — після ґрунтовної розвідки SignPath Foundation і Microsoft Partner Center переосмислено розділ «Майбутні етапи». Microsoft скасував комісію за реєстрацію Partner Center (раніше $19 individual / $99 company → **$0 з травня 2026**), що різко змінило баланс на користь публікації в Store. **Етап 7** повністю переписано як 7 послідовних підетапів (7.1 підготовка коду з conditional compile, 7.2 Privacy Policy, 7.3 .wapproj+manifest, 7.4 Partner Center реєстрація, 7.5 WACK+submit, 7.6 CI publish, 7.7 перший Store-реліз) з чек-листами «Перевірка перед наступним підетапом» і виділеними червоними прапорами (undocumented `IVirtualDesktopPinnedApps`, HKCU autostart, settings migration). **Етап 8** (SignPath Foundation) переформульовано як «відкладено до ~2026-11-28» з конкретним чек-листом підготовки за 6 місяців (накопичити stars, написати Code Signing Policy, OpenHub, MFA, external contributors). **Етап 9** (auto-update) знижено в пріоритеті — Store вирішує проблему для більшості користувачів; для portable додано два варіанти (проста кнопка «Перевірити оновлення» vs Velopack). **Етап 10** без змін. `CHANGELOG.md` `[Unreleased]` синхронізовано. Збережено project-memory про дату повернення до Етапу 8.

## Поточний статус

🟢 **Базові 6 етапів + рефакторинг + hotfix виконано** — проект живий у мережі (v1.1.1 на GitHub Releases).
🟡 **Етап 7 (Microsoft Store) — у сертифікації:** submission 1 подано 2026-06-01, підетапи 7.1-7.5 закрито, чекаємо вердикт Microsoft (~до 3 робочих днів). Далі — 7.6 (CI publish) і 7.7 (кнопка Store на сайті) після проходження. Деталі — у блоці «Фактичні дані» Етапу 7 вище.

Нижче — підсумок початкових 6 етапів (історичний, без Етапу 7).

### Що зробили (за один день)

| # | Дія | Хто | Час |
|---|---|---|---|
| 1 | Створено 10 файлів у корені (LICENSE, README × 2, CHANGELOG, CONTRIBUTING, CODE_OF_CONDUCT, RELEASE, SETUP, plan_reliz, .gitignore) | Claude | 30 хв |
| 2 | Створено 6 файлів у `.github/` (build/release workflows + 4 шаблони) | Claude | 10 хв |
| 3 | Створено 11 файлів у `docs/` (HTML UA+EN, CSS, ассети, CNAME) | Claude | 20 хв |
| – | **Рефакторинг** `Polychka` → `Shelf` (10 тек, 11 csproj/sln, 93 файли через sed, +міграційний код) | Claude | 30 хв |
| 4 | Реєстрація GitHub `BridgesCom`, org `bridges-net-ua`, repo `shelf`, перший push (133 об'єкти, 1.14 МБ) | Користувач | 15 хв |
| 5 | DNS CNAME у cPanel Hostiq, GitHub Pages з `/docs`, SSL Let's Encrypt, Enforce HTTPS | Користувач | 10 хв |
| 6 | Реліз **v1.0.0** через Web UI + **v1.0.1 hotfix** (виправлено single-file баг з widget DLL) | Користувач + Claude | 15 хв |

**Усього:** ~2 год активної роботи Claude + ~40 хв дій користувача + ~30 хв чекання DNS/CI.

### Підсумкові артефакти

- **Локальний проект:** 27 нових файлів + рефакторинг ~93 файлів.
- **GitHub репо:** 4 коміти (`c1cd086` initial → `31be083` version metadata → `a1919de` plan update → `78dade9` hotfix v1.0.1).
- **GitHub Releases:** v1.0.0 (зі застереженням про баг), v1.0.1 (стабільна).
- **GitHub Actions:** `build.yml` (трігер push/PR) і `release.yml` (трігер тег `v*`); обидва зелені.
- **Сайт:** `https://shelf.bridges.net.ua/` + `/en/`, темна тема, HTTPS.
- **Збірка `.exe`:** self-contained folder для win-x64, ~64 МБ zip, працює на чистій Windows-машині.

### Відомі обмеження (не блокують реліз, але є в списку побажань)

- **OG-image — SVG**, не PNG. Соцмережі не генерують прев'ю — виправити одним конвертом SVG → PNG.
- **Скріншоти — SVG-макети**, не реальні скріни додатка. Замінити після першого використання.
- **`v1.0.0` реліз має критичний баг** з віджетами (виправлено в v1.0.1). Бажано додати застереження в опис релізу `v1.0.0` через GitHub Web UI (Edit).
