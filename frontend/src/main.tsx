import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { createRoot } from 'react-dom/client';
import type { MatchResponse, Opponent, Replay } from './contracts';
import { ReplayPlayer } from './ReplayPlayer';
import './style.css';

const example = `function tick({ self, enemy }) {
  const { x, y } = self.position;
  // Пока цель скрыта, объезжаем стену сверху.
  const waypoint = enemy?.position ??
    (y > 3.65 ? { x: 4.1, y: 3.45 } : { x: 12, y: 3.5 });
  const dx = waypoint.x - x;
  const dy = waypoint.y - y;
  const heading = Math.atan2(dy, dx);
  const wrap = a => Math.atan2(Math.sin(a), Math.cos(a));
  const error = wrap(heading - self.bodyAngle);
  const turn = Math.max(-1, Math.min(1, error * 2));
  const far = !enemy || Math.hypot(dx, dy) > 5;
  const drive = far && Math.abs(error) < 0.55 ? 1 : 0;
  return {
    leftTrack: drive + turn,
    rightTrack: drive - turn,
    aimDirection: { x: dx, y: dy },
    shoot: !!enemy && self.canShoot &&
      Math.abs(wrap(heading - self.turretAngle)) < 0.045
  };
}`;

function App() {
  const [code, setCode] = useState(example);
  const [opponents, setOpponents] = useState<Opponent[]>([]);
  const [opponentId, setOpponentId] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [replay, setReplay] = useState<Replay>();

  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      try {
        const response = await fetch('/api/opponents', { signal: controller.signal });
        if (!response.ok) throw new Error();
        const choices: Opponent[] = await response.json();
        setOpponents(choices);
        setOpponentId(choices[0]?.id ?? '');
      } catch {
        if (!controller.signal.aborted) setError('Не удалось загрузить противников. Проверьте, запущен ли сервер, и обновите страницу.');
      }
    }
    void load();
    return () => controller.abort();
  }, []);

  async function run(event: FormEvent) {
    event.preventDefault();
    setLoading(true);
    setError('');
    setReplay(undefined);
    try {
      const response = await fetch('/api/matches', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ botCode: code, opponentId }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        const tick = typeof problem?.tick === 'number' ? ` Тик ${problem.tick}.` : '';
        throw new Error((problem?.error ?? `Ошибка сервера (${response.status}).`) + tick);
      }
      const match: MatchResponse = await response.json();
      setReplay(match.replay);
      if (match.error) {
        const bot = match.error.botId === 'player' ? 'Ваш бот' : 'Противник';
        const tick = match.error.tick === null ? '' : ` Тик ${match.error.tick}.`;
        setError(`${bot}. ${match.error.message}${tick}`);
      }
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Не удалось запустить матч.');
    } finally {
      setLoading(false);
    }
  }

  const opponent = opponents.find(item => item.id === opponentId);

  return (
    <main>
      <header><span>● BotArena</span><span className="muted">Match Lab / дуэль 1×1</span></header>
      <div className="layout">
        <form className="panel editor" onSubmit={run}>
          <h1>Стратегия бота</h1>
          <label htmlFor="strategy">JavaScript · tick(state)</label>
          <textarea id="strategy" value={code} onChange={event => setCode(event.target.value)} spellCheck={false} maxLength={16384} />
          <details>
            <summary>Как управлять ботом</summary>
            <p><code>state</code> содержит <code>self</code>, <code>enemy</code> (или <code>null</code> за стеной), <code>bullets</code>, <code>tick</code> и <code>arena</code>.</p>
            <p>Верните <code>leftTrack</code> и <code>rightTrack</code> от −1 до 1, <code>aimDirection: &#123; x, y &#125;</code> и <code>shoot</code>. Ось Y направлена вниз.</p>
          </details>
          <label htmlFor="opponent">Противник</label>
          <select id="opponent" value={opponentId} onChange={event => setOpponentId(event.target.value)}>
            {opponents.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
          {opponent && <p className="muted">{opponent.description}</p>}
          <button className="primary" disabled={loading || !opponentId || !code.trim()}>
            {loading ? 'Рассчитываем матч…' : 'Запустить матч'}
          </button>
          {error && <p role="alert" className="error">{error}</p>}
        </form>
        <section className="panel replay">
          <h2>Арена</h2>
          {replay ? <ReplayPlayer replay={replay} /> :
            <div className="empty">Запустите матч, чтобы увидеть бой.</div>}
        </section>
      </div>
    </main>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
