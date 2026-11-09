# BotArena

Учебная платформа для боёв программируемых танков. Напишите стратегию на JavaScript, выберите встроенного противника и запустите дуэль. Сервер C# полностью рассчитает бой, а интерфейс React воспроизведёт его по кадрам.

## Локальный запуск

Нужны .NET SDK 10, Node.js 22.12+ и npm.

```sh
npm --prefix frontend ci
npm --prefix frontend run build
dotnet run --project server
```

Откройте [http://127.0.0.1:5050](http://127.0.0.1:5050). Для разработки фронтенда запустите `npm --prefix frontend run dev` в отдельном терминале с работающим сервером и откройте [http://127.0.0.1:5173](http://127.0.0.1:5173).

## Стратегия

Редактор содержит готовую стратегию. Минимальный бот остаётся на месте и ведёт огонь по видимой цели:

```js
function tick({ self, enemy }) {
  if (!enemy) return {};
  const x = enemy.position.x - self.position.x;
  const y = enemy.position.y - self.position.y;
  const error = Math.atan2(y, x) - self.turretAngle;
  return {
    aimDirection: { x, y },
    shoot: self.canShoot && Math.abs(Math.atan2(Math.sin(error), Math.cos(error))) < 0.05
  };
}
```

`leftTrack` и `rightTrack` задают тягу гусениц в диапазоне от −1 до 1. Пропущенные команды эквивалентны нулевой тяге, сохранению направления башни и отсутствию выстрела. Состояние JavaScript сохраняется между тиками одного матча. `enemy` равен `null`, когда между центрами танков стоит стена.

## Проверки

```sh
dotnet build server
dotnet test tests
npm --prefix frontend run build
# При запущенном сервере
python3 scripts/smoke.py
python3 -B scripts/validate_strategies.py
python3 -B scripts/validate_queue.py
```

Готовые функции для ручной проверки и ожидаемое поведение описаны в [проверочных стратегиях](scripts/strategies/README.md).

Архитектура, контракт HTTP и согласованные допущения описаны в [контексте проекта](docs/project.md).

Текст интерфейса редактируется в [frontend/src/text.ts](frontend/src/text.ts). Диагностика движка приходит из API.
