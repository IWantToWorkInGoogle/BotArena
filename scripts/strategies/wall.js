// Упереться в центральную стену, подождать, затем отъехать задним ходом.
function tick({ tick }) {
  const throttle = tick < 40 ? 1 : -1;
  return { leftTrack: throttle, rightTrack: throttle };
}
