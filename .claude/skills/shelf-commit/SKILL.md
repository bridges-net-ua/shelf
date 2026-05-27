---
name: shelf-commit
description: Закомітити і відразу запушити локальні зміни в проекті Shelf на GitHub (репо bridges-net-ua/shelf). Тригериться, коли користувач пише «ЗРОБИ КОМІТ» (велики літерами або звичайними), або іншими формулюваннями: «закомить», «закомить зміни», «запуш», «запуш зміни», «відправ на github», «push», «зроби commit і push». Аналізує git status, формулює conventional commit message англійською, виконує git add явних файлів + git commit + git push одним заходом, без проміжного пітання про push.
---

# Shelf Commit & Push

Цей skill закриває цикл змін - бере локальні правки, формулює осмислений commit message, комітить і пушить на GitHub. Запускається після того, як користувач протестував зміни руками і влаштовується результатом.

## Преконтролі

1. **Перевір, що ми в правильному проекті**: у поточному cwd мають бути `Shelf.sln` і `Shelf.csproj`. Path-agnostic — назва теки не важлива. Якщо файлів нема — зупинись, скажи «Це не Shelf-проект».

   ```bash
   if [ ! -f Shelf.sln ] || [ ! -f Shelf.csproj ]; then
     echo "Not a Shelf project (Shelf.sln/Shelf.csproj missing in $(pwd))"
     exit 1
   fi
   ```

2. **Перевір, чи є що комітити**:
   ```bash
   git status --short
   ```
   Якщо чисто (нічого не змінено, нічого untracked) - скажи користувачу: «Немає змін для коміту. `git status` чистий.» і зупинись.

3. **Перевір, що локальний main не позаду remote**:
   ```bash
   git fetch origin && git status -uno
   ```
   Якщо локальна гілка позаду origin/main - попередь і запропонуй `git pull --ff-only` перш ніж комітити (інакше push провалиться).

## Етапи виконання

### 1. Подивись повну картину

```bash
git status --short
git diff --stat
```

Якщо є **untracked** файли в списку - подумай уважно:
- Якщо це нові файли, які мають бути в проекті (новий віджет, нова view, нові ресурси) - додавай їх.
- Якщо це випадкові тестові файли, бекапи, логи - попередь користувача, що знайшов підозрілі untracked, спитай чи додавати в gitignore.
- **Ніколи не роби `git add .`** - це може втягнути зайве.

### 2. Сформулюй commit message англійською в Conventional Commits форматі

**Префікси (тип змін):**

| Префікс | Коли використовувати |
|---|---|
| `feat:` | Нова функціональність (новий віджет, нове налаштування) |
| `fix:` | Виправлення бага |
| `docs:` | Тільки документація (README, CLAUDE.md, плани, коментарі) |
| `refactor:` | Реструктуризація коду без зміни поведінки |
| `style:` | Косметичні правки UI (без зміни логіки) |
| `perf:` | Покращення продуктивності |
| `test:` | Додавання чи правка тестів |
| `chore:` | Технічне: gitignore, csproj-метадані, dependencies |
| `ci:` | Зміни в GitHub Actions (`.github/workflows/`) |
| `build:` | Зміни системи збірки (release.yml, csproj build properties) |

**Формат:**

```
<type>: <subject in lower-case, max 72 chars, no period at end>

<optional body explaining WHY, not WHAT - WHAT видно з diff>
<wrap at 72 chars per line>

<optional footer like 'Closes #123' or 'BREAKING CHANGE: ...'>
```

**Приклади з нашого репо як референс:**
- `feat: add 12/24 hour format choice to Clock widget`
- `fix: handle empty city name in Weather widget gracefully`
- `docs: update CONTRIBUTING with widget conventions`
- `chore: bump CHANGELOG to v1.1.0-dev`
- `ci: cache NuGet packages in build workflow`
- `refactor: extract TrayPalette into separate file`

**Особливі субʼєкти за scope:** якщо зміна тільки в одному віджеті - можна додати scope: `feat(Clock): ...`, `fix(Weather): ...`.

### 3. Покажи commit message користувачу

Перед `git add` покажи:
- Список файлів, які підеш додавати (`git status --short` ще раз)
- Запропонований commit message повністю

Користувач має ШАНС сказати «ні, переформулюй» або «не комітимо цей файл». Чекай 1-2 секунди мовчанки = згода. Або якщо повідомлення явно «так, OK» - продовжуй. Якщо «зміни message на ...» - переформулюй і покажи знову.

### 4. Стейджинг

Додавай ЯВНІ файли, не патерни:

```bash
git add WidgetPlugins/Shelf.Widgets.Clock/ClockSettingsDialog.xaml \
        WidgetPlugins/Shelf.Widgets.Clock/ClockWidget.xaml.cs \
        Shelf.Sdk/Strings.uk.xaml \
        Shelf.Sdk/Strings.en.xaml
```

(Можна на одному рядку без backslash, це для читабельності.)

### 5. Commit

Для багаторядкового message використовуй heredoc (підтримується git bash):

```bash
git commit -m "feat: add 12/24 hour format choice to Clock widget

Adds a ComboBox in ClockSettingsDialog letting users pick between
12-hour and 24-hour time display. The choice persists in widget state
and applies immediately via Loc.Format with conditional format string."
```

Якщо коротке (одне-рядкове):
```bash
git commit -m "fix: handle empty city name in Weather widget gracefully"
```

### 6. Push одразу

```bash
git push
```

**Без додаткових пітань.** Користувач уже двічі підтвердив намір: один раз словом «ЗРОБИ КОМІТ», другий раз - тим, що дозволив commit message пройти. Якщо push провалиться (наприклад, конфлікт з origin) - покажи помилку і чекай інструкцій, не пробуй `--force`.

### 7. Звіт користувачу

```
✅ Запушено.

Commit: a1b2c3d
Message: feat: add 12/24 hour format choice to Clock widget

Файли (4):
- WidgetPlugins/Shelf.Widgets.Clock/ClockSettingsDialog.xaml
- WidgetPlugins/Shelf.Widgets.Clock/ClockWidget.xaml.cs
- Shelf.Sdk/Strings.uk.xaml
- Shelf.Sdk/Strings.en.xaml

CI: https://github.com/bridges-net-ua/shelf/actions
(build.yml стартує через ~30 сек, перевіряє збірку в чистій хмарі ~3-5 хв)

GitHub: https://github.com/bridges-net-ua/shelf/commit/a1b2c3d
```

## Чого НЕ робити

- ❌ **`git push --force`, `--force-with-lease`** - заборонено без явного дозволу користувача (глобальне правило).
- ❌ **`git reset --hard`** - те саме.
- ❌ **`git commit --amend` після push** - переписує історію публічних комітів.
- ❌ **`--no-verify`** - не пропускати hooks (зараз hooks немає, але правило).
- ❌ **`git add .` або `git add -A`** - надто широко, може втягнути логи/секрети.
- ❌ **Створювати теги** - це частина release-процесу (`RELEASE.md`), не звичайного коміту.
- ❌ **Бранчуватись** (`git checkout -b ...`) - проект простий, single-developer, для нього feature-бранчі зайве.

## Особливі випадки

### Якщо у списку untracked є щось підозріле

Наприклад: `secrets.json`, `*.pfx`, `appsettings.Local.json`, `.env`, файли понад 50 МБ.

- **СТОП.** Покажи користувачу, не додавай.
- Запропонуй або додати у `.gitignore`, або вилучити, або (якщо це справді треба) явно підтвердити.

### Якщо є зміни в `Resources/`, які я не очікую

Особливо `shelf.ico`, `shelf.png` - це бренд-ассети. Подвійно перевір, що це навмисна зміна, а не випадкове перезаписання.

### Якщо `git push` провалився

Найпоширеніше:
- **Rejected (non-fast-forward)** - локальний main позаду origin. Зроби `git pull --ff-only`, потім знову `git push`.
- **Authentication failed** - креди закінчились. Покажи помилку, скажи користувачу пройти `gh auth login` або оновити PAT.
- **Repository not found** - перевір `git remote -v`. Має бути `https://github.com/bridges-net-ua/shelf.git`.

### Якщо треба зробити коміт ТІЛЬКИ певних файлів зі списку зміненого

Користувач може сказати: «ЗРОБИ КОМІТ тільки годинника, замітки лиши на потім». Уважно прочитай прохання, обмеж `git add` тільки до вказаних файлів. Інші лишаться в робочій теці незакомічені - це нормально.
