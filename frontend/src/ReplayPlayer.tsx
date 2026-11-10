import { useEffect, useMemo, useRef, useState } from 'react';
import type { Replay } from './contracts';
import { BlueprintArena, tacticalPhase, tacticalPhaseLabel } from './BlueprintArena';
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
  { mode: 'perception', label: 'Восприятие' },
  { mode: 'timeline', label: 'Башня времени' },
];

export function ReplayPlayer({ replay }: { replay: Replay }) {
  const [position, setPosition] = useState(0);
  const [playing, setPlaying] = useState(true);
  const [speed, setSpeed] = useState(1);
  const [cameraMode, setCameraMode] = useState<CameraMode>('adaptive');
  const [showTrails, setShowTrails] = useState(true);
  const [showChronoXray, setShowChronoXray] = useState(true);
  const [showTacticalTrace, setShowTacticalTrace] = useState(true);
  const [traceBotId, setTraceBotId] = useState(replay.frames[0]?.bots[0]?.id ?? '');
  const heldChronoShots = useRef(new Set<string>());
  const chronoHoldUntil = useRef(0);
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
    heldChronoShots.current.clear();
    chronoHoldUntil.current = 0;
    setPosition(0);
    setPlaying(true);
    setTraceBotId(replay.frames[0]?.bots[0]?.id ?? '');
  }, [replay]);

  useEffect(() => {
    if (cameraMode.startsWith('bot:') && !botIds.includes(cameraMode.slice(4))) {
      setCameraMode('adaptive');
    }
  }, [botIds, cameraMode]);

  useEffect(() => {
    if (!botIds.includes(traceBotId)) setTraceBotId(botIds[0] ?? '');
  }, [botIds, traceBotId]);

  useEffect(() => {
    if (!playing || finished) return;
    let animation: number;
    let previous: number | undefined;

    function advance(now: number) {
      const elapsed = previous === undefined ? 0 : now - previous;
      previous = now;
      setPosition(current => {
        const directorMode = cameraMode === 'auto' || cameraMode === 'adaptive';
        const shot = directorMode ? shotAt(shotPlan, current) : undefined;
        const reachedChronoEvent = shot?.eventFrame != null && current >= shot.eventFrame;
        if (showChronoXray && reachedChronoEvent
          && (shot.kind === 'impact' || shot.kind === 'finale')
          && !heldChronoShots.current.has(shot.id)) {
          heldChronoShots.current.add(shot.id);
          chronoHoldUntil.current = now + 1_050;
        }
        const cinematicRate = now < chronoHoldUntil.current ? 0
          : directorMode ? directorPlaybackRate(shotPlan, current) : 1;
        return Math.min(visualEnd, current + elapsed * replay.tickRate * speed * cinematicRate / 1000);
      });
      animation = requestAnimationFrame(advance);
    }

    animation = requestAnimationFrame(advance);
    return () => cancelAnimationFrame(animation);
  }, [playing, finished, visualEnd, replay.tickRate, speed, cameraMode, shotPlan, showChronoXray]);

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
  const chronoMoment = showChronoXray && (cameraMode === 'auto' || cameraMode === 'adaptive')
    && activeShot?.eventFrame != null
    && (activeShot.kind === 'impact' || activeShot.kind === 'finale')
    && position >= activeShot.eventFrame && position <= activeShot.eventFrame + 1.35;
  const shotNumber = activeShot ? shotPlan.shots.indexOf(activeShot) + 1 : 0;
  const traceSample = useMemo(() => (replay.behaviorSamples ?? [])
    .filter(sample => sample.botId === traceBotId && sample.tick <= frame.tick)
    .at(-1), [replay.behaviorSamples, traceBotId, frame.tick]);
  const tracePhase = traceSample ? tacticalPhase(traceSample) : undefined;
  const traceMovement = frame.bots.find(bot => bot.id === traceBotId)?.movement ?? 'tracked';
  const traceCommands: Array<[string, number]> = traceSample
    ? traceMovement === 'tracked'
      ? [['L', traceSample.action.leftTrack], ['R', traceSample.action.rightTrack]]
      : traceMovement === 'wheeled'
        ? [['V', traceSample.action.throttle ?? 0], ['T', traceSample.action.turn ?? 0]]
        : [['X', traceSample.action.thrust?.x ?? 0], ['Y', traceSample.action.thrust?.y ?? 0],
          ['T', traceSample.action.turn ?? 0]]
    : [];
  const lastVisibleSample = useMemo(() => (replay.behaviorSamples ?? [])
    .filter(sample => sample.botId === traceBotId && sample.tick <= frame.tick && sample.enemyVisible)
    .at(-1), [replay.behaviorSamples, traceBotId, frame.tick]);
  const perceptionEnemy = frame.bots.find(bot => bot.id !== traceBotId);
  const perceptionHasContact = traceSample?.enemyVisible === true && (perceptionEnemy?.hp ?? 0) > 0;
  const lostContactSeconds = lastVisibleSample
    ? Math.max(0, frame.tick - lastVisibleSample.tick) / replay.tickRate : undefined;
  const formatTrack = (value: number) => `${value >= 0 ? '+' : '−'}${Math.abs(value).toFixed(2)}`;
  const seekReplay = (nextPosition: number) => {
    setPlaying(false);
    heldChronoShots.current.clear();
    chronoHoldUntil.current = 0;
    setPosition(nextPosition);
  };

  return (
    <>
      <div className={`arena-stage blueprint-stage${cinematicFocus ? ' cinematic-focus' : ''}`
        + `${cameraMode === 'perception' ? ' perception-mode' : ''}`
        + `${cameraMode === 'timeline' ? ' timeline-mode' : ''}`}>
        <BlueprintArena replay={replay} frame={frame} next={next} fraction={fraction}
          position={position} cameraMode={cameraMode} showTrails={showTrails} shotPlan={shotPlan}
          showChronoXray={showChronoXray} showTacticalTrace={showTacticalTrace}
          traceBotId={traceBotId} perceptionBotId={traceBotId} onSeek={seekReplay} />
        <div className="blueprint-hud" aria-hidden="true">
          <span>{cameraMode === 'perception' ? `BOT PERCEPTION / ${botLabel(traceBotId)}`
            : cameraMode === 'timeline' ? 'CHRONOSPACE / TIME TOWER'
            : (cameraMode === 'auto' || cameraMode === 'adaptive') && activeShot
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
        {chronoMoment && (
          <div className="chrono-badge" aria-hidden="true">
            <strong>CHRONO X‑RAY</strong>
            <span>TIME LOCK · IMPACT VOLUME</span>
          </div>
        )}
        {showTacticalTrace && cameraMode !== 'perception' && cameraMode !== 'timeline'
          && traceSample && tracePhase && (
          <div className={`tactical-readout phase-${tracePhase}`} aria-hidden="true">
            <span>TACTICAL TRACE / {botLabel(traceBotId)}</span>
            <strong>{tacticalPhaseLabel(tracePhase)}</strong>
            {traceCommands.map(([command, value]) => (
              <div className="track-command" key={command}>
                <b>{command}</b>
                <i><u className={value < 0 ? 'reverse' : undefined}
                  style={{ width: `${Math.abs(value) * 100}%` }} /></i>
                <em>{formatTrack(value)}</em>
              </div>
            ))}
            <small>ШИРИНА ЛЕНТЫ — ТЯГА · ФИОЛЕТОВЫЙ — НАВЕДЕНИЕ · ЯНТАРНЫЙ — ОГОНЬ</small>
          </div>
        )}
        {cameraMode === 'perception' && traceSample && (
          <div className={`perception-readout ${perceptionHasContact ? 'contact' : 'lost'}`} aria-hidden="true">
            <span>PERCEPTION FEED / {botLabel(traceBotId)}</span>
            <strong>{perceptionHasContact ? 'ЦЕЛЬ ВИДНА'
              : lastVisibleSample ? `КОНТАКТ ПОТЕРЯН · ${lostContactSeconds?.toFixed(1)}s`
              : 'ЦЕЛЬ НЕ ОБНАРУЖЕНА'}</strong>
            <small>ТЁМНЫЕ СЕКТОРЫ — ИНФОРМАЦИОННАЯ ТЕНЬ · ЯНТАРНЫЙ КОНТУР — ПОСЛЕДНИЙ КОНТАКТ</small>
          </div>
        )}
        {cameraMode === 'timeline' && (
          <div className="timeline-readout" aria-hidden="true">
            <span>SPACETIME RECONSTRUCTION</span>
            <strong>T+{(Math.min(position, lastFrame) / replay.tickRate).toFixed(2)}s</strong>
            <small>Y = ВРЕМЯ · ЛЕНТЫ = ТАНКИ · ЯНТАРНЫЕ СВЯЗИ = ВЫСТРЕЛЫ · КЛИК = ПЕРЕЙТИ К КАДРУ</small>
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
        <div className="visual-layers">
          <label className="trail-toggle">
            <input type="checkbox" checked={showChronoXray}
              onChange={event => setShowChronoXray(event.target.checked)} />
            Chrono X‑Ray
          </label>
          <label className="trail-toggle">
            <input type="checkbox" checked={showTacticalTrace}
              onChange={event => setShowTacticalTrace(event.target.checked)} />
            Тактический след
          </label>
          {(showTacticalTrace || cameraMode === 'perception') && botIds.length > 1 && (
            <select aria-label="Бот для анализа" value={traceBotId}
              onChange={event => setTraceBotId(event.target.value)}>
              {botIds.map(id => <option key={id} value={id}>{botLabel(id)}</option>)}
            </select>
          )}
        </div>
      </div>
      <div className="controls replay-controls">
        <button type="button" onClick={() => {
          if (finished) {
            heldChronoShots.current.clear();
            chronoHoldUntil.current = 0;
            setPosition(0);
          }
          setPlaying(finished || !playing);
        }}>{finished ? copy.restart : playing ? copy.pause : copy.resume}</button>
        <input aria-label={copy.position} type="range" min="0" max={visualEnd} step="0.01" value={position} onChange={event => {
          seekReplay(Number(event.target.value));
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
