# SETUP - Покрокова інструкція першого розгортання Поличка на GitHub

Цей документ потрібен **один раз** - щоб запустити проект «з нуля» на GitHub: створити організацію, репозиторій, налаштувати сайт за власним доменом і випустити перший реліз.

Для випуску подальших версій дивись [RELEASE.md](RELEASE.md). Для розробки - [CONTRIBUTING.md](CONTRIBUTING.md).

> **Час виконання**: ~30-40 хвилин активної роботи + 10-60 хв пасивного очікування DNS і SSL-сертифіката.

---

## Етап 4 - Реєстрація на GitHub і перший push

### 4.1. Реєстрація на GitHub

1. Відкрий [github.com/signup](https://github.com/signup).
2. Email - твій основний (наприклад `lextiks@gmail.com` або `shelf@bridges.net.ua`).
3. Пароль - сильний (краще через менеджер паролів).
4. Username (нік) - на твій вибір. Це твій особистий профіль; для проекту створимо окрему організацію далі. Приклади: `lextiks`, `bridges-author`.
5. Country: Ukraine.
6. Підтверди email і налаштуй 2FA (Settings → Password and authentication → Enable two-factor authentication) - це обов'язково для безпеки.

### 4.2. Створення організації `bridges-net-ua`

1. Відкрий [github.com/account/organizations/new](https://github.com/account/organizations/new).
2. Plan: **Free** (Create a free organization).
3. Organization account name: **`bridges-net-ua`**.
4. Contact email: `shelf@bridges.net.ua`.
5. This organization belongs to: **My personal account**.
6. Натисни Next, пропусти запрошення учасників (можна додати пізніше).

### 4.3. Створення публічного репо `shelf`

1. Перейди на сторінку організації: `github.com/bridges-net-ua`.
2. Натисни зелену кнопку **New repository** (або відкрий `github.com/organizations/bridges-net-ua/repositories/new`).
3. Owner: `bridges-net-ua`.
4. Repository name: **`shelf`** (важливо - саме маленькими літерами).
5. Description: `Customizable widget dock for Windows desktop / Док-панель з віджетами для робочого столу Windows`.
6. Visibility: **Public**.
7. **НЕ вмикай** жодну з опцій нижче (Add a README, Add .gitignore, Choose a license) - усі ці файли вже є локально.
8. Натисни **Create repository**.

GitHub покаже сторінку зі швидким стартом - переходимо до наступного кроку.

### 4.4. Локальне налаштування git і перший push

> **Перед стартом — за бажанням перейменуй фізичну теку проекту.** Зараз вона на диску називається `D:\project\Polychka`. Для повної консистентності з новою назвою закрий усі IDE (Visual Studio / Rider / VS Code), у File Explorer перейменуй `Polychka` → `Shelf`. Це впливає тільки на твою машину, у репозиторій нічого не пише. Якщо не перейменовуєш — у командах нижче залиш свій реальний шлях замість `D:\project\Shelf`.

Відкрий PowerShell у корені проекту:

```powershell
cd D:\project\Shelf
```

Перевір, чи git встановлено:

```powershell
git --version
```

Якщо немає - встанови [Git for Windows](https://git-scm.com/download/win).

Налаштуй своє ім'я і email **глобально** (одноразово):

```powershell
git config --global user.name "Твоє Ім'я"
git config --global user.email "shelf@bridges.net.ua"
```

> Email тут має співпадати з email твого GitHub-акаунта (або з одним із verified emails у Settings → Emails), інакше комити не з'являтимуться як твої на профілі.

Ініціалізуй репо і зроби перший комміт:

```powershell
git init -b main
git add .
git commit -m "Initial commit: Shelf v1.0.0"
```

> Якщо `git add .` лякає кількістю файлів - перевір спершу `git status` і потім `git status --ignored` що `.gitignore` правильно відсіює `bin/`, `obj/`, `artifacts/` тощо.

Підключи віддалений репозиторій і запуш:

```powershell
git remote add origin https://github.com/bridges-net-ua/shelf.git
git push -u origin main
```

При першому push GitHub запросить авторизацію. Найпростіше - встановити [GitHub CLI (gh)](https://cli.github.com/) і виконати `gh auth login` (відкриє браузер). Альтернативно - створити **Personal Access Token (Classic)** у Settings → Developer settings → Personal access tokens і ввести його як пароль.

### 4.5. Перевірка

- Відкрий `https://github.com/bridges-net-ua/shelf` - повинні бути всі файли проекту.
- Перейди у вкладку **Actions** - workflow `build.yml` запустився автоматично після push. Дочекайся ✅ (3-5 хв).
- На головній сторінці репо `README.md` повинен красиво відображатись з логотипом, бейджами, секціями.

### 4.6. (Опційно) Профіль організації

- На сторінці `github.com/bridges-net-ua` → **Settings** → **Profile** → завантаж логотип (`Resources/shelf.png`).
- Description: `Open-source software by Bridges Community`.
- URL: `https://shelf.bridges.net.ua/`.
- Email: `shelf@bridges.net.ua`.

---

## Етап 5 - Підключення власного домену `shelf.bridges.net.ua`

### 5.1. Додавання DNS-запису

1. Зайди в панель керування доменом `bridges.net.ua` у твого реєстратора (наприклад, ім.юа, IMENA.UA, Hostmaster, Cloudflare).
2. Знайди розділ **DNS** / **DNS records** / **Записи зони**.
3. Додай новий запис:

   | Поле | Значення |
   |---|---|
   | Тип | `CNAME` |
   | Ім'я / Host / Subdomain | `shelf` |
   | Значення / Target / Points to | `bridges-net-ua.github.io` |
   | TTL | `3600` або «За замовчуванням» / `Auto` |

4. Збережи.

> **Важливо**: у полі «Значення» **без** крапки в кінці і **без** протоколу `https://`. Просто `bridges-net-ua.github.io`. Деякі реєстратори додають крапку автоматично - це нормально.

### 5.2. Перевірка поширення DNS

DNS-зміни поширюються від 5 хв до 24 год (зазвичай 10-30 хв). Перевір на [whatsmydns.net](https://www.whatsmydns.net/), ввівши `shelf.bridges.net.ua` і тип `CNAME`. Більшість регіонів має показувати `bridges-net-ua.github.io`.

В PowerShell локально:

```powershell
nslookup shelf.bridges.net.ua
```

Має показати щось на зразок:
```
shelf.bridges.net.ua  canonical name = bridges-net-ua.github.io
```

### 5.3. Увімкнення GitHub Pages

1. У репо `bridges-net-ua/shelf` → **Settings**.
2. У лівому меню - **Pages**.
3. **Source**: `Deploy from a branch`.
4. **Branch**: `main`, **folder**: `/docs`.
5. Натисни **Save**.
6. GitHub автоматично прочитає файл `docs/CNAME` і заповнить поле **Custom domain** значенням `shelf.bridges.net.ua`. Якщо ні - впиши вручну і натисни Save.
7. Зачекай 10-30 хв - GitHub видає Let's Encrypt SSL-сертифікат.
8. Коли з'явиться галочка **Enforce HTTPS** - постав її. (Не активна, поки сертифікат не виданий.)

### 5.4. Перевірка сайту

- Відкрий `https://shelf.bridges.net.ua/` - має завантажитись головна (українська).
- У адресному рядку - **замок** (HTTPS валідний).
- Відкрий `https://shelf.bridges.net.ua/en/` - має завантажитись англійська версія.
- Перевір на [SSL Labs Test](https://www.ssllabs.com/ssltest/analyze.html?d=shelf.bridges.net.ua), що рейтинг ≥ B.

> **Якщо сайт не відкривається**: перевір вкладку Settings → Pages у репо. Якщо там червоний банер з помилкою DNS - значить, CNAME ще не поширився; почекай. Якщо проблема з SSL - GitHub автоматично перевипустить сертифікат протягом години.

---

## Етап 6 - Перший реліз v1.0.0

### 6.1. Підготовка csproj

Перевір, що у `Shelf.csproj` є рядок `<Version>1.0.0</Version>` у блоці `<PropertyGroup>`. Якщо немає:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net8.0-windows</TargetFramework>
  ...
  <Version>1.0.0</Version>
  <FileVersion>1.0.0.0</FileVersion>
  <AssemblyVersion>1.0.0.0</AssemblyVersion>
  <Company>Bridges Community</Company>
  <Product>Shelf</Product>
  <Copyright>© 2026 Bridges Community</Copyright>
  <Description>Customizable widget dock for Windows desktop</Description>
</PropertyGroup>
```

Закоміть і запушни:

```powershell
git add Shelf.csproj
git commit -m "Set version metadata for v1.0.0"
git push
```

Дочекайся ✅ у Actions.

### 6.2. Створення релізу

1. У репо: вкладка **Releases** (праворуч на головній) → **Draft a new release**.
   Або відкрий напряму `https://github.com/bridges-net-ua/shelf/releases/new`.
2. **Choose a tag** → ввести `v1.0.0` → випадне «Create new tag: v1.0.0 on publish» - вибрати.
3. **Release title**: `Shelf v1.0.0 - перший публічний реліз`.
4. **Description**: скопіюй вміст запису `[1.0.0]` з `CHANGELOG.md` (без квадратних дужок навколо `1.0.0`).
5. Прокрути вниз - залиш галочку **Set as the latest release**.
6. **Publish release**.

### 6.3. Автоматична збірка

Одразу після Publish:
- GitHub створює тег `v1.0.0`.
- Запускається `release.yml` workflow (вкладка **Actions**).
- Через 3-5 хв з'явиться артефакт `Shelf-v1.0.0-win-x64.zip` (~70 МБ) у списку assets релізу.

### 6.4. Фінальна перевірка

- Зайди на `https://github.com/bridges-net-ua/shelf/releases/latest` - бачиш `v1.0.0` і кнопку для завантаження zip.
- Натисни на zip - він завантажується.
- Розпакуй у тимчасову папку, запусти `Shelf.exe`.
- Перевір кнопку «Завантажити» на сайті `https://shelf.bridges.net.ua/` - вона веде на `releases/latest` і працює.
- (Опційно) Створи перший issue «👋 Welcome - вітаємо в Поличці» у репо як прикріплений до Discussions.

---

## Готово!

Тепер проект:

- ✅ Живе як open-source на `github.com/bridges-net-ua/shelf`
- ✅ Має сайт-візитівку на `shelf.bridges.net.ua` з HTTPS
- ✅ Має готовий .exe для скачування з Releases
- ✅ Автоматично перезбирається на кожен push (build.yml)
- ✅ Автоматично пакує реліз на кожен новий tag `v*` (release.yml)

Подальші релізи робляться за [RELEASE.md](RELEASE.md) - просто bump версії, push, створи tag.

---

## Часті проблеми

### Push помилка «Permission denied (publickey)»
Ти підключив remote через SSH-URL (`git@github.com:...`), а ключа не маєш. Переключи на HTTPS:
```powershell
git remote set-url origin https://github.com/bridges-net-ua/shelf.git
```

### Push помилка «authentication failed» (HTTPS)
Або встанови [GitHub CLI](https://cli.github.com/) і виконай `gh auth login` (найпростіше), або створи Personal Access Token у Settings → Developer settings → Personal access tokens (classic) з правом `repo` і використай його як пароль.

### Pages показує 404
- Перевір, що у Settings → Pages вибрано джерело `main` / `/docs`.
- Перевір, що файл `docs/index.html` існує у репо (через GitHub Web UI).
- Зачекай 5-10 хв після Save.

### Custom domain «Domain's DNS record could not be retrieved»
DNS ще не поширився. Зачекай. Перевір через `nslookup shelf.bridges.net.ua` або [whatsmydns.net](https://www.whatsmydns.net/).

### Сайт працює, але без HTTPS («Enforce HTTPS» неактивна)
Let's Encrypt видає сертифікат після того, як DNS стабільно показує на GitHub. Це 10-60 хв. Якщо за годину сертифіката нема - спробуй зняти і поставити заново Custom domain у Settings → Pages.

### `release.yml` падає у Actions
Подивись логи у вкладці Actions. Типові причини:
- `<Version>` не оновлено в csproj - звідки workflow не може взяти версію.
- Сторонні залежності, які впали при `dotnet restore`.

### Reset перебудувати - як видалити невдалий реліз
- На сторінці Releases → Edit → Delete release (внизу).
- У PowerShell видали локальний і віддалений тег:
  ```powershell
  git tag -d v1.0.0
  git push --delete origin v1.0.0
  ```
- Виправ помилку, запушни новий комміт, створи реліз заново.
