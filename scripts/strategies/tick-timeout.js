// Блокировка длится дольше бюджета одного вызова, но короче дедлайна всего матча.
function tick() {
  const cell = new Int32Array(new SharedArrayBuffer(4));
  Atomics.wait(cell, 0, 0, 250);
  return {};
}
