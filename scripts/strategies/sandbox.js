// Если возможности хоста доступны или динамический JS запустился, тест падает.
function tick() {
  const host = [typeof process, typeof require, typeof System, typeof importNamespace, typeof fetch];
  if (host.some(type => type !== 'undefined')) throw new Error('Host API exposed');

  let executed = false;
  try { eval('executed = true'); } catch {}
  try { if (Function('return true')()) executed = true; } catch {}
  if (executed) throw new Error('Dynamic code executed');
  return {};
}
