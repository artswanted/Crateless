# Crateless — реестр расхождений с upstream

Что мы намеренно меняем относительно [seerge/g-helper](https://github.com/seerge/g-helper),
почему, и как мёржить upstream не ломая это.

Базовая ревизия на момент заведения форка: `929dd33b` (AssemblyVersion 0.284).

---

## Главный принцип

> **Ядро железа не форкаем.**

`AsusACPI.cs`, `HardwareControl.cs`, `Pawn/`, `Gpu/`, `Fan/`, `USB/`, `Peripherals/`,
`AnimeMatrix/` — территория upstream. Там идёт непрерывный реверс-инжиниринг под новые
модели ASUS, 4300+ коммитов именно поэтому. Любая наша правка в этих файлах превращается
в вечный конфликт при каждом мёрже.

Меняем только: идентичность приложения, автообновление, сборку/релизы и `UI/`.

### Зоны ответственности

| Каталог / файл | Владелец | Правим? |
|---|---|---|
| `app/AsusACPI.cs`, `app/HardwareControl.cs` | upstream | нет |
| `app/Pawn/`, `app/Gpu/`, `app/Fan/`, `app/USB/` | upstream | нет |
| `app/Peripherals/`, `app/AnimeMatrix/`, `app/Ally/` | upstream | нет |
| `app/Mode/` | upstream | нет (логика уже правильная) |
| `app/Helpers/` | общий | точечно (см. §5) |
| `app/UI/` | **наш** | да, фаза 3 |
| `app/AutoUpdate/` | **наш** | да, обязательно |
| `app/GHelper.csproj` | общий | да, точечно |
| `.github/workflows/` | **наш** | да |
| `docs/README*.md` | upstream | нет — свой README в корне |
| `docs/crateless/` | **наш** | да |
| корневой `README.md` | **наш** | да (у upstream его нет → конфликтов не будет) |

---

## Реестр расхождений

Статус: ⬜ не сделано · ✅ сделано

### 1. Автообновление — критично ✅

**Риск: без этой правки приложение скачает релиз upstream и перезапишет наш билд.**

| | |
|---|---|
| Файл | `app/AutoUpdate/AutoUpdateControl.cs` |
| Строка 15 | `versionUrl = "https://github.com/seerge/g-helper/releases"` |
| Строка 77 | `https://api.github.com/repos/seerge/g-helper/releases/latest` |
| Стало | `artswanted/Crateless` в обоих местах |
| Риск мёржа | **высокий** — upstream активно правит этот файл |

### 2. Имя сборки ✅

| | |
|---|---|
| Файл | `app/GHelper.csproj`, строка 15 |
| Было | `<AssemblyName>GHelper</AssemblyName>` |
| Стало | `<AssemblyName>Crateless</AssemblyName>` |
| Риск мёржа | низкий |

`<StartupObject>GHelper.Program</StartupObject>` (строка 11) **не трогаем** — namespace
остаётся `GHelper`, см. решение в [PLAN.md §4](PLAN.md#4-принятые-решения).

### 3. Версия ✅

| | |
|---|---|
| Файл | `app/GHelper.csproj`, строка 22 |
| Было | `<AssemblyVersion>0.284</AssemblyVersion>` (версия upstream) |
| Стало | своя нумерация с `0.1.0` |
| Риск мёржа | **высокий** — upstream бампает при каждом релизе, конфликт гарантирован каждый раз |

Разрешать всегда в свою пользу. Записывать в `docs/crateless/BASELINE.md`, на какой
ревизии upstream основана наша версия.

### 4. Иконка ✅

| | |
|---|---|
| Файл | `app/GHelper.csproj`, строка 12 + `app/favicon.ico` |
| Стало | `app/Crateless.ico` из `Crateless-Design-Kit-Nebula-v2/assets/brand/` |
| Риск мёржа | низкий (бинарник, конфликт разрешается выбором нашего) |

### 5. Имя события single-instance ✅

| | |
|---|---|
| Файл | `app/Helpers/ProcessHelper.cs`, строка 9 |
| Было | `private const string ExitEventName = "Global\\GHelperApp-Exit";` |
| Стало | `Global\\CratelessApp-Exit` |
| Зачем | чтобы Crateless и стоковый G-Helper могли работать рядом и не убивали друг друга |
| Риск мёржа | низкий |

Связано: `app/GHelper.csproj`, строки 83–89 — target-ы `KillRunningGHelper` и
`ZipSingleExe` ссылаются на это же событие и на имя процесса `GHelper`. Обновить вместе.

### 6. Путь конфигурации ✅

| | |
|---|---|
| Файл | `app/AppConfig.cs`, строки 30 и 33 |
| Было | `%APPDATA%\GHelper` и `%PROGRAMDATA%\GHelper` |
| Стало | `%APPDATA%\Crateless` и `%PROGRAMDATA%\Crateless` |
| Риск мёржа | низкий |

Тем же коммитом: `app/Helpers/Logger.cs` (папка логов), `app/AnimeMatrix/MatrixFont.cs` (шрифт матрицы),
`app/Helpers/Startup.cs` (имя задачи планировщика `Crateless`, чтобы не делить задачу со стоковым G-Helper).

Открытый вопрос: импортировать существующий конфиг G-Helper при первом запуске
([PLAN.md §6](PLAN.md#6-открытые-вопросы), вопрос 2).

### 7. Релизный workflow ✅

| | |
|---|---|
| Файл | `.github/workflows/release.yml` |
| Проблема | шаги 40–72 используют SignPath (`SIGNPATH_API_TOKEN`, `SIGNPATH_ORG_ID`, `SIGNPATH_PROJECT`, `SIGNPATH_POLICY`) — этих секретов у форка нет, workflow упадёт |
| Стало | подпись убрана; публикуются `Crateless.exe` (framework-dependent) и `Crateless-standalone.exe` (self-contained) + zip |
| Риск мёржа | средний |

Шаг публикации оставляем как есть, он корректный:
```
dotnet publish app/GHelper.sln --configuration Release --runtime win-x64 \
  -p:PublishSingleFile=true --no-self-contained
```

### 8. Ссылки на вики upstream ⬜

| | |
|---|---|
| Файл | `app/Extra.cs`, строка 848 |
| Что | ссылка на `seerge/g-helper/wiki/Power-user-settings` |
| Решение | **оставить как есть** пока своей вики нет — документация актуальна и полезна |
| Риск мёржа | нулевой (не трогаем) |

---

## Процедура мёржа upstream

Раз в 2–4 недели, либо когда в upstream выходит нужный фикс.

```bash
cd /d/GitHub/Crateless
git fetch upstream
git log --oneline HEAD..upstream/main          # что прилетит
git checkout -b merge/upstream-$(date +%Y%m%d)
git merge upstream/main
```

Ожидаемые конфликты и как их решать:

| Файл | Решение |
|---|---|
| `app/GHelper.csproj` — `AssemblyVersion` | наша версия |
| `app/GHelper.csproj` — `AssemblyName`, иконка, build-target-ы | наши |
| `app/GHelper.csproj` — `PackageReference`, `EmbeddedResource` | **их** (новые зависимости нужны) |
| `app/AutoUpdate/AutoUpdateControl.cs` | их логика + наши URL |
| `app/Helpers/ProcessHelper.cs` | их логика + наше имя события |
| `app/AppConfig.cs` | их логика + наши пути |
| `.github/workflows/release.yml` | наш |
| `app/UI/**` (после фазы 3) | наш, их изменения переносить вручную |
| всё остальное | **их, без раздумий** |

После мёржа:

```bash
dotnet build app/GHelper.sln -c Release
# прогнать на железе: переключение режимов, вентиляторы, GPU-режимы
git checkout main && git merge --no-ff merge/upstream-YYYYMMDD
```

Проверка, что ничего не забыли — все места должны указывать на Crateless:

```bash
git grep -n "seerge/g-helper" -- app/AutoUpdate    # должно быть пусто
git grep -n "GHelperApp-Exit"                       # должно быть пусто
git grep -n "AssemblyName" -- app/GHelper.csproj    # Crateless
```

---

## Обязательства GPL-3.0

Как производная работа Crateless обязан:

1. Остаться под GPL-3.0, файл `LICENSE` не трогать.
2. Сохранить копирайты и атрибуцию upstream — сделано в корневом `README.md`.
3. Обозначать, что это модифицированная версия, и указывать характер изменений —
   этот документ и есть такое обозначение.
4. Публиковать исходники любого распространяемого бинарника.

Товарные знаки: «ASUS», «ROG», «TUF», «Armoury Crate» принадлежат ASUSTeK Computer Inc.
В имени продукта они не используются — только номинативно в описании. Дисклеймер в
корневом `README.md`.
