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

Конфигурация — `appsettings.json`, секция `Agent`. Поверх него читается
`appsettings.{окружение}.json` (`Development` при `dotnet run`, иначе
`Production`), а поверх файлов — переменные окружения с префиксом `AGENT_`
(формат .NET: `AGENT_Agent__Simulate=true`).

| Ключ | Назначение | По умолчанию |
| --- | --- | --- |
| `AmqpUri` | RabbitMQ (тот же брокер, что у core/playback); `amqps://` — с TLS | `amqp://guest:guest@localhost:5672/` |
| `BrokerCaCertPath` | `ca.crt` частного CA брокера: цепочка проверяется только по нему; пусто — системное хранилище. Имя сервера проверяется всегда | пусто |
| `CommandsQueue` / `EventsQueue` | очереди команд и статусов | `agent_commands` / `agent_events` |
| `CoreBaseUrl` | базовый URL core для скачивания аудио | `http://localhost:8080/` |
| `CoreToken` | токен агента для скачивания (`AGENT_TOKEN` core), заголовок `Authorization: Bearer` | пусто; в `appsettings.Development.json` — `dev-agent-token` |
| `CacheDir` | локальный кэш файлов и маркеры дедупликации | `var/cache` |
| `PlayerCommand` | команда плеера, `{file}` — путь к файлу | `ffplay -nodisp -autoexit -loglevel error {file}` |
| `MaxCommandAgeSeconds` | команды старше порога не играются, а фейлятся как `stale command` (защита от бэклога durable-очереди, пока агент был выключен) | `300` |
| `Simulate` | режим без звука: лог + пауза (docker/CI) | `false` |

## Подключение к серверу

Агент за NAT подключается к серверу сам (задача 038): `amqps` на 5671 под
пользователем брокера `aeroflow-agent` и `https` к `/internal` core с токеном.
Настройки подключения, включая пароль и токен, лежат в
`appsettings.Production.json` в папке агента. Этого файла нет в git и в сборке,
поэтому обновление агента его не затирает. Значения и `ca.crt` — в
`deploy/README.md` корневого репозитория.

Если брокер недоступен при старте, агент повторяет подключение каждые 5 с;
разорванное соединение клиент восстанавливает сам. Итог воспроизведения
(`completed`/`failed`/`interrupted`) агент повторяет, пока брокер его не примет.

## Тесты

```bash
dotnet test
```

## Ограничения первого среза

* нет `Stop/Pause/Resume/SetVolume` — придут с emergency mode;
* дедупликация повторной команды best-effort (без БД по architecture.md);
  строгая идемпотентность — выше, в playback;
* один агент, один audio output.
