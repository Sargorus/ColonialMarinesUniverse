# Инструкция для тестировщика

Это пошаговая инструкция: как скачать ветку, собрать, запустить сервер и
клиент и что проверить. Список фич и сценариев лежит рядом в
[`BRANCH_FEATURES.md`](BRANCH_FEATURES.md).

## 1. Что понадобится

- Windows 10/11.
- Git с поддержкой submodules.
- .NET SDK из `global.json` (нужен `10.0.100`, подойдёт более новый
  patch/feature с тем же major).
- Python 3 — используется сборочными скриптами репозитория.
- Свободное место на диске: репозиторий с ассетами и сборкой занимает
  несколько гигабайт.
- Свободный порт `1212`.

PostgreSQL не нужен: сервер по умолчанию использует SQLite.

## 2. Скачивание ветки

Для основной ветки:

```powershell
git clone --branch RoboticsUpdate --recurse-submodules https://github.com/Sargorus/ColonialMarinesUniverse.git
cd ColonialMarinesUniverse
git log -1 --oneline
```

Последний коммит должен быть:

```text
f690fab55a feat(research): require personal sightings and reward botany reports
```

Если submodule не скачался:

```powershell
git submodule update --init --recursive
```

`RobustToolbox/` — обязательный submodule. Без него сборка не запустится.

### Другие ветки

В том же клоне:

```powershell
git fetch origin
git checkout CorporateSecUpdate
git submodule update --init --recursive
```

или:

```powershell
git checkout SafeSystemUpdate
```

Лучше тестировать каждую ветку в отдельной папке, чтобы не путать
сборку и сохранения. Если переключаетесь в одном клоне — после
`git checkout` сделайте чистую пересборку (см. раздел 5).

`RoboticsQueueNetwork` отдельно тестировать не нужно: она уже влита в
`RoboticsUpdate`. `test/RoboticsUpdate` — локальная ветка автора, на
GitHub её нет.

## 3. Сборка

Из корня репозитория:

```powershell
dotnet build Content.Server/Content.Server.csproj --configuration Tools
dotnet build Content.Client/Content.Client.csproj --configuration Tools
```

Первый запуск может занять несколько минут: скачиваются пакеты и
собираются ассеты. Дождитесь `Сборка успешно завершена`.

Если NuGet-пакеты не скачались:

```powershell
dotnet restore SpaceStation14.slnx
```

Затем повторите сборку.

## 4. Запуск

1. Запустите сервер:

   ```powershell
   runserver.bat
   ```

   Или:

   ```powershell
   dotnet run --project Content.Server --configuration Tools
   ```

2. Дождитесь в консоли сервера сообщения о запуске. Не закрывайте окно.
3. Запустите клиент:

   ```powershell
   runclient.bat
   ```

   Или:

   ```powershell
   dotnet run --project Content.Client --configuration Tools
   ```

4. В клиенте подключитесь к `localhost` / `127.0.0.1` и порту `1212`,
   создайте персонажа и зайдите в раунд.

Сервер использует SQLite и создаёт локальную базу сам. Если сервер сразу
закрывается:

- проверьте, не занят ли порт `1212` (закройте старый сервер);
- проверьте, что `RobustToolbox` submodule на месте;
- удалите `bin/` и `obj/`, затем соберите заново;
- посмотрите последние строки консоли сервера.

## 5. Переключение веток и чистая пересборка

После `git checkout` ветки ассеты и бинарники могут остаться от прошлой
ветки. Для чистого теста:

```powershell
git status
git checkout <branch>
git submodule update --init --recursive
dotnet clean Content.Server/Content.Server.csproj
dotnet clean Content.Client/Content.Client.csproj
dotnet build Content.Server/Content.Server.csproj --configuration Tools
dotnet build Content.Client/Content.Client.csproj --configuration Tools
```

Если ветки сильно отличаются, надёжнее клонировать каждую в отдельную
папку.

## 6. Админ-доступ и тестовые сущности

Вы — хост локального сервера, поэтому админ-панель доступна. Откройте
её через меню/клавишу админ-панели и используйте `Spawn` или консоль
сервера.

Полезные прототипы:

- робототехник: `AU14JobCivilianScientistRoboticist`;
- фабрикатор: `CMURoboticsFabricator`;
- терминал заказов: `CMURoboticsOrderConsole`;
- диагностический терминал SMO: `CMUProductionDiagnosticConsole`;
- XRF-сканер: `CMUXRFScanner`;
- химический терминал: `CMUResearchDataTerminalCorporate`
  (или `...Govfor`, `...Opfor`, `...Colony`);
- intel-консоль: `RMCComputerIntelGovfor`, `RMCComputerIntelOpfor`,
  `RMCComputerIntelCLF`;
- корпоративный терминал связного: `AU14CorporateConsole`;
- корпоративный грузовой лифт: `AU14CorporateASRSElevator`;
- ксеноморф: `CMXenoDrone`;
- неоморф: `CMU14XenoNeomorph`;
- вендиго: `CMUWendigo`;
- наруч хищника: `CMUYautjaBracer`;
- бумага: `CMPaper` / `Paper`, ручка: `CMPen` / `Pen`.

Для witness-сценария удобно:

1. Заспавнить `CMXenoDrone`, `CMU14XenoNeomorph` и `CMUWendigo` рядом.
2. Осмотреть каждого.
3. Проверить знание через ПКМ по себе → «Review research knowledge».
4. Записать знание на бумагу и проверить сдачу в терминал.

## 7. Что тестировать

Полный чек-лист — в [`BRANCH_FEATURES.md`](BRANCH_FEATURES.md). Краткий
порядок для `RoboticsUpdate`:

1. Робототехник: доступы, фабрикатор, терминал, заказ, выплата.
2. Дроны: utility, programmable, combat/flamer, медбот, боты.
3. Конечности и операции.
4. Модульная мина.
5. SMO-диагностика: контракт, анализ, μ, перенаправление, выплата.
6. XRF-сканер → отчёт → «Remember» → сдача в терминал.
7. Бумага + ручка: одно знание, текст не открывается, знание в описании.
8. Частичный отчёт: только контракт, без свойств.
9. Ботаника: отчёт читается, награда возвращается.
10. Witness: личный осмотр, лимит 3, 3 sample data.
11. Чужое свидетельство: прочитал — сдать нельзя; сам увидел — можно.
12. Intel: GOVFOR/OPFOR работают, CLF отклоняет.
13. GOVFOR в Insurgency: research заблокирован.
14. Каталог `Knowledge` в химическом терминале и кнопка сдачи.
15. Поиск/фильтр в личных знаниях.
16. Гарантии: кредиты, цены 8/10/15, уровень контракта по редкости.
17. Специализация: Robotics ×1,5; Bioengineering +1 за контракт.
18. Миссия наруча: один наруч в карго, 50 company value за вскрытие.

Для `CorporateSecUpdate` и `SafeSystemUpdate` — соответствующие разделы
`BRANCH_FEATURES.md`.

## 8. Как сообщать о багах

Пришлите:

1. Ветку и коммит (`git log -1 --oneline`).
2. Что делали по шагам.
3. Что ожидали и что получили.
4. Скриншот/видео, если проблема визуальная.
5. Последние 30–50 строк консоли сервера и клиента.
6. Если возможно — имя карты, профессию и ID предметов.

Перед отправкой проверьте, что баг повторяется после перезапуска раунда
и чистой пересборки ветки.

## 9. Автотесты (по желанию)

```powershell
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~CMUResearchDocumentationTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~CMUResearchDocumentInteractionTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~CMUResearchDocumentationIntelTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~CMUFactionResearchTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~CMUCompanyMissionTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~CMURoboticsContractTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~CMURoboticsTerminalTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~CMURoboticsBotsTest"
```

Тесты лучше запускать по одной фикстуре: полный прогон всех тестов одним
процессом на некоторых машинах падает по памяти при предзагрузке
текстур, это не ошибка кода.

YAML-линтер:

```powershell
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj
```
