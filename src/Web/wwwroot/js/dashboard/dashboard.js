document.addEventListener('DOMContentLoaded', () => {
  const graphElement = document.getElementById('neural-graph');
  if (!graphElement) return;
  Neura.Graph.init('neural-graph');

  Neura.Connection.start((evt) => {
    handleEvent(evt);
  });

  function handleEvent(evt) {
    const { type, payload } = evt;
    switch (type) {
      case 'AgentExecuting': {
        Neura.Graph.ensureAgentNode(payload.id || payload.Id, payload.name || payload.Name);
        Neura.pushEvent(`Agent executing: <b>${payload.name || payload.Name}</b>`);
        break;
      }
      case 'TokenUsageUpdated': {
        const ratio = payload.usageRatio ?? payload.UsageRatio ?? 0;
        document.getElementById('metric-context').textContent = Neura.formatPercent(ratio);
        document.getElementById('metric-tokens').textContent = (payload.totalTokens ?? payload.TotalTokens ?? 0);
        break;
      }
      case 'ContextWarning': {
        Neura.Graph.setAgentState(payload.id || payload.Id, 'agent-warning');
        Neura.pushEvent(`Context warning on agent (usage ${Neura.formatPercent(payload.usageRatio ?? payload.UsageRatio)})`, 'warning');
        break;
      }
      case 'ContextCritical': {
        Neura.Graph.setAgentState(payload.id || payload.Id, 'agent-critical');
        Neura.pushEvent(`⚠ Context CRITICAL (usage ${Neura.formatPercent(payload.usageRatio ?? payload.UsageRatio)})`, 'critical');
        break;
      }
      case 'ContextExhausted': {
        Neura.pushEvent('Context EXHAUSTED — initiating handoff', 'critical');
        Neura.announce('Context exhausted. Handoff starting.');
        break;
      }
      case 'HandoffStarted': {
        Neura.Graph.ensureHandoffNode(payload.id || payload.Id, payload.from || payload.From, payload.to || payload.To);
        Neura.pushEvent(`Handoff started: ${payload.from || payload.From} → ${payload.to || payload.To}`, 'warning');
        break;
      }
      case 'HandoffProgress': {
        Neura.pushEvent(`Handoff progress: ${payload.stage || payload.Stage}`);
        break;
      }
      case 'HandoffCompleted': {
        const from = payload.from || payload.From, to = payload.to || payload.To;
        Neura.Graph.pulseEdge(from, to);
        document.getElementById('metric-handoffs').textContent =
          (parseInt(document.getElementById('metric-handoffs').textContent || '0') + 1);
        Neura.pushEvent(`Handoff completed: ${from} → ${to}`);
        Neura.announce('Handoff completed. Work continues on the new agent.');
        break;
      }
      case 'TaskStarted': {
        Neura.Graph.ensureTaskNode(payload.id || payload.Id, payload.title || payload.Title);
        document.getElementById('metric-tasks').textContent =
          (parseInt(document.getElementById('metric-tasks').textContent || '0') + 1);
        Neura.pushEvent(`Task started: ${payload.title || payload.Title}`);
        break;
      }
      case 'TaskCompleted': {
        Neura.pushEvent('Task completed ✓');
        break;
      }
      case 'TaskFailed':
      case 'MissionFailed':
      case 'Error': {
        Neura.Graph.addErrorNode(payload?.id || payload?.Id, payload?.error || payload?.Error || type);
        Neura.pushEvent(payload?.error || payload?.Error || type, 'critical');
        break;
      }
      default:
        if (String(type).toLowerCase().includes('error') || String(type).toLowerCase().includes('failed')) {
          Neura.Graph.addErrorNode(payload?.id || payload?.Id, type);
          Neura.pushEvent(`${type}`, 'critical');
        } else {
          Neura.pushEvent(`${type}`);
        }
    }
  }

  document.querySelectorAll('[data-filter]').forEach((button) => {
    button.addEventListener('click', () => {
      document.querySelectorAll('[data-filter]').forEach((b) => b.classList.remove('chip--active'));
      button.classList.add('chip--active');
      Neura.Graph.setFilter(button.dataset.filter || 'all');
    });
  });

  const runBtn = document.getElementById('run-demo-btn');
  if (runBtn) {
    runBtn.addEventListener('click', async () => {
      runBtn.disabled = true;
      runBtn.textContent = 'Running…';
      Neura.pushEvent('Simulation demo started (Claude → context exhaustion → ChatGPT handoff)');
      try {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const resp = await fetch('/Brain/RunSimulationDemo', {
          method: 'POST',
          headers: token ? { 'RequestVerificationToken': token, 'Accept': 'application/json' } : { 'Accept': 'application/json' }
        });
        const raw = await resp.text();
        let data;
        try { data = raw ? JSON.parse(raw) : null; }
        catch { data = { success: false, error: `Server returned ${resp.status} without JSON.` }; }
        if (!resp.ok || !data) {
          Neura.pushEvent(`Demo failed: ${data?.error || `HTTP ${resp.status}`}`, 'critical');
        } else {
          Neura.pushEvent(data.success
            ? `Demo complete. Completed by: <b>${data.completedBy}</b>`
            : `Demo failed: ${data.error}`, data.success ? 'info' : 'critical');
        }
      } catch (e) {
        Neura.pushEvent('Demo request failed: ' + e.message, 'critical');
      } finally {
        runBtn.disabled = false;
        runBtn.textContent = '▶ Run Simulation Demo';
      }
    });
  }
});
