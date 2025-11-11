import { useMemo, useState } from 'react';
import type { ChangeEvent, FormEvent } from 'react';
import { API_BASE_URL, runMlAgents } from './api';
import type { MlAgentsRunResponse, RequestStatus } from './types';

type FormState = {
  runId: string;
  configPath: string;
  environmentPath: string;
  noGraphics: boolean;
};

const createDefaultForm = (): FormState => ({
  runId: '',
  configPath: 'config/ppo/3DBall.yaml',
  environmentPath: 'Project/Builds/3DBall',
  noGraphics: true
});

export function App() {
  const [form, setForm] = useState<FormState>(() => createDefaultForm());
  const [additionalArguments, setAdditionalArguments] = useState('');
  const [status, setStatus] = useState<RequestStatus>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [response, setResponse] = useState<MlAgentsRunResponse | null>(null);

  const parsedArgs = useMemo(
    () => additionalArguments
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter(Boolean),
    [additionalArguments]
  );

  const handleInputChange = (event: ChangeEvent<HTMLInputElement>) => {
    const { name, value } = event.target;
    setForm((current) => ({ ...current, [name]: value }));
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setStatus('submitting');
    setErrorMessage(null);

    try {
      const payload = {
        runId: form.runId.trim() || undefined,
        configPath: form.configPath.trim() || undefined,
        environmentPath: form.environmentPath.trim() || undefined,
        noGraphics: form.noGraphics,
        additionalArguments: parsedArgs.length > 0 ? parsedArgs : undefined
      };

      const result = await runMlAgents(payload);
      setResponse(result);
      setStatus('success');
    }
    catch (error) {
      const message = error instanceof Error ? error.message : 'Unexpected error';
      setErrorMessage(message);
      setStatus('error');
    }
  };

  const handleReset = () => {
    setForm(createDefaultForm());
    setAdditionalArguments('');
    setResponse(null);
    setErrorMessage(null);
    setStatus('idle');
  };

  return (
    <main className="container">
      <header>
        <h1>Mentor ML Console</h1>
        <p>A tiny control surface for the local ML-Agents runner.</p>
        <p className="hint">API base URL: {API_BASE_URL}</p>
      </header>

      <section className="card">
        <form onSubmit={handleSubmit} className="form-grid">
          <label>
            <span>Run ID (optional)</span>
            <input
              name="runId"
              value={form.runId}
              onChange={handleInputChange}
              placeholder="mentor-3dball"
            />
          </label>

          <label>
            <span>Config Path</span>
            <input
              name="configPath"
              value={form.configPath}
              onChange={handleInputChange}
              required
            />
          </label>

          <label>
            <span>Environment Path</span>
            <input
              name="environmentPath"
              value={form.environmentPath}
              onChange={handleInputChange}
            />
          </label>

          <label className="toggle">
            <input
              type="checkbox"
              checked={form.noGraphics}
              onChange={(event) =>
                setForm((current) => ({ ...current, noGraphics: event.target.checked }))
              }
            />
            <span>No Graphics</span>
          </label>

          <label className="full-width">
            <span>Additional CLI arguments (one per line)</span>
            <textarea
              value={additionalArguments}
              onChange={(event) => setAdditionalArguments(event.target.value)}
              rows={4}
              placeholder={"--resume\n--time-scale=20"}
            />
          </label>

          <div className="actions">
            <button type="button" onClick={handleReset} disabled={status === 'submitting'}>
              Reset
            </button>
            <button type="submit" className="primary" disabled={status === 'submitting'}>
              {status === 'submitting' ? 'Running…' : 'Run Training'}
            </button>
          </div>
        </form>
      </section>

      <section className="card">
        <header className="section-header">
          <h2>Latest Response</h2>
          <StatusBadge status={status} />
        </header>
        {status === 'idle' && <p>No runs yet. Submit the form to kick one off.</p>}
        {status === 'error' && errorMessage && (
          <p className="error">{errorMessage}</p>
        )}
        {response && (
          <div className="response">
            <dl>
              <div>
                <dt>Command</dt>
                <dd><code>{response.command}</code></dd>
              </div>
              <div>
                <dt>Working Directory</dt>
                <dd><code>{response.workingDirectory}</code></dd>
              </div>
              <div>
                <dt>Exit Code</dt>
                <dd>{response.exitCode}</dd>
              </div>
              <div>
                <dt>Started</dt>
                <dd>{formatDate(response.startedAt)}</dd>
              </div>
              <div>
                <dt>Completed</dt>
                <dd>{formatDate(response.completedAt)}</dd>
              </div>
            </dl>

            <div className="log-columns">
              <LogViewer title="Standard Output" entries={response.standardOutput} />
              <LogViewer title="Standard Error" entries={response.standardError} />
            </div>
          </div>
        )}
      </section>
    </main>
  );
}

function formatDate(value: string) {
  try {
    return new Date(value).toLocaleString();
  }
  catch {
    return value;
  }
}

function LogViewer({ title, entries }: { title: string; entries: string[] }) {
  return (
    <article>
      <h3>{title}</h3>
      {entries.length === 0 ? (
        <p className="hint">No entries</p>
      ) : (
        <pre>{entries.join('\n')}</pre>
      )}
    </article>
  );
}

function StatusBadge({ status }: { status: RequestStatus }) {
  const label: Record<RequestStatus, string> = {
    idle: 'Idle',
    submitting: 'Running',
    success: 'Success',
    error: 'Error'
  };

  return <span className={`status status-${status}`}>{label[status]}</span>;
}

export default App;