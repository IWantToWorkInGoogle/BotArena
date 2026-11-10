import { useEffect, useMemo, useState } from 'react';
import type { Replay } from './contracts';
import { BlueprintArena } from './BlueprintArena';
import type { CameraMode } from './BlueprintArena';
import { buildShotPlan, directorPlaybackRate, shotAt, VISUAL_TAIL_FRAMES } from './cinematicDirector';
import { botCssColor, botLabel } from './botVisuals';
import { text } from './text';

const copy = text.replay;
const speeds = [0.5, 1, 2, 4];
const fixedCameras: Array<{ mode: CameraMode; label: string }> = [
  { mode: 'auto', label: 'Кино' },
  { mode: 'adaptive', label: 'Разрез' },
  { mode: 'plan', label: 'План' },
  { mode: 'overview', label: 'Сверху' },
];

export function ReplayPlayer({ replay }: { replay: Replay }) {
  const [position, setPosition] = useState(0);
  const [playing, setPlaying] = useState(true);
  const [speed, setSpeed] = useState(1);
  const [cameraMode, setCameraMode] = useState<CameraMode>('adaptive');
  const [showTrails, setShowTrails] = useState(true);
  const shotPlan = useMemo(() => buildShotPlan(replay), [replay]);
  const botIds = useMemo(() => [...new Set(replay.frames.flatMap(frame => frame.bots.map(bot => bot.id)))], [replay]);
  const multiple = botIds.length > 2;
  const cameraOptions = useMemo(() => [
    ...fixedCameras,
    ...botIds.map(id => ({ mode: `bot:${id}` as CameraMode, label: botLabel(id, multiple) })),
  ], [botIds, multiple]);
  const lastFrame = replay.frames.length - 1;
  const visualEnd = lastFrame + VISUAL_TAIL_FRAMES;
  const finished = position >= visualEnd;

  useEffect(() => {
    if (cameraMode.startsWith('bot:') && !botIds.includes(cameraMode.slice(4))) {
      setCameraMode('adaptive');
    }
  }, [botIds, cameraMode]);

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
        return Math.min(visualEnd, current + elapsed * replay.tickRate * speed * cinematicRate / 1000);
      });
      animation = requestAnimationFrame(advance);
    }

    animation = requestAnimationFrame(advance);
    return () => cancelAnimationFrame(animation);
  }, [playing, finished, visualEnd, replay.tickRate, speed, cameraMode, shotPlan]);

  const index = Math.min(lastFrame, Math.floor(position));
  const frame = replay.frames[index];
  const next = replay.frames[Math.min(lastFrame, index + 1)];
  const fraction = position < lastFrame ? position - Math.floor(position) : 0;
  const winner = replay.result.winnerBotId;
  const outcome = winner ? copy.winner(botLabel(winner, multiple)) : copy.draw;
  const reason = !winner && replay.result.reason === 'BotDestroyed'
    ? (multiple ? copy.allDestroyed : copy.doubleKnockout) : copy.reason[replay.result.reason];
  const activeShot = shotAt(shotPlan, position);
  const cinematicFocus = (cameraMode === 'auto' || cameraMode === 'adaptive')
    && (activeShot?.kind === 'projectile' || activeShot?.kind === 'impact' || activeShot?.kind === 'finale');
  const shotNumber = activeShot ? shotPlan.shots.indexOf(activeShot) + 1 : 0;

  return (
    <>
      <div className={`arena-stage blueprint-stage${cinematicFocus ? ' cinematic-focus' : ''}`}>
        <BlueprintArena replay={replay} frame={frame} next={next} fraction={fraction}
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
          {cameraOptions.map(({ mode, label }) => (
            <button type="button" key={mode} className={cameraMode === mode ? 'active' : undefined}
              aria-pressed={cameraMode === mode} onClick={() => setCameraMode(mode)}>
              {(mode === 'auto' || mode === 'adaptive') && <span className="live-dot" />}
              {label}
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
        <input aria-label={copy.position} type="range" min="0" max={visualEnd} step="0.01" value={position} onChange={event => {
          setPlaying(false);
          setPosition(Number(event.target.value));
        }} />
        <output>{copy.time(Math.min(position, lastFrame) / replay.tickRate, lastFrame / replay.tickRate)}</output>
        <select aria-label={copy.speed} value={speed} onChange={event => setSpeed(Number(event.target.value))}>
          {speeds.map(value => <option key={value} value={value}>{copy.speedOption(value)}</option>)}
        </select>
      </div>
      <ul className="health" aria-label={copy.health}>
        {frame.bots.map(bot => (
          <li key={bot.id} style={{ color: botCssColor(bot.id) }}>
            <span>{botLabel(bot.id, multiple)}{bot.movement && <small>{text.movements[bot.movement]}</small>}</span>
            <meter aria-label={botLabel(bot.id, multiple)} min={0} max={bot.maxHp} value={bot.hp} />
            <b>{bot.forfeited ? copy.forfeited : copy.hp(bot.hp, bot.maxHp)}</b>
          </li>
        ))}
      </ul>
    </>
  );
}
