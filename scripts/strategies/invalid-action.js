// Сначала едем вперёд, потом возвращаем неверный тип. Останется частичный реплей.
function tick({ tick }) {
  if (tick === 8) return { leftTrack: 'full', rightTrack: 1 };
  return { leftTrack: 1, rightTrack: 1 };
}
