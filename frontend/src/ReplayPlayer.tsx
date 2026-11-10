import { useEffect, useId, useState } from 'react';
import type { Arena, Bot, Frame, Point, Replay } from './contracts';
import { text } from './text';

const palette = ['#3dc9f1', '#ff9a45', '#bda0fa', '#74d9a4'];
const copy = text.replay;
const speeds = [0.5, 1, 2, 4];

function positionBetween(start: Point, end: Point, fraction: number): Point {
  return { x: start.x + (end.x - start.x) * fraction, y: start.y + (end.y - start.y) * fraction };
}

function angleBetween(start: number, end: number, fraction: number): number {
  const turn = Math.atan2(Math.sin(end - start), Math.cos(end - start));
  return (start + turn * fraction) * 180 / Math.PI;
}

function ArenaView({ arena, frame, next, fraction, finished, winner, colors }: {
  arena: Arena; frame: Frame; next: Frame; fraction: number; finished: boolean; winner: Bot['id'] | null; colors: ReadonlyMap<string, string>;
}) {
  const haloId = useId();
  return (
    <svg className="arena" viewBox={`0 0 ${arena.width} ${arena.height}`} role="img" aria-label={copy.imageLabel}>
      <defs>
        {winner && <radialGradient id={haloId}>
          <stop offset="0%" stopColor={colors.get(winner)} stopOpacity=".65" />
          <stop offset="65%" stopColor={colors.get(winner)} stopOpacity=".3" />
          <stop offset="100%" stopColor={colors.get(winner)} stopOpacity="0" />
        </radialGradient>}
        <pattern id="grid" width="1" height="1" patternUnits="userSpaceOnUse">
          <path d="M 1 0 H 0 V 1" fill="none" stroke="#253b51" strokeWidth=".04" />
        </pattern>
      </defs>
      <rect width={arena.width} height={arena.height} fill="url(#grid)" />
      {arena.walls.map((wall, i) => <rect key={i} {...wall} rx=".08" fill="#273e58" stroke="#476383" strokeWidth=".05" />)}
      {frame.bullets.map(bullet => {
        const end = next.bullets.find(b => b.id === bullet.id) ?? bullet;
        const { x, y } = positionBetween(bullet.position, end.position, fraction);
        const degrees = Math.atan2(bullet.direction.y, bullet.direction.x) * 180 / Math.PI;
        return <rect key={bullet.id} x="-.095" y="-.045" width=".19" height=".09"
          transform={`translate(${x} ${y}) rotate(${degrees})`} fill={colors.get(bullet.ownerBotId)} />;
      })}
      {!finished && frame.events.map((event, index) => {
        if (!event.position || event.type === 'MatchFinished') return null;
        const hit = event.type === 'BotHit' || event.type === 'BotDestroyed';
        return <circle key={index} cx={event.position.x} cy={event.position.y}
          r={(hit ? 0.25 : 0.12) + fraction * 0.35} fill="none"
          stroke={hit ? '#fff3d0' : '#e7ab55'} strokeWidth=".06" opacity={1 - fraction * 0.8} />;
      })}
      {frame.bots.map(bot => {
        const end = next.bots.find(b => b.id === bot.id) ?? bot;
        const { x, y } = positionBetween(bot.position, end.position, fraction);
        const hull = angleBetween(bot.bodyAngle, end.bodyAngle, fraction);
        const turret = angleBetween(bot.turretAngle, end.turretAngle, fraction);
        const color = colors.get(bot.id);
        const hovering = bot.movement === 'hover';
        const strafing = hovering || bot.movement === 'omni';
        const velocity = bot.velocity && positionBetween(bot.velocity, end.velocity ?? bot.velocity, fraction);
        const won = finished && winner === bot.id;
        const faded = bot.hp === 0 || bot.forfeited || (finished && winner !== null && !won);
        return (
          <g key={bot.id} transform={`translate(${x} ${y})`} opacity={faded ? 0.35 : 1}
            className={won ? 'winning-bot' : undefined} style={{ color }}>
            {won && <ellipse cx="0" cy="0" rx=".91" ry=".7" fill={`url(#${haloId})`} />}
            {strafing && velocity && !finished && Math.hypot(velocity.x, velocity.y) > 0.1 &&
              <line className="hover-trail" x1={-velocity.x * 0.32} y1={-velocity.y * 0.32} x2="0" y2="0"
                stroke={color} strokeWidth=".055" strokeDasharray=".085 .065" opacity=".85" />}
            <g transform={`rotate(${hull})`}>
              {hovering ? (
                <path className="hover-hull" d="M .34 0 L .21 .29 H -.26 L -.34 .18 V -.18 L -.26 -.29 H .21 Z"
                  fill="#173242" stroke={color} strokeWidth=".045" />
              ) : bot.movement === 'wheeled' ? (
                <g className="wheeled-hull">
                  <rect x="-.32" y="-.18" width=".64" height=".36" rx=".1" fill="#283140" stroke={color} strokeWidth=".045" />
                  <path d="M -.25 -.245 H -.11 M .11 -.245 H .25 M -.25 .245 H -.11 M .11 .245 H .25"
                    stroke={color} strokeWidth=".09" strokeLinecap="round" />
                </g>
              ) : bot.movement === 'omni' ? (
                <path className="omni-hull" d="M -.17 -.29 H .17 L .34 -.12 V .12 L .17 .29 H -.17 L -.34 .12 V -.12 Z"
                  fill="#263849" stroke={color} strokeWidth=".045" />
              ) : (
                <>
                  <rect x="-.34" y="-.29" width=".68" height=".58" rx=".04" fill="#111c2c" stroke={color} strokeWidth=".045" />
                  <path d="M -.29 -.2 H .29 M -.29 .2 H .29" stroke={color} strokeWidth=".045" />
                </>
              )}
            </g>
            <g transform={`rotate(${turret})`}>
              <circle r=".15" fill={color} />
              <rect x=".07" y="-.045" width=".43" height=".09" fill={color} />
            </g>
            <rect x="-.37" y="-.58" width=".74" height=".065" fill="#273e58" />
            <rect x="-.37" y="-.58" width={.74 * bot.hp / bot.maxHp} height=".065" fill={color} />
          </g>
        );
      })}
    </svg>
  );
}

export function ReplayPlayer({ replay }: { replay: Replay }) {
  const [position, setPosition] = useState(0);
  const [playing, setPlaying] = useState(true);
  const [speed, setSpeed] = useState(1);
  const last = replay.frames.length - 1;
  const finished = position >= last;

  useEffect(() => {
    if (!playing || finished) return;
    let animation: number;
    let previous: number | undefined;

    function advance(now: number) {
      const elapsed = previous === undefined ? 0 : now - previous;
      previous = now;
      setPosition(current => Math.min(last, current + elapsed * replay.tickRate * speed / 1000));
      animation = requestAnimationFrame(advance);
    }

    animation = requestAnimationFrame(advance);
    return () => cancelAnimationFrame(animation);
  }, [playing, finished, last, replay.tickRate, speed]);

  const index = Math.floor(position);
  const frame = replay.frames[index];
  const next = replay.frames[Math.min(last, index + 1)];
  const winner = replay.result.winnerBotId;
  const multiple = frame.bots.length > 2;
  const colors = new Map(replay.frames[0].bots.map((bot, index) => [bot.id, palette[index % palette.length]]));
  const outcome = winner ? copy.winner(text.botLabel(winner, multiple)) : copy.draw;
  const reason = !winner && replay.result.reason === 'BotDestroyed'
    ? (multiple ? copy.allDestroyed : copy.doubleKnockout) : copy.reason[replay.result.reason];

  return (
    <>
      <div className="arena-stage">
        <ArenaView arena={replay.arena} frame={frame} next={next} fraction={position - index}
          finished={finished} winner={winner} colors={colors} />
        {finished && <p role="status" className="outcome">{copy.outcome(outcome, reason)}</p>}
      </div>
      <div className="controls">
        <button type="button" onClick={() => {
          if (finished) setPosition(0);
          setPlaying(finished || !playing);
        }}>{finished ? copy.restart : playing ? copy.pause : copy.resume}</button>
        <input aria-label={copy.position} type="range" min="0" max={last} value={index} onChange={event => {
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
          <li key={bot.id} style={{ color: colors.get(bot.id) }}>
            <span>{text.botLabel(bot.id, multiple)}{bot.movement && <small>{text.movements[bot.movement]}</small>}</span>
            <meter aria-label={text.botLabel(bot.id, multiple)} min={0} max={bot.maxHp} value={bot.hp} />
            <b>{bot.forfeited ? copy.forfeited : copy.hp(bot.hp, bot.maxHp)}</b>
          </li>
        ))}
      </ul>
    </>
  );
}
