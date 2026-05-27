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

### 🚀 Серйозніші майбутні кроки (години-дні роботи, можуть коштувати)

#### Етап 7 — MSIX-пакування для Microsoft Store

- **Зусилля:** ~1 день розробки + ~3-7 днів Store certification.
- **Витрати:** $19 одноразово за Microsoft Partner dev-акаунт.
- **Що зробити:**
  - Додати в рішення `Windows Application Packaging Project` (`Shelf.Package`).
  - Створити `Package.appxmanifest` із метаданими (Publisher Identity, Capabilities, Assets).
  - Розширити `.github/workflows/release.yml`: окремий job, який збирає `.msix` через `MSBuild Shelf.Package.wapproj`.
  - Скласти Privacy Policy URL (обов'язково для Store, бо Weather widget шле координати в Open-Meteo).
  - Подати в Microsoft Partner Center, пройти certification.

#### Етап 8 — Code signing (Authenticode)

- **Зусилля:** ~2-3 год налаштування.
- **Витрати:** ~$200/рік (OV) або ~$300-400/рік + hardware token (EV).
- **Чому варто:** прибирає попередження SmartScreen «Windows protected your PC» при першому запуску; EV-сертифікат дає миттєву репутацію (OV — за 2-4 тижні після кількох downloads).
- **Що зробити:**
  - Купити сертифікат у Sectigo/DigiCert/SSL.com.
  - Покласти `.pfx` (base64) у GitHub Secrets як `SIGN_CERT_PFX_BASE64`, пароль як `SIGN_CERT_PASSWORD`.
  - У `release.yml` додати крок з `signtool sign /sha1 ... /fd SHA256 /tr http://timestamp.digicert.com /td SHA256` для кожного `.exe`/`.dll`.
- Деталі вже задокументовано в `RELEASE.md` (секція «Підпис коду»).

#### Етап 9 — Auto-update mechanism

- **Зусилля:** ~1 день розробки.
- **Витрати:** $0.
- **Чому варто:** користувачам не треба вручну заходити в Releases для нової версії; новинки доходять швидше.
- **Варіанти:**
  - **[Velopack](https://github.com/velopack/velopack)** — рекомендовано: сучасний, MIT, активно розвивається, з коробки тягне новий `.exe` з GitHub Releases і застосовує оновлення.
  - **Squirrel.Windows** — старіший, перевірений.
  - **Власне**: HTTP-запит до `api.github.com/repos/bridges-net-ua/shelf/releases/latest`, порівняти tag з поточною версією, при різниці — відкрити браузер на сторінку Releases.

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
- [ ] **Перейменування фізичної теки `D:\project\Polychka` → `D:\project\Shelf`** на машині розробника, для повної консистентності з технічною назвою (не впливає на репо).
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

## Поточний статус

🟢 **ФІНАЛ — усі 6 етапів + рефакторинг + hotfix виконано.** Проект живий і доступний у мережі.

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
