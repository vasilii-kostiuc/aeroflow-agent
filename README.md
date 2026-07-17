# aeroflow-agent

Локальный аудио-агент AeroFlow: исполняет команды воспроизведения от
`aeroflow-playback` и сообщает результат. Не содержит бизнес-логику аэропорта —
видит только `jobId` и нейтральную аудиопоследовательность (задача 016).

```text
agent_commands (RabbitMQ) -> PlaySequenceCommand
  -> download/кэш файлов по audioAssetId (эндпоинт aeroflow-core)
  -> последовательное воспроизведение (системный плеер)
  -> agent_events: audio.playback_started / _completed / _failed
```

## Стек

C# / .NET Worker Service. Агент рассчитан на on-prem машину у аудиовыхода
(Windows или Linux); публикуется self-contained бинарником.

## Запуск

```bash
dotnet run --project src/AeroFlow.Agent
```

Конфигурация — `appsettings.json`, секция `Agent`; любое поле переопределяется
переменной окружения с префиксом `AGENT_` (формат .NET: `AGENT_Agent__Simulate=true`).

| Ключ | Назначение | По умолчанию |
| --- | --- | --- |
| `AmqpUri` | RabbitMQ (тот же брокер, что у core/playback) | `amqp://guest:guest@localhost:5672/` |
| `CommandsQueue` / `EventsQueue` | очереди команд и статусов | `agent_commands` / `agent_events` |
| `CoreBaseUrl` | базовый URL core для скачивания аудио | `http://localhost:8080/` |
| `CacheDir` | локальный кэш файлов и маркеры дедупликации | `var/cache` |
| `PlayerCommand` | команда плеера, `{file}` — путь к файлу | `ffplay -nodisp -autoexit -loglevel error {file}` |
| `MaxCommandAgeSeconds` | команды старше порога не играются, а фейлятся как `stale command` (защита от бэклога durable-очереди, пока агент был выключен) | `300` |
| `Simulate` | режим без звука: лог + пауза (docker/CI) | `false` |

## Тесты

```bash
dotnet test
```

## Ограничения первого среза

* нет `Stop/Pause/Resume/SetVolume` — придут с emergency mode;
* дедупликация повторной команды best-effort (без БД по architecture.md);
  строгая идемпотентность — выше, в playback;
* один агент, один audio output.
