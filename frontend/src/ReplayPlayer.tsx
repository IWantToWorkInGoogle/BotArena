import { useEffect, useMemo, useState } from 'react';
import type { Replay } from './contracts';
import { BlueprintArena } from './BlueprintArena';
import type { CameraMode } from './BlueprintArena';
import { buildShotPlan, directorPlaybackRate, shotAt } from './cinematicDirector';
import { text } from './text';

const colors = { player: '#53e7ff', opponent: '#ffad58' };
const copy = text.replay;
const speeds = [0.5, 1, 2, 4];
const cameraLabels: Record<CameraMode, string> = {
  auto: 'Кино',
  adaptive: 'Разрез',
  plan: 'План',
  overview: 'Сверху',
  player: 'Мой бот',
  opponent: 'Противник',
};

export function ReplayPlayer({ replay }: { replay: Replay }) {
  const [position, setPosition] = useState(0);
  const [playing, setPlaying] = useState(true);
  const [speed, setSpeed] = useState(1);
  const [cameraMode, setCameraMode] = useState<CameraMode>('adaptive');
  const [showTrails, setShowTrails] = useState(true);
  const shotPlan = useMemo(() => buildShotPlan(replay), [replay]);
  const last = replay.frames.length - 1;
  const finished = position >= last;

  useEffect(() => {
    if (!playing || finished) return;
    let animation: number;
    let previous: number | undefined;

    function advance(now: number) {
      const elapsed = previous === undefined ? 0 : now - previous;
      previous = now;
      setPosition(current => {
        const cinematicRate = cameraMode === 'auto' || cameraMode === 'adaptive'
          ? directorPlaybackRate(shotPlan, current) : 1;
        return Math.min(last, current + elapsed * replay.tickRate * speed * cinematicRate / 1000);
      });
      animation = requestAnimationFrame(advance);
    }

    animation = requestAnimationFrame(advance);
    return () => cancelAnimationFrame(animation);
  }, [playing, finished, last, replay.tickRate, speed, cameraMode, shotPlan]);

  const index = Math.floor(position);
  const frame = replay.frames[index];
  const next = replay.frames[Math.min(last, index + 1)];
  const winner = replay.result.winnerBotId;
  const outcome = winner ? copy.winner(copy[winner]) : copy.draw;
  const reason = !winner && replay.result.reason === 'BotDestroyed'
    ? copy.doubleKnockout : copy.reason[replay.result.reason];
  const activeShot = shotAt(shotPlan, position);
  const cinematicFocus = (cameraMode === 'auto' || cameraMode === 'adaptive')
    && (activeShot?.kind === 'projectile' || activeShot?.kind === 'impact' || activeShot?.kind === 'finale');
  const shotNumber = activeShot ? shotPlan.shots.indexOf(activeShot) + 1 : 0;

  return (
    <>
      <div className={`arena-stage blueprint-stage${cinematicFocus ? ' cinematic-focus' : ''}`}>
        <BlueprintArena replay={replay} frame={frame} next={next} fraction={position - index}
          position={position} cameraMode={cameraMode} showTrails={showTrails} shotPlan={shotPlan} />
        <div className="blueprint-hud" aria-hidden="true">
          <span>{(cameraMode === 'auto' || cameraMode === 'adaptive') && activeShot
            ? `${cameraMode === 'adaptive' ? 'SECTION DIRECTOR' : 'CINEMA DIRECTOR'} / ${activeShot.label}`
            : 'BA–25 / REPLAY VISUALIZER'}</span>
          <span>{String(frame.tick).padStart(3, '0')}F</span>
        </div>
        {(cameraMode === 'auto' || cameraMode === 'adaptive') && activeShot && (
          <div className="director-cue" aria-hidden="true">
            <span>CUT {String(shotNumber).padStart(2, '0')} / {String(shotPlan.shots.length).padStart(2, '0')}</span>
            <strong>{activeShot.label}</strong>
            <em>{cameraMode === 'adaptive'
              ? `DYNAMIC SECTION${activeShot.playbackRate < 1 ? ` · ${activeShot.playbackRate.toFixed(2)}×` : ''}`
              : activeShot.playbackRate < 1
              ? `${activeShot.playbackRate.toFixed(2)}× TIME` : 'CINEMA MODE'}</em>
          </div>
        )}
        <div className="blueprint-reticle" aria-hidden="true"><i /><i /></div>
        {finished && <p role="status" className="outcome">{copy.outcome(outcome, reason)}</p>}
      </div>
      <div className="director-bar">
        <div className="camera-modes" role="group" aria-label="Режим камеры">
          {(Object.keys(cameraLabels) as CameraMode[]).map(mode => (
            <button type="button" key={mode} className={cameraMode === mode ? 'active' : undefined}
              aria-pressed={cameraMode === mode} onClick={() => setCameraMode(mode)}>
              {(mode === 'auto' || mode === 'adaptive') && <span className="live-dot" />}
              {cameraLabels[mode]}
            </button>
          ))}
        </div>
        <label className="trail-toggle">
          <input type="checkbox" checked={showTrails}
            onChange={event => setShowTrails(event.target.checked)} />
          Траектории
        </label>
      </div>
      <div className="controls replay-controls">
        <button type="button" onClick={() => {
          if (finished) setPosition(0);
          setPlaying(finished || !playing);
        }}>{finished ? copy.restart : playing ? copy.pause : copy.resume}</button>
        <input aria-label={copy.position} type="range" min="0" max={last} step="0.01" value={position} onChange={event => {
          setPlaying(false);
          setPosition(Number(event.target.value));
        }} />
        <output>{copy.time(position / replay.tickRate, last / replay.tickRate)}</output>
        <select aria-label={copy.speed} value={speed} onChange={event => setSpeed(Number(event.target.value))}>
          {speeds.map(value => <option key={value} value={value}>{copy.speedOption(value)}</option>)}
        </select>
      </div>
      <ul className="health" aria-label={copy.health}>
        {frame.bots.map(bot => (
          <li key={bot.id} style={{ color: colors[bot.id] }}>
            <span>{copy[bot.id]}</span>
            <meter aria-label={copy[bot.id]} min={0} max={bot.maxHp} value={bot.hp} />
            <b>{copy.hp(bot.hp, bot.maxHp)}</b>
          </li>
        ))}
      </ul>
    </>
  );
}
