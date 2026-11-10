export interface Point { x: number; y: number }
export interface Wall extends Point { width: number; height: number }
export interface Arena { width: number; height: number; walls: Wall[] }
export type Movement = 'tracked' | 'hover' | 'wheeled' | 'omni';
export interface Bot {
  id: string;
  teamId?: string;
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
export interface BotAction {
  leftTrack: number;
  rightTrack: number;
  aimDirection: Point;
  shoot: boolean;
  thrust?: Point;
  turn?: number;
  throttle?: number;
}
export interface BehaviorSample {
  tick: number;
  botId: Bot['id'];
  position: Point;
  bodyAngle: number;
  turretAngle: number;
  enemyVisible: boolean;
  action: BotAction;
}
export interface BattleEvent {
  tick: number;
  type: 'ShotFired' | 'BotHit' | 'BotDestroyed' | 'ZoneDamage' | 'MatchFinished';
  sourceBotId: string | null;
  targetBotId: string | null;
  position: Point | null;
  bulletId: number | null;
  damage: number | null;
}
export interface SafeZone { x: number; y: number; width: number; height: number }
export interface Frame {
  tick: number;
  bots: Bot[];
  bullets: Bullet[];
  events: BattleEvent[];
  safeZone?: SafeZone | null;
}
export interface ReachMarker { x: number; y: number; radius: number }
export interface Replay {
  tickRate: number;
  arena: Arena;
  result: {
    winnerBotId: string | null;
    winnerTeamId?: string | null;
    reason: 'BotDestroyed' | 'TimeLimit' | 'BotError' | 'ObjectiveComplete';
    durationTicks: number;
  };
  frames: Frame[];
  behaviorSamples?: BehaviorSample[];
  marker?: ReachMarker | null;
}
export interface CampaignSpawn {
  id: string;
  teamId?: string;
  position: Point;
  bodyAngle: number;
  turretAngle: number;
}
export interface CampaignLevel {
  id: string;
  order: number;
  name: string;
  summary: string;
  objective: string;
  hint: string;
  arena: Arena;
  marker?: ReachMarker | null;
  spawns: CampaignSpawn[];
  playerBotIds?: string[];
  starters?: Record<string, Record<StrategyLanguage, string>>;
  starter: Record<StrategyLanguage, string>;
}
export interface CampaignTrack {
  id: string;
  order: number;
  name: string;
  summary: string;
  available: boolean;
  levelCount: number;
  levels: CampaignLevel[];
}
export type AppMode = 'lab' | 'campaign';
export interface ScriptError {
  botId: string;
  tick: number | null;
  message: string;
  line?: number | null;
  column?: number | null;
}

export interface StrategyLogLine {
  tick: number | null;
  message: string;
}
export interface MatchResponse {
  replay: Replay;
  error: ScriptError | null;
  logs?: StrategyLogLine[] | null;
}
export interface OpponentSlot { id: string; movement: Movement }
export interface Opponent { id: string; name: string; description: string }

export type MatchJobStatus =
  | { state: 'queued'; position: number }
  | { state: 'running' }
  | { state: 'completed'; match: MatchResponse | null; error: string | null; diagnostic?: ScriptError | null };

export type StrategyLanguage = "javascript" | "python";
