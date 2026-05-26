<!--
Дякуємо за внесок! Перш ніж надсилати PR, ознайомся з CONTRIBUTING.md.
Thanks for contributing! Please read CONTRIBUTING.md before submitting.
-->

## Опис / Description
<!-- Що змінює цей PR і навіщо? -->


## Пов'язаний issue / Related issue
<!-- Якщо PR закриває issue, вкажи: Closes #123 -->


## Тип змін / Type of change

- [ ] 🐛 Bug fix (виправлення без зміни API/поведінки)
- [ ] ✨ New feature (нова функціональність)
- [ ] 💥 Breaking change (зміна, яка ламає сумісність)
- [ ] 📝 Documentation (тільки документація)
- [ ] 🎨 UI/UX (поліпшення інтерфейсу без зміни логіки)
- [ ] 🌐 Localization (локалізація / переклади)
- [ ] 🧪 Tests / CI
- [ ] ♻️ Refactor (рефакторинг без зміни поведінки)

## Чек-лист / Checklist

- [ ] Я зібрав проект локально (`dotnet build Shelf.sln -c Debug`) - збірка проходить без помилок.
- [ ] Я запустив додаток і перевірив сценарій, який цей PR змінює.
- [ ] Я дотримався конвенцій з [CONTRIBUTING.md](../CONTRIBUTING.md):
  - [ ] Жоден видимий рядок не хардкоднутий - усі через `Strings.uk.xaml` + `Strings.en.xaml`.
  - [ ] У видимих рядках використано ASCII `-`, а не em-dash `—`.
  - [ ] У видимих рядках використано українські лапки `«»`, а не `""`.
  - [ ] Тематичні brush-ключі - через `{DynamicResource ...}`, а не `{StaticResource}`.
  - [ ] Нові вікна викликають `WindowChrome.Apply(this)` у конструкторі.
  - [ ] SDK / віджети не посилаються на хост-проект.
- [ ] Я оновив `CHANGELOG.md` (секція `[Unreleased]`) для користувацьких змін.
- [ ] Я оновив документацію (README, CLAUDE.md), якщо це потрібно.

## Скріншоти / Screenshots
<!-- Якщо PR змінює UI - бажано прикріпити скріншоти до і після. -->


## Як тестував / How was this tested?
<!-- Опиши кроки ручного тестування. -->
