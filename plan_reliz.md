# План публікації Поличка як Open-Source

> Робочий документ. Оновлюємо тут до моменту, коли план буде готовий до виконання.
> Останнє оновлення: 2026-05-26

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
| GitHub-акаунт | Ще не створено, користувач реєструється вручну за інструкцією |
| Мова сайту і README | Двомовний: українська (основна) + англійська |
| Початкова версія | `1.0.0` |
| Легасі-теки (`WidgetPlugins/Помічник.Widgets.*`, `bin/`, `artifacts/`, `*.log`) | Додати в `.gitignore` без видалення з диска |
| Скріншоти | Заглушки, користувач додасть реальні файли пізніше |
| Контактний email | `shelf@bridges.net.ua` (вже в `App_About`) |
| MSIX / Microsoft Store | Пізніше, окремим завданням |
| Тип збірки `.exe` | Self-contained, win-x64, single-file (~70 МБ, не вимагає окремо встановлювати .NET) |
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

- [ ] 9 файлів існують у корені.
- [ ] `README.md` коректно відображається у будь-якому Markdown-переглядачі.
- [ ] `.gitignore` містить правильні патерни (перевірити вручну на 2-3 файлах).

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

- [ ] 6 файлів існують у `.github/`.
- [ ] YAML-файли валідні синтаксично (перевіримо через online YAML linter або просто Read).
- [ ] Логіка release.yml використовує тег як версію у назві артефакту.

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

- [ ] Усі файли існують у `docs/`.
- [ ] Користувач відкрив `docs/index.html` у браузері локально — сайт виглядає як треба, темна тема працює, логотип на місці.
- [ ] Кнопка «Завантажити» веде на `github.com/bridges-net-ua/shelf/releases/latest` (поки 404, нормально — реліз буде на етапі 6).
- [ ] Перемикач мови UA ↔ EN працює локально.

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

- [ ] `https://github.com/bridges-net-ua/shelf` відкривається і показує файли проекту.
- [ ] У вкладці **Actions** видно, що `build.yml` запустився і завершився зеленим ✅.
- [ ] `README.md` коректно відображається на головній сторінці репо.

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

- [ ] `https://shelf.bridges.net.ua/` відкривається, показує сайт-візитівку.
- [ ] У браузері поряд з URL — замок (HTTPS, валідний сертифікат).
- [ ] Версія `/en/` теж відкривається.
- [ ] Перевірити через [whatsmydns.net](https://whatsmydns.net) що CNAME `shelf.bridges.net.ua` пошириться на більшість регіонів.

---

## ⬜ Етап 6 — Перший реліз v1.0.0

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

- [ ] На сторінці Releases висить `v1.0.0` з прикріпленим `Shelf-v1.0.0-win-x64.zip` (~70 МБ).
- [ ] Файл завантажується, розпаковується, `Shelf.exe` запускається на чистій Windows-машині.
- [ ] Кнопка «Завантажити» на сайті веде сюди і працює.
- [ ] (Опційно) Створити issue «Welcome / Зворотний зв'язок» у репо.

---

## Майбутні етапи (поза цим завданням)

Після завершення 6 етапів проект буде повноцінним open-source продуктом. Наступне — за потребою:

- **Етап 7 (опційно)** — MSIX-пакування для Microsoft Store: окремий проект `Shelf.Package`, GitHub Action для збірки `.msix`, реєстрація dev-акаунту ($19), верифікація.
- **Етап 8 (опційно)** — Code signing: Authenticode-сертифікат (EV або OV) → користувачі не бачитимуть попередження SmartScreen.
- **Етап 9 (опційно)** — Auto-update mechanism: Squirrel/Velopack/власний механізм. Зараз користувач завантажує нову версію вручну з Releases.
- **Етап 10 (опційно)** — Розширена локалізація сайту, аналітика (Plausible/Umami), додаткові мови інтерфейсу.

---

## Журнал змін плану

- **2026-05-26 v1** — початкова версія плану.
- **2026-05-26 v2** — додано `shelf.bridges.net.ua` як власний піддомен сайту.
- **2026-05-26 v3** — план розбито на 6 послідовних етапів з чек-листами перевірки перед переходом до наступного.
- **2026-05-26 v4** — виконано Етапи 1-3 (локальні файли проекту, GitHub Actions, сайт-візитівка). Готовий до переходу на Етап 4 (реєстрація GitHub + push).
- **2026-05-26 v5** — користувач створив org `bridges-net-ua` (замість `bridges-community`) і обрав репо `shelf` (замість `polychka`). Усі URL оновлено: 75 згадок `bridges-community` → `bridges-net-ua`, потім 60 згадок `bridges-net-ua/polychka` → `bridges-net-ua/shelf` у 12 файлах.
- **2026-05-26 v6** — повний рефакторинг технічної назви проекту: `Polychka` → `Shelf` (`polychka` → `shelf` у lowercase). Зачеплено: 10 тек перейменовано, 11 csproj/sln/resource-файлів перейменовано, 93 текстові файли оновлено через sed. «Поличка» (кирилиця) збережена як `App_Name` у `Strings.uk.xaml`. «ShelfDesk» збережено в `Strings.en.xaml`. Додано міграційний код у `SettingsService.cs` (`%APPDATA%\Polychka\` → `%APPDATA%\Shelf\`) і `AutoStartService.cs` (HKCU\\…\\Run\\Polychka → Shelf). Збірка `dotnet build Shelf.sln -c Debug` пройшла з 0 помилок.

## Поточний статус

🟢 **Етапи 1-3 виконано.** Створено:

- **10 файлів у корені**: `LICENSE`, `.gitignore`, `README.md`, `README.en.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `RELEASE.md`, `SETUP.md`, `plan_reliz.md`.
- **6 файлів у `.github/`**: `workflows/build.yml`, `workflows/release.yml`, `ISSUE_TEMPLATE/bug_report.md`, `ISSUE_TEMPLATE/feature_request.md`, `ISSUE_TEMPLATE/config.yml`, `PULL_REQUEST_TEMPLATE.md`.
- **11 файлів у `docs/`**: `index.html` (UA), `en/index.html` (EN), `style.css`, `assets/logo.png`, `assets/favicon.ico`, `assets/og-image.svg`, `assets/screenshot-1/2/3.svg`, `CNAME`, `.nojekyll`.

### Стан після рефакторингу Polychka → Shelf

- [x] Технічна назва проекту: `Polychka` → `Shelf`.
- [x] Локальна тека SDK і 9 widget-тек перейменовано.
- [x] `Polychka.sln` → `Shelf.sln`, усі csproj-файли перейменовано.
- [x] Resources: `polychka.ico/png` → `shelf.ico/png`.
- [x] Усі namespaces, using-statements, XAML `clr-namespace`, `RootNamespace`, `AssemblyName` оновлено.
- [x] Mutex name: `Polychka_SingleInstance_E94F12C7` → `Shelf_SingleInstance_E94F12C7`.
- [x] Тека налаштувань: `%APPDATA%\Polychka\` → `%APPDATA%\Shelf\`; міграційний код додано.
- [x] Autostart-ключ реєстру: `HKCU\…\Run\Polychka` → `Shelf`; міграційний код додано.
- [x] Лог-файли: `Polychka.crash.log`, `Polychka.vd.log` → `Shelf.crash.log`, `Shelf.vd.log`.
- [x] DLL-фільтр: `Polychka.Widgets.*.dll` → `Shelf.Widgets.*.dll`.
- [x] app.manifest: assemblyIdentity name `Помічник` → `Shelf`.
- [x] Export filename defaults в Strings.uk/en.xaml: `polychka-holidays/-birthdays` → `shelf-…`.
- [x] CHANGELOG, README × 2, CONTRIBUTING, RELEASE, SETUP, плани, CLAUDE.md, AGENTS.md — усі згадки оновлено.
- [x] GitHub Actions workflows (`build.yml`, `release.yml`) — назви zip і шляхи csproj оновлено.
- [x] Сайт `docs/index.html` і `docs/en/index.html` — усі code-блоки збірки оновлено.
- [x] **Збереглося в коді тільки як назва легасі**: `"Polychka"` у списках міграційних шляхів (`SettingsService.LegacyDirs`, `AutoStartService.LegacyValueNames`) — це не активне ім'я, а пошуковий патерн.
- [x] **Збереглося як видима локалізована назва**: `App_Name` = «Поличка» (uk), «ShelfDesk» (en) — це тільки в `Strings.*.xaml`.
- [x] `dotnet build Shelf.sln -c Debug` — 0 помилок, 1 warning (відомий WFAC010 false-positive про DPI). `bin/Debug/net8.0-windows/Shelf.exe` створено (256 КБ).
- [ ] (Опційно, користувач) Перейменувати фізичну теку `D:\project\Polychka` → `D:\project\Shelf` через File Explorer для повної консистентності з новою назвою.

### Чек-лист перед Етапом 4

- [x] Усі 27 файлів створено.
- [x] Рефакторинг `Polychka` → `Shelf` виконано, проект компілюється.
- [ ] Користувач відкрив `docs/index.html` у браузері локально — сайт виглядає як треба (темна тема, логотип, секції, кнопки, навігація працює).
- [ ] Користувач переключився на `docs/en/index.html` — англомовна версія коректна.
- [ ] Користувач переглянув `SETUP.md` і готовий виконувати Етап 4 (реєстрація на GitHub).

🟡 **Очікує** виконання Етапів 4-6 користувачем за інструкцією [SETUP.md](SETUP.md):

- **Етап 4** — реєстрація на GitHub, створення org `bridges-net-ua`, репо `shelf`, перший push.
- **Етап 5** — DNS CNAME-запис + GitHub Pages з власним доменом `shelf.bridges.net.ua`.
- **Етап 6** — створення першого релізу `v1.0.0` через GitHub Web UI; GitHub Actions автоматично збере `.exe`.

### Відомі обмеження (не блокують реліз)

- **OG-image — SVG**, не PNG. Соцмережі (Facebook, Twitter, LinkedIn) можуть не генерувати превʼю при шерингу, поки SVG не сконвертовано у PNG. Це косметичне обмеження — сайт і завантаження працюють. Виправляється одноразово: відкрити `docs/assets/og-image.svg` у браузері → зробити скріншот 1200×630 → зберегти як `docs/assets/og-image.png` → оновити посилання `og:image` в обох HTML на `.png`.
- **Скріншоти — SVG-макети**, не реальні скріншоти. Користувач замінить їх після першого використання додатка: переписати файли `docs/assets/screenshot-1/2/3.svg` на `.png`/`.jpg` і поміняти розширення в обох `index.html`.
