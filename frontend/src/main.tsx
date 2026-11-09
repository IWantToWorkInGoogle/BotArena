import { lazy, Suspense, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { createRoot } from 'react-dom/client';
import type { MatchJobStatus, Opponent, Replay } from './contracts';
import { ReplayPlayer } from './ReplayPlayer';
import { runMatch } from './matchApi';
import { StrategyHelp } from './StrategyHelp';
import { text } from './text';
import './style.css';

const StrategyEditor = lazy(() => import('./StrategyEditor'));

const example = `function tick({ self, enemy }) {
  const { x, y } = self.position;
  // ${text.strategyComment}
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
  const [jobStatus, setJobStatus] = useState<MatchJobStatus>();
  const activeRun = useRef<AbortController | null>(null);
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
        if (!controller.signal.aborted) setError(text.opponentsUnavailable);
      }
    }
    void load();
    return () => {
      controller.abort();
      activeRun.current?.abort();
    };
  }, []);

  async function run(event: FormEvent) {
    event.preventDefault();
    if (activeRun.current) return;
    const controller = new AbortController();
    activeRun.current = controller;
    setLoading(true);
    setJobStatus(undefined);
    setError('');
    setReplay(undefined);
    try {
      const match = await runMatch(code, opponentId, controller.signal, setJobStatus);
      setReplay(match.replay);
      if (match.error) {
        const { botId, message, tick } = match.error;
        setError(text.scriptError(text.replay[botId], message, tick));
      }
    } catch (problem) {
      if (!controller.signal.aborted) {
        setError(problem instanceof TypeError ? text.networkError :
          problem instanceof Error ? problem.message : text.matchError);
      }
    } finally {
      activeRun.current = null;
      setLoading(false);
      setJobStatus(undefined);
    }
  }

  function opponentText(opponent: Opponent) {
    return Object.hasOwn(text.opponents, opponent.id)
      ? text.opponents[opponent.id as keyof typeof text.opponents] : opponent;
  }

  const opponent = opponents.find(item => item.id === opponentId);

  return (
    <main>
      <header><span>{text.brand}</span><span className="muted">{text.mode}</span></header>
      <div className="layout">
        <form className="panel editor" onSubmit={run}>
          <h1>{text.strategy}</h1>
          <span className="muted" id="strategy-label">{text.strategyLanguage}</span>
          <Suspense fallback={<div className="code-editor" />}>
            <StrategyEditor initialCode={example} onChange={setCode} />
          </Suspense>
          <StrategyHelp />
          <label htmlFor="opponent">{text.opponent}</label>
          <select id="opponent" value={opponentId} onChange={event => setOpponentId(event.target.value)}>
            {opponents.map(item => <option key={item.id} value={item.id}>{opponentText(item).name}</option>)}
          </select>
          {opponent && <p className="muted">{opponentText(opponent).description}</p>}
          <button className="primary" disabled={loading || !opponentId || !code.trim() || code.length > 16384}>
            {loading ? text.working : text.start}
          </button>
          {code.length > 16384 && <p role="alert" className="error">{text.strategyTooLong}</p>}
          {jobStatus?.state === 'queued' && <p role="status">{text.queuePosition(jobStatus.position)}</p>}
          {error && <p role="alert" className="error">{error}</p>}
        </form>
        <section className="panel replay">
          <h2>{text.arena}</h2>
          {replay ? <ReplayPlayer replay={replay} /> :
            <div className="empty">{text.emptyArena}</div>}
        </section>
      </div>
    </main>
  );
}

document.title = text.title;
document.documentElement.lang = text.language;
createRoot(document.getElementById('root')!).render(<App />);
