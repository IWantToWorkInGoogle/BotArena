let sharedContext: AudioContext | null = null;

function audioContext(): AudioContext | null {
  const Context = window.AudioContext ?? (window as typeof window & {
    webkitAudioContext?: typeof AudioContext;
  }).webkitAudioContext;
  if (!Context) return null;
  if (!sharedContext || sharedContext.state === 'closed') {
    sharedContext = new Context();
  }
  return sharedContext;
}

export async function unlockBattleAudio() {
  const context = audioContext();
  if (!context) return;
  if (context.state === 'suspended') {
    try {
      await context.resume();
    } catch {
      /* ignore autoplay blocks until next gesture */
    }
  }
}

export function playShotSound() {
  const context = audioContext();
  if (!context || context.state !== 'running') {
    void unlockBattleAudio().then(() => {
      if (audioContext()?.state === 'running') playShotSound();
    });
    return;
  }

  const duration = 0.14;
  const length = Math.max(1, Math.floor(context.sampleRate * duration));
  const buffer = context.createBuffer(1, length, context.sampleRate);
  const data = buffer.getChannelData(0);
  for (let i = 0; i < length; i++) {
    const t = i / context.sampleRate;
    const snap = Math.exp(-t * 55);
    const body = Math.exp(-t * 18);
    data[i] = (Math.random() * 2 - 1) * snap * 0.85 + (Math.random() * 2 - 1) * body * 0.25;
  }

  const source = context.createBufferSource();
  source.buffer = buffer;

  const filter = context.createBiquadFilter();
  filter.type = 'bandpass';
  filter.frequency.value = 900 + Math.random() * 500;
  filter.Q.value = 0.7;

  const gain = context.createGain();
  gain.gain.value = 0.2;

  source.connect(filter);
  filter.connect(gain);
  gain.connect(context.destination);
  source.start();
}
