export interface Point { x: number; y: number }
export interface Wall extends Point { width: number; height: number }
export interface Arena { width: number; height: number; walls: Wall[] }
export type Movement = 'tracked' | 'hover' | 'wheeled' | 'omni';
export interface Bot {
  id: string;
  position: Point;
  movement?: Movement;
  velocity?: Point;
  forfeited?: boolean;
  hp: number;
  maxHp: number;
  bodyAngle: number;
  turretAngle: number;
  speed: number;
  reloadingTime: number;
  canShoot: boolean;
}
export interface Bullet { id: number; ownerBotId: Bot['id']; position: Point; direction: Point }
export interface BattleEvent {
  tick: number;
  type: 'ShotFired' | 'BotHit' | 'BotDestroyed' | 'MatchFinished';
  sourceBotId: Bot['id'] | null;
  targetBotId: Bot['id'] | null;
  position: Point | null;
  bulletId: number | null;
  damage: number | null;
}
export interface Frame { tick: number; bots: Bot[]; bullets: Bullet[]; events: BattleEvent[] }
export interface Replay {
  tickRate: number;
  arena: Arena;
  result: { winnerBotId: Bot['id'] | null; reason: 'BotDestroyed' | 'TimeLimit' | 'BotError'; durationTicks: number };
  frames: Frame[];
}
export interface ScriptError {
  botId: Bot['id'];
  tick: number | null;
  message: string;
  line?: number | null;
  column?: number | null;
}

export interface MatchResponse {
  replay: Replay;
  error: ScriptError | null;
}
export interface OpponentSlot { id: string; movement: Movement }
export interface Opponent { id: string; name: string; description: string }

export type MatchJobStatus =
  | { state: 'queued'; position: number }
  | { state: 'running' }
  | { state: 'completed'; match: MatchResponse | null; error: string | null; diagnostic?: ScriptError | null };

export type StrategyLanguage = "javascript" | "python";
