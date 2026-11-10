import { useCallback, useEffect, useRef, useState } from 'react';
import { BattleAudio, DEFAULT_AUDIO_SETTINGS } from './battleAudio';
import type { AudioChannel, AudioSettings, AudioTransport } from './battleAudio';
import type { Replay } from './contracts';
import { text } from './text';

export function useBattleAudio(replay: Replay, transport: () => AudioTransport) {
  const director = useRef<BattleAudio | null>(null);
  const currentTransport = useRef(transport);
  currentTransport.current = transport;
  const [enabled, setEnabled] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [settings, setSettings] = useState<AudioSettings>(DEFAULT_AUDIO_SETTINGS);

  useEffect(() => {
    const audio = new BattleAudio(replay);
    director.current = audio;
    setEnabled(false);
    setBusy(false);
    setError('');
    let animation: number;
    const update = () => {
      audio.update(currentTransport.current(), !document.hidden);
      animation = requestAnimationFrame(update);
    };
    animation = requestAnimationFrame(update);
    const hide = () => {
      if (document.hidden) audio.update({ ...currentTransport.current(), playing: false }, false);
    };
    document.addEventListener('visibilitychange', hide);
    return () => {
      cancelAnimationFrame(animation);
      document.removeEventListener('visibilitychange', hide);
      director.current = null;
      audio.dispose();
    };
  }, [replay]);

  useEffect(() => { director.current?.applySettings(settings); }, [settings, replay]);

  const toggle = async () => {
    const audio = director.current;
    if (!audio || busy) return;
    if (enabled) {
      audio.disable();
      setEnabled(false);
      return;
    }
    setBusy(true);
    setError('');
    try {
      await audio.enable();
      if (director.current === audio) {
        audio.update(currentTransport.current(), !document.hidden);
        setEnabled(true);
      }
    } catch {
      if (director.current === audio) setError(text.audio.unavailable);
    } finally {
      if (director.current === audio) setBusy(false);
    }
  };
  const seek = useCallback((position: number) => { director.current?.seek(position); }, []);
  const setMaster = (master: number) => setSettings(value => ({ ...value, master }));
  const setChannel = (channel: AudioChannel, volume: number) => setSettings(value => ({
    ...value, channels: { ...value.channels, [channel]: volume },
  }));
  return { enabled, busy, error, settings, toggle, seek, setMaster, setChannel };
}
