# ZibraSharp

Консольная утилита: сканирует папку, упаковывает файлы в ZIP-архив и загружает его в Google Drive.

Это переписанная на C#/.NET 9 версия оригинальной Go-утилиты. Архивы создаются в стандартном формате ZIP (поддерживаются ZIP64 и UTF-8 имена файлов), поэтому открываются любым архиватором.

## Содержание

- [Возможности](#возможности)
- [Требования](#требования)
- [Установка](#установка)
- [Быстрый старт в Windows](#быстрый-старт-в-windows)
- [Настройка Google Drive](#настройка-google-drive)
- [Важно: рабочая папка](#важно-рабочая-папка)
- [Все команды кратко](#все-команды-кратко)
- [Команда start](#команда-start)
- [Команда settings](#команда-settings)
- [Команда extensions](#команда-extensions)
- [Команда log](#команда-log)
- [Команда pending](#команда-pending)
- [Команды version и help](#команды-version-и-help)
- [Глобальные флаги](#глобальные-флаги)
- [Коды выхода](#коды-выхода)
- [Рабочие файлы](#рабочие-файлы)
- [Офлайн-режим и очередь загрузки](#офлайн-режим-и-очередь-загрузки)
- [Логирование](#логирование)
- [Разработка](#разработка)
- [Бенчмарки](#бенчмарки)
- [Структура проекта](#структура-проекта)
- [Безопасность](#безопасность)
- [Решение проблем](#решение-проблем)

## Возможности

- Рекурсивное сканирование папки с исключением расширений.
- Настоящее параллельное сжатие: файлы сжимаются пулом воркеров, затем собираются одним писателем.
- Поддержка ZIP64 и UTF-8 имён.
- Атомарная замена архива: новый архив пишется во временный файл и переносится на место только после успешной записи.
- Офлайн-режим: если загрузка не удалась, архив сохраняется и попадает в очередь; при следующем запуске утилита предложит его загрузить.
- Структурированные JSON-логи с ротацией.
- Атомарная запись JSON-файлов состояния.
- `--dry-run`, настройка числа воркеров, защита от перезаписи, отмена по Ctrl+C.
- Тесты xUnit и бенчмарки BenchmarkDotNet.

## Требования

- .NET 9 SDK или runtime.
- Аккаунт Google.
- Включённый Google Drive API в Google Cloud.
- OAuth-клиент типа **Desktop app** (`credentials.json`).

## Установка

Сборка из исходников:

```powershell
dotnet build .\ZibraSharp\ZibraSharp.sln -c Release
```

Публикация в отдельную папку:

```powershell
dotnet publish .\ZibraSharp\ZibraSharp.csproj -c Release -o C:\ZibraSharp\app
```

После публикации исполняемый файл:

```text
C:\ZibraSharp\app\zibrasharp.exe
```

Опционально — self-contained сборка в один файл:

```powershell
dotnet publish .\ZibraSharp\ZibraSharp.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o C:\ZibraSharp\app
```

Для Linux/macOS используй соответствующий RID, например `linux-x64` или `osx-arm64`.

### Добавить в PATH

Только для текущей сессии PowerShell:

```powershell
$env:Path += ";C:\ZibraSharp\app"
```

Навсегда для текущего пользователя:

```powershell
[Environment]::SetEnvironmentVariable(
    "Path",
    [Environment]::GetEnvironmentVariable("Path", "User") + ";C:\ZibraSharp\app",
    "User"
)
```

После этого перезапусти PowerShell и проверь:

```powershell
zibrasharp --version
```

## Быстрый старт в Windows

```powershell
# 1. Перейти в постоянную рабочую папку
cd C:\ZibraSharp

# 2. Проверить запуск
.\app\zibrasharp.exe --version

# 3. Указать папку, куда складывать архивы
.\app\zibrasharp.exe settings C:\Backups

# 4. Положить credentials.json в C:\ZibraSharp\credentials.json

# 5. Проверить сканирование без архивации и без credentials
.\app\zibrasharp.exe start C:\Data --dry-run

# 6. Первый реальный запуск — откроется браузер для OAuth
.\app\zibrasharp.exe start C:\Data
```

> В PowerShell программы из текущей папки не запускаются по имени. Нужно `.\zibrasharp.exe` или полный путь. Если папка добавлена в `PATH`, можно писать просто `zibrasharp`.

## Настройка Google Drive

1. Открой [Google Cloud Console](https://console.cloud.google.com/).
2. Создай проект или выбери существующий.
3. Включи **Google Drive API**: `APIs & Services` → `Library` → `Google Drive API` → `Enable`.
4. Настрой OAuth consent screen, если ещё не настроен.
5. Создай OAuth-клиент: `APIs & Services` → `Credentials` → `Create credentials` → `OAuth client ID` → тип **Desktop app**.
6. Скачай JSON и сохрани его как `credentials.json`.

Используемый scope — `drive.file`: приложение видит только файлы, которые создало само.

### Где ищется credentials.json

По порядку:

1. Явно указанный путь (сейчас CLI его не передаёт).
2. Переменная окружения `CREDENTIALS_ZIBRA`.
3. Переменная окружения `CREDENTIALS_ZIBRASHARP`.
4. `credentials.json` в текущей рабочей папке.
5. `config/credentials.json`, `configuration/credentials.json`, `settings/credentials.json`.
6. `~/.ZibraSharp/credentials.json`.

Проще всего — положить `credentials.json` в ту папку, из которой запускается утилита.

### Токен

При первой загрузке откроется браузер для согласия. Токен сохранится в `token.json` в текущей рабочей папке. На Unix файл получает права `0600` (только владелец).

## Важно: рабочая папка

Все рабочие файлы создаются **в текущей рабочей папке**, а не рядом с `.exe`:

- `zipdir.json`
- `extensions.json`
- `pending.json`
- `token.json`
- `zibra.log`

Поэтому запускай утилиту из постоянной папки, например `C:\ZibraSharp`, а не из `bin\Release\net9.0`.

Пример:

```powershell
cd C:\ZibraSharp
C:\ZibraSharp\app\zibrasharp.exe start C:\Data
```

## Все команды кратко

| Команда | Назначение |
|---|---|
| `zibrasharp start [archive.zip] <directory> [options]` | Создать архив и загрузить его |
| `zibrasharp settings [<path>] [--show]` | Настроить или показать папку для архивов |
| `zibrasharp extensions [<extensions...>] [--list] [--remove]` | Настроить исключаемые расширения |
| `zibrasharp log [--lines N]` | Показать последние строки лога |
| `zibrasharp pending [upload]` | Показать очередь или загрузить её |
| `zibrasharp version` / `zibrasharp --version` | Показать версию |
| `zibrasharp help` / `zibrasharp --help` | Показать справку |

Опции можно указывать после команды в любом порядке.

## Команда start

```text
zibrasharp start [archive.zip] <directory> [options]
```

Создаёт архив из `<directory>` и загружает его в Google Drive.

### Варианты вызова

| Команда | Что делает |
|---|---|
| `start C:\Data` | Имя архива генерируется автоматически: `Data-yyyyMMdd-HHmmss.zip` |
| `start backup.zip C:\Data` | Явное имя архива |
| `start C:\Data --dry-run` | Только сканирование: количество файлов и суммарный размер |
| `start C:\Data --force` | Перезаписать существующий архив |
| `start C:\Data --keep-archive` | Оставить архив после успешной загрузки |
| `start C:\Data --workers 8` | 8 потоков сжатия |
| `start C:\Data --yes` | Не спрашивать про очередь pending |

### Опции start

| Опция | Алиас | Описание |
|---|---|---|
| `--yes` | `-y` | Загрузить pending-архивы без вопроса |
| `--dry-run` | | Сканирование без архивации; не требует `zipdir.json` и `credentials.json` |
| `--force` | `-f` | Разрешить перезапись существующего архива |
| `--keep-archive` | | Не удалять архив после успешной загрузки |
| `--workers N` | | Число потоков сжатия; `0` (по умолчанию) — по числу CPU |

Также поддерживается форма `--workers=N`.

### Поведение

1. Проверяет, что папка существует.
2. Проверяет `zipdir.json` (кроме `--dry-run`).
3. Проверяет `credentials.json` (кроме `--dry-run`).
4. Проверяет, не существует ли уже архив; без `--force` завершается с подсказкой.
5. Предлагает загрузить очередь pending (`--yes` — без вопроса).
6. Сканирует папку и создаёт архив с прогрессом.
7. Загружает архив.
8. При успехе удаляет архив, если не указан `--keep-archive`.
9. При ошибке загрузки оставляет архив и добавляет его в `pending.json`.

### Коды результата

- `0` — архив создан и загружен.
- `1` — ошибка конфигурации, валидации или архивации.
- `2` — архив создан, но загрузка не удалась; архив сохранён в очереди.
- `130` — отменено Ctrl+C.

## Команда settings

```text
zibrasharp settings
zibrasharp settings <path>
zibrasharp settings --show
```

| Команда | Что делает |
|---|---|
| `settings` | Интерактивно спрашивает путь |
| `settings C:\Backups` | Сохраняет путь без вопросов |
| `settings --show` | Показывает текущую папку и исключённые расширения |

Путь должен существовать. Утилита не создаёт папку для архивов автоматически.

## Команда extensions

Расширения — это **исключения** из сканирования. Файлы без расширения всегда включаются.

```text
zibrasharp extensions
zibrasharp extensions .txt .exe .log
zibrasharp extensions --list
zibrasharp extensions --remove
```

| Команда | Что делает |
|---|---|
| `extensions` | Интерактивный ввод |
| `extensions .txt .exe` | Сохраняет список без вопросов |
| `extensions --list` | Показывает текущие исключения |
| `extensions --remove` | Удаляет файл расширений |

Алиас `--remove`: `-r`.

Расширения разделяются пробелами. Запятые запрещены. Поддерживаются составные расширения, например `.tar.gz`.

## Команда log

```text
zibrasharp log
zibrasharp log --lines 200
zibrasharp log -n 20
```

Показывает последние N строк текущего `zibra.log` (по умолчанию 50). Форма `--lines=N` тоже поддерживается.

Если лог ещё не создавался, выводит:

```text
Log file does not exist yet.
```

## Команда pending

```text
zibrasharp pending
zibrasharp pending upload
```

| Команда | Что делает |
|---|---|
| `pending` | Показывает архивы, ожидающие загрузки |
| `pending upload` | Пробует загрузить все архивы из очереди |

`pending upload` возвращает `2`, если хотя бы один архив остался в очереди.

## Команды version и help

```text
zibrasharp version
zibrasharp --version
zibrasharp help
zibrasharp --help
zibrasharp -h
```

Пример:

```text
zibrasharp 1.0.0
```

## Глобальные флаги

| Флаг | Описание |
|---|---|
| `--verbose` | Включает debug-логирование |

Пример:

```powershell
zibrasharp start C:\Data --verbose
```

## Коды выхода

| Код | Значение |
|---|---|
| `0` | Успех |
| `1` | Ошибка конфигурации, валидации или неожиданная ошибка |
| `2` | Архив создан, но загрузка не удалась; архив в очереди |
| `130` | Отменено Ctrl+C |

## Рабочие файлы

Все файлы создаются в текущей рабочей папке, если не указано иное.

| Файл | Назначение |
|---|---|
| `credentials.json` | OAuth-клиент Google (не коммитить) |
| `token.json` | Токен доступа; создаётся автоматически |
| `zipdir.json` | Папка для архивов |
| `extensions.json` | Исключаемые расширения |
| `extentions.json` | Старое имя файла расширений; читается как fallback |
| `pending.json` | Очередь архивов на загрузку |
| `zibra.log` | JSON-лог |
| `zibra.1.log` … `zibra.5.log` | Ротированные логи |

`credentials.json`, `token.json`, `zipdir.json`, `extensions.json`, `pending.json`, `*.zip` и `*.log` добавлены в `.gitignore`.

## Офлайн-режим и очередь загрузки

`LazyGoogleDriveApi` инициализирует OAuth и сетевой клиент только при первой загрузке. Поэтому:

1. Архив создаётся всегда первым.
2. Если облако недоступно, архив **не удаляется**.
3. Его путь записывается в `pending.json`.
4. Следующий `start` или `pending upload` повторяет загрузку.

Это защищает от потери данных при проблемах с сетью.

## Логирование

Лог пишется в формате JSON Lines — один объект на строку:

```json
{"time":"2026-11-08T12:00:00.0000000+00:00","level":"INFO","msg":"Files found","Count":128}
```

- Уровни: `DEBUG`, `INFO`, `WARN`, `ERROR`.
- Для ошибок сохраняются тип исключения, сообщение, stack trace и вложенные исключения.
- Файл создаётся лениво, при первом событии.
- Ротация: при достижении 10 МБ; хранится 5 ротированных файлов.
- Просмотр: `zibrasharp log --lines 200`.

## Разработка

Сборка:

```powershell
dotnet build .\ZibraSharp\ZibraSharp.sln
```

Тесты:

```powershell
dotnet test .\ZibraSharp.Tests\ZibraSharp.Tests.csproj
```

Release-сборка и тесты:

```powershell
dotnet build .\ZibraSharp\ZibraSharp.sln -c Release
dotnet test .\ZibraSharp.Tests\ZibraSharp.Tests.csproj -c Release --no-build
```

## Бенчмарки

Запустить все бенчмарки:

```powershell
dotnet run --project .\ZibraSharp.Benchmarks\ZibraSharp.Benchmarks.csproj -c Release
```

Показать список бенчмарков:

```powershell
dotnet run --project .\ZibraSharp.Benchmarks\ZibraSharp.Benchmarks.csproj -c Release -- --list flat
```

Запустить конкретный набор:

```powershell
dotnet run --project .\ZibraSharp.Benchmarks\ZibraSharp.Benchmarks.csproj -c Release -- --filter *ZipArchiverBenchmarks*
```

Быстрая проверка без полноценных замеров:

```powershell
dotnet run --project .\ZibraSharp.Benchmarks\ZibraSharp.Benchmarks.csproj -c Release -- --filter *ZipArchiverBenchmarks* --job dry
```

Что измеряется:

- архиватор при 1 и 4 воркерах;
- рекурсивный обход папки;
- парсинг списка расширений;
- запись 100 JSON-событий;
- атомарная запись файла.

## Структура проекта

```text
ZibraSharp/
  CLI/                     Командная строка и разбор аргументов
  Internal/
    Api/                   Google Drive, credentials, token store
    Archivers/             Параллельный ZIP-writer и CRC32
    Config/                zipdir.json и extensions.json
    Exceptions/            AppException и коды ошибок
    IO/                    Атомарная запись файлов
    Logger/                JSON-логгер с ротацией
    Ports/                 Интерфейсы и общие record-типы
    Services/              Сценарий backup, pending-загрузка, очередь
    Walkers/               Рекурсивный сканер папок
ZibraSharp.Tests/          Тесты xUnit
ZibraSharp.Benchmarks/     Бенчмарки BenchmarkDotNet
```

## Безопасность

- Никогда не коммить `credentials.json` и `token.json`.
- Токен — секрет; на Unix он записывается с правами `0600`.
- Архивы загружаются в аккаунт Drive, который авторизовал токен. Перед боевым запуском проверь аккаунт.
- Scope `drive.file` даёт доступ только к файлам, созданным приложением.

## Решение проблем

### `zibrasharp` не распознаётся в PowerShell

PowerShell не запускает программы из текущей папки по имени. Используй:

```powershell
.\zibrasharp.exe
```

или добавь папку в `PATH` (см. раздел «Установка»).

### `credentials.json not found`

Проверь, что файл лежит в текущей рабочей папке или путь задан через `CREDENTIALS_ZIBRA`:

```powershell
$env:CREDENTIALS_ZIBRA = "C:\ZibraSharp\credentials.json"
```

Также проверь, что JSON — это OAuth-клиент типа **Desktop app**, а Google Drive API включён.

### `Backup directory is not configured`

Выполни:

```powershell
zibrasharp settings C:\Backups
```

Папка должна существовать.

### `Archive already exists`

Укажи другое имя или добавь `--force`:

```powershell
zibrasharp start C:\Data --force
```

### `Operation cancelled`

Нажат Ctrl+C. Архив, который не успел создаться, удаляется; существующий архив не повреждается.

### Браузер не открывается при OAuth

Первый запуск требует браузер для согласия. На headless-сервере авторизацию нужно выполнить на машине с браузером и перенести `token.json`.

### Загрузка не удалась

Архив останется на диске и попадёт в очередь. Проверь:

```powershell
zibrasharp pending
zibrasharp log --lines 100
```

Повторить загрузку:

```powershell
zibrasharp pending upload
```

### `Unknown option`

Проверь написание флага. Список всех флагов — в разделе [Все команды кратко](#все-команды-кратко).
