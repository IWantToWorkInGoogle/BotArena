// Разворот на месте, смена направления, затем частое переключение команд.
function tick({ tick }) {
  const turn = tick < 12 ? 1 : tick < 24 ? -1 : tick % 2 === 0 ? 1 : -1;
  return { leftTrack: turn, rightTrack: -turn, aimDirection: { x: 0, y: -turn } };
}
