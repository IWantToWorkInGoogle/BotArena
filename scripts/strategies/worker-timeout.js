// Нативное ожидание не проверяет бюджет Jint изнутри. Процесс остановит сервер.
function tick() {
  const cell = new Int32Array(new SharedArrayBuffer(4));
  Atomics.wait(cell, 0, 0, 7000);
  return {};
}
