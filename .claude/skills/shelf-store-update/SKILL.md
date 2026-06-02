---
name: shelf-store-update
description: Провести напівручну публікацію оновлення ShelfDesk (Поличка) у Microsoft Store. Тригериться, коли користувач пише «ОНОВИ STORE», «онови стор», «онови в сторі», «опублікуй в стор», «опублікуй оновлення в магазині», «онови в магазині», «store update», «публікація в Microsoft Store», «залий новий msix у store». Це ГАЙД, а не автомат - бере вже зібраний MSIX з останнього GitHub Release, відкриває Partner Center, дає покроковий чек-лист і текст «What's new» з CHANGELOG. САМ пакет у Store не заливає (немає API-доступу - акаунт без Entra directory). Запускати ПІСЛЯ shelf-release, коли GitHub Actions добудував MSIX. Не бампає версію (це shelf-release) і не комітить.
---

# Shelf - Microsoft Store update (напівручна публікація)

Цей skill проводить тебе через публікацію оновлення в Microsoft Store **після того**, як `ЗРОБИ РЕЛІЗ` (shelf-release) створив тег і GitHub Actions (`release.yml`) зібрав `.msix` і прикріпив його до GitHub Release.

**Чому напівручний:** акаунт `bridges@bridges.net.ua` - це Microsoft Account без Entra directory, тому повна CI-автопублікація (`store-publish.yml`) заблокована (немає де створити app registration + client secret для Store submission API). Завантаження пакета в Partner Center лишається ручним кроком у браузері (~2 хв). Цей skill максимально його спрощує - знаходить файл, відкриває потрібні сторінки, дає готовий текст і чек-лист.

## Ключові дані проекту

- **Store ID:** `9NFC2DKPQDLJ`
- **Store-сторінка продукту:** https://apps.microsoft.com/detail/9NFC2DKPQDLJ
- **Partner Center:** https://partner.microsoft.com/dashboard
- **Releases:** https://github.com/bridges-net-ua/shelf/releases
- **Ім'я MSIX-асета у релізі:** `ShelfDesk-<tag>.msix` (напр. `ShelfDesk-v1.2.0.msix`)
- **Repo:** `bridges-net-ua/shelf`

> ⚠️ `gh` CLI на цій машині **не залогінений** - НЕ використовуй `gh`. Для читання релізів бери **GitHub REST API через curl** (репо публічне, авторизація не потрібна) і `git` для локальних тегів.

## Преконтролі (виконати ДО будь-яких дій)

1. **Правильний проект**: cwd має містити `Shelf.sln` і `Shelf.csproj`. Path-agnostic.
   ```bash
   if [ ! -f Shelf.sln ] || [ ! -f Shelf.csproj ]; then
     echo "Not a Shelf project (Shelf.sln/Shelf.csproj missing in $(pwd))"; exit 1
   fi
   ```
2. Нагадай користувачу одним рядком: «Це напівручний процес - я підготую все і відкрию Partner Center, але фінальне завантаження й Submit тиснеш ти.»

## Крок 1 - Визначити версію/тег для публікації

```bash
git fetch --tags origin 2>/dev/null
git describe --tags --abbrev=0
```

- Покажи знайдений останній тег (напр. `v1.2.0`) і спитай: «Публікуємо в Store саме `<tag>`?»
- Якщо користувач хоче інший тег - використай його. Далі скрізь `<TAG>` = підтверджений тег.

## Крок 2 - Перевірити, що Release з MSIX готовий

GitHub Actions збирає MSIX кілька хвилин після push тега. Перевір, що asset уже існує (публічний API, без auth):

```bash
curl -s "https://api.github.com/repos/bridges-net-ua/shelf/releases/tags/<TAG>" \
  | grep -o '"name": *"ShelfDesk-[^"]*\.msix"'
```

- Якщо знайдено `ShelfDesk-<TAG>.msix` - добре, запам'ятай URL завантаження:
  `https://github.com/bridges-net-ua/shelf/releases/download/<TAG>/ShelfDesk-<TAG>.msix`
- Якщо **порожньо** - CI ще збирає або реліз не створений. Скажи користувачу зачекати кілька хвилин і дай лінк на Actions:
  `https://github.com/bridges-net-ua/shelf/actions`
  Зупинись, доки asset не з'явиться.

## Крок 3 - ГЕЙТ: чи завершилась попередня certification?

⚠️ **Критично.** Microsoft Store дозволяє лише **одну активну submission на продукт**. Якщо попередній реліз ще «In certification» - нова публікація впаде.

Спитай користувача прямо: **«Попередня submission у Partner Center уже у статусі In the Store (не In certification)?»**
- Якщо **ні / не знає** - попроси перевірити на https://partner.microsoft.com/dashboard (продукт ShelfDesk → Application overview, статус). Не продовжуй, доки не підтверджено.
- Якщо **так** - далі.

## Крок 4 - Підготувати текст «What's new»

Витягни секцію CHANGELOG для цієї версії (без літери `v`), щоб користувач вставив її у Store listing:

```bash
VER=$(echo "<TAG>" | sed 's/^v//')
awk -v ver="$VER" '
  $0 ~ "^## \\[" ver "\\]" {f=1; next}
  f && /^## \[/ {exit}
  f && /^---[[:space:]]*$/ {exit}
  f {print}
' CHANGELOG.md
```

- Покажи цей текст користувачу як **готовий «What's new»** (можна трохи скоротити для Store - підкажи).
- Якщо секція порожня - просто запропонуй коротко описати зміни вручну.

## Крок 5 - Відкрити браузер і провести по кроках

Відкрий обидві потрібні сторінки (PowerShell, безпечно - лише відкриває URL):

```powershell
Start-Process "https://github.com/bridges-net-ua/shelf/releases/download/<TAG>/ShelfDesk-<TAG>.msix"
Start-Process "https://partner.microsoft.com/dashboard"
```

Потім виведи користувачу **точний чек-лист** дій у Partner Center:

1. Дочекайся завантаження `ShelfDesk-<TAG>.msix` (перша вкладка).
2. Partner Center → **Apps and games → ShelfDesk → Start update** (новий submission).
3. **Packages**: видали старий пакет (за наявності), перетягни/завантаж новий `ShelfDesk-<TAG>.msix`. Дочекайся валідації (зелено; warning про `runFullTrust` - норма).
4. (опційно) **Store listings → English / Українська → What's new**: встав текст із Кроку 4.
5. Залиш решту секцій як є (вони успадковуються з попередньої submission).
6. **Submit to the Store**.

## Крок 6 - Звіт і нагадування

Після того як користувач натиснув Submit, нагадай:
- Certification: зазвичай години, інколи до 3 робочих днів.
- Після проходження оновлення **саме розкотиться** користувачам Store (Microsoft робить це автоматично, ~до 24 год).
- **Не запускай `ОНОВИ STORE` ще раз**, доки ця submission не завершить certification.
- Store-сторінка: https://apps.microsoft.com/detail/9NFC2DKPQDLJ

## Чого НЕ робити

- ❌ **Не намагайся залити пакет у Store автоматично** (через `msstore`/API) - авторизації немає (немає Entra directory). Тільки напівручний шлях через браузер.
- ❌ **Не бампай версію і не чіпай `Shelf.csproj`/`CHANGELOG.md`** - це робота `shelf-release` (ЗРОБИ РЕЛІЗ). Цей skill публікує вже випущену версію.
- ❌ **Не збирай MSIX локально** без потреби - бери готовий артефакт з GitHub Release (його зібрав CI з тією ж версією). Локальний `tools/make-msix.ps1` - лише запасний варіант, якщо релізного MSIX чомусь немає.
- ❌ **Не використовуй `gh`** (не залогінений) - тільки git + curl до публічного API.
- ❌ **Не комітити** - цей skill нічого в репозиторії не змінює.

## Якщо релізного MSIX немає (запасний варіант)

Якщо з якоїсь причини GitHub Release не містить `.msix` (напр. реліз робили до того, як `release.yml` навчився збирати MSIX), збери локально:

```powershell
pwsh tools/make-msix.ps1
```
Результат: `bin/Store/Shelf.msix` (версія з `Shelf.csproj`). Завантаж саме його. Потребує Windows SDK на машині.
