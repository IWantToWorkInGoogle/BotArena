import type { Movement, StrategyLanguage } from './contracts';
import { text } from './text';

function Fields({ title, rows }: { title: string; rows: readonly (readonly [string, string, string])[] }) {
  return (
    <table className="strategy-fields">
      <caption>{title}</caption>
      <thead><tr>{text.help.columns.map(column => <th key={column} scope="col">{column}</th>)}</tr></thead>
      <tbody>
        {rows.map(([field, type, description]) => (
          <tr key={field}>
            <th scope="row"><code>{field}</code></th>
            <td><code>{type}</code></td>
            <td>{description}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

const movementFields = {
  tracked: text.help.trackedFields,
  hover: text.help.hoverFields,
  wheeled: text.help.wheeledFields,
  omni: text.help.omniFields,
};

export function StrategyHelp({ language, movement }: { language: StrategyLanguage; movement: Movement }) {
  return (
    <details>
      <summary>{text.help.title}</summary>
      <p>{language === 'python' ? text.help.pythonIntro : text.help.intro}</p>
      <Fields title={text.help.input} rows={text.help.inputFields} />
      {movement === 'hover' && <p>{text.help.hoverIntro}</p>}
      <Fields title={text.help.output} rows={[
        ...movementFields[movement],
        ...text.help.sharedFields,
      ]} />
      <Fields title={text.help.nested} rows={text.help.nestedFields} />
    </details>
  );
}
