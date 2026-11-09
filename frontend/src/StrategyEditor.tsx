import { useEffect, useRef } from 'react';
import { minimalSetup, EditorView } from 'codemirror';
import { lineNumbers } from '@codemirror/view';
import { javascript } from '@codemirror/lang-javascript';
import { oneDark } from '@codemirror/theme-one-dark';

export default function StrategyEditor({ initialCode, onChange }: {
  initialCode: string;
  onChange: (code: string) => void;
}) {
  const parent = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const editor = new EditorView({
      doc: initialCode,
      parent: parent.current!,
      extensions: [
        minimalSetup,
        lineNumbers(),
        javascript(),
        oneDark,
        EditorView.contentAttributes.of({ id: 'strategy', 'aria-labelledby': 'strategy-label' }),
        EditorView.updateListener.of(update => {
          if (update.docChanged) onChange(update.state.doc.toString());
        }),
      ],
    });
    return () => editor.destroy();
  }, [initialCode, onChange]);

  return <div className="code-editor" ref={parent} />;
}
