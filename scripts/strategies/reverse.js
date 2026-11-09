// Переключить обе гусеницы с полного хода вперёд на полный задний ход.
function tick({ tick }) {
  const throttle = tick < 12 ? 1 : -1;
  return { leftTrack: throttle, rightTrack: throttle };
}
