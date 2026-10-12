document.addEventListener('DOMContentLoaded', () => {
  const graphElement = document.getElementById('neural-graph');
  if (!graphElement) return;

  const graph = Neura.Graph.init('neural-graph');
  if (!graph) {
    Neura.pushEvent('Neural graph library did not load. Check the page CSP/network configuration.', 'critical');
    return;
  }

  let latestAgentId = null;
  Neura.Connection.start(handleEvent);

  function value(payload, ...keys) {
    for (const key of keys) {
      if (payload && payload[key] !== undefined && payload[key] !== null) return payload[key];
    }
    return null;
  }

  function updateNumber(id, amount) {
    const element = document.getElementById(id);
    if (!element) return;
    const current = Number.parseFloat(element.textContent || '0') || 0;
    element.textContent = String(current + amount);
  }

  function handleEvent(evt) {
    const type = value(evt, 'type', 'Type') || 'Event';
    const payload = value(evt, 'payload', 'Payload') || {};
    const missionId = value(payload, 'missionId', 'MissionId');
    const agentId = value(payload, 'agentId', 'AgentId');
    const agentName = value(payload, 'agentName', 'AgentName') || 'Agent';

    switch (type) {
      case 'AgentStarted':
      case 'AgentExecuting':
      case 'AgentThinking': {
        latestAgentId = agentId || latestAgentId;
        if (agentId) Neura.Graph.ensureAgentNode(agentId, agentName, type);
        Neura.pushEvent(`${agentName} ${type === 'AgentStarted' ? 'connected to the mission' : type === 'AgentThinking' ? 'is preparing a response' : 'is executing a task'}.`);
        Neura.announce(`${agentName} is ${type === 'AgentThinking' ? 'preparing a response' : 'executing a task'}.`);
        break;
      }
      case 'TokenUsageUpdated': {
        if (agentId) {
          latestAgentId = agentId;
          Neura.Graph.ensureAgentNode(agentId, agentName, 'Executing');
        }
        const ratio = Number(value(payload, 'usageRatio', 'UsageRatio') || 0);
        const total = Number(value(payload, 'totalTokens', 'TotalTokens') || 0);
        const context = document.getElementById('metric-context');
        const tokens = document.getElementById('metric-tokens');
        if (context) context.textContent = Neura.formatPercent(ratio);
        if (tokens) tokens.textContent = total.toLocaleString();
        const cost = Number(value(payload, 'estimatedCost', 'EstimatedCost') || 0);
        const costElement = document.getElementById('metric-cost');
        if (costElement && cost > 0) {
          const accumulated = (Number.parseFloat(costElement.dataset.total || '0') || 0) + cost;
          costElement.dataset.total = String(accumulated);
          costElement.textContent = `$${accumulated.toFixed(4)}`;
        }
        Neura.pushEvent(`${agentName}: ${total.toLocaleString()} tokens, ${Neura.formatPercent(ratio)} context, ${Number(value(payload, 'latencyMilliseconds', 'LatencyMilliseconds') || 0).toFixed(0)} ms.`);
        break;
      }
      case 'ContextWarning': {
        const ratio = value(payload, 'usageRatio', 'UsageRatio');
        if (agentId) Neura.Graph.setAgentState(agentId, 'agent-warning');
        Neura.pushEvent(`Context warning on ${agentName} (${Neura.formatPercent(ratio)}).`, 'warning');
        Neura.announce(`Context warning on ${agentName}: ${Neura.formatPercent(ratio)} used.`);
        break;
      }
      case 'ContextCritical': {
        const ratio = value(payload, 'usageRatio', 'UsageRatio');
        if (agentId) Neura.Graph.setAgentState(agentId, 'agent-critical');
        Neura.pushEvent(`Context critical on ${agentName} (${Neura.formatPercent(ratio)}).`, 'critical');
        Neura.announce(`Context critical on ${agentName}: ${Neura.formatPercent(ratio)} used.`);
        break;
      }
      case 'ContextExhausted':
        if (agentId) Neura.Graph.setAgentState(agentId, 'agent-critical');
        Neura.pushEvent(`Context exhausted on ${agentName}; the continuity engine is packaging task state.`, 'critical');
        Neura.announce(`Context exhausted on ${agentName}. The continuity engine is packaging the minimum state needed to continue.`);
        break;
      case 'RoutingDecision': {
        const selectedName = value(payload, 'selectedAgentName', 'SelectedAgentName') || 'Agent';
        const score = value(payload, 'selectedScore', 'SelectedScore');
        const selectedId = value(payload, 'selectedAgentId', 'SelectedAgentId');
        if (selectedId) Neura.Graph.ensureAgentNode(selectedId, selectedName, 'Online');
        Neura.pushEvent(`Router selected ${selectedName}${score ? ` (score ${score.overallScore ?? score.OverallScore ?? ''})` : ''}.`);
        Neura.announce(`Routing decision: selected ${selectedName}.`);
        break;
      }
      case 'HandoffStarted': {
        const id = value(payload, 'handoffId', 'HandoffId');
        const from = value(payload, 'fromAgentId', 'FromAgentId', 'from', 'From');
        const to = value(payload, 'toAgentId', 'ToAgentId', 'to', 'To');
        const fromName = value(payload, 'fromAgentName', 'FromAgentName') || 'Source agent';
        const toName = value(payload, 'toAgentName', 'ToAgentName') || 'Receiving agent';
        if (id && from && to) Neura.Graph.ensureHandoffNode(id, from, to, { fromName, toName, reason: value(payload, 'reason', 'Reason') });
        Neura.pushEvent(`Handoff started: ${fromName} → ${toName}.`, 'warning');
        Neura.announce(`Handoff started from ${fromName} to ${toName}.`);
        break;
      }
      case 'HandoffProgress':
        Neura.pushEvent(`Handoff: ${value(payload, 'stage', 'Stage') || 'processing context package'}.`);
        break;
      case 'HandoffCompleted': {
        const from = value(payload, 'fromAgentId', 'FromAgentId');
        const to = value(payload, 'toAgentId', 'ToAgentId');
        const id = value(payload, 'handoffId', 'HandoffId');
        const fromName = value(payload, 'fromAgentName', 'FromAgentName') || 'Source agent';
        const toName = value(payload, 'toAgentName', 'ToAgentName') || 'Receiving agent';
        if (id && from && to) Neura.Graph.ensureHandoffNode(id, from, to, {
          fromName, toName,
          contextPackageId: value(payload, 'contextPackageId', 'ContextPackageId'),
          reason: value(payload, 'reason', 'Reason'),
          status: 'Validated'
        });
        if (from && to) Neura.Graph.pulseEdge(from, to);
        updateNumber('metric-handoffs', 1);
        Neura.pushEvent(`Handoff validated: ${fromName} → ${toName}.`);
        Neura.announce(`Handoff completed. ${toName} received the validated context package and continues the task.`);
        break;
      }
      case 'ProjectMemoryRetrieved': {
        const taskId = value(payload, 'taskId', 'TaskId');
        const taskTitle = value(payload, 'taskTitle', 'TaskTitle') || 'Task';
        const memories = value(payload, 'memories', 'Memories') || [];
        for (const memory of memories) {
          const memoryId = value(memory, 'memoryId', 'MemoryId');
          const memoryType = value(memory, 'type', 'Type') || 'Reference';
          if (memoryId) Neura.Graph.ensureMemoryNode(memoryId, memoryType, missionId, taskId, taskTitle);
        }
        Neura.pushEvent(`Retrieved ${memories.length} project ${memories.length === 1 ? 'memory' : 'memories'} as untrusted task context.`);
        break;
      }
      case 'TaskCreated':
      case 'TaskStarted': {
        const taskId = value(payload, 'taskId', 'TaskId', 'id', 'Id');
        const title = value(payload, 'title', 'Title') || 'Task';
        if (taskId) Neura.Graph.ensureTaskNode(taskId, title, missionId);
        if (type === 'TaskStarted') {
          updateNumber('metric-tasks', 1);
          Neura.pushEvent(`Task started: ${title}.`);
        } else {
          Neura.pushEvent(`Task created: ${title}.`);
        }
        break;
      }
      case 'TaskCompleted': {
        const taskId = value(payload, 'taskId', 'TaskId');
        const title = value(payload, 'title', 'Title') || 'Task';
        if (taskId) Neura.Graph.ensureTaskNode(taskId, title, missionId);
        if (agentId) Neura.Graph.setAgentState(agentId, 'agent-online');
        const output = value(payload, 'output', 'Output');
        Neura.pushEvent(output ? `Task completed by ${agentName}. Result is available in mission details.` : `Task completed by ${agentName}.`);
        Neura.announce(`Task completed by ${agentName}.`);
        break;
      }
      case 'MissionCompleted':
        Neura.pushEvent(`Mission completed: ${value(payload, 'title', 'Title') || missionId || ''}.`);
        break;
      case 'MissionFailed': {
        const error = value(payload, 'error', 'Error') || 'The mission failed without an automatic retry.';
        if (missionId) Neura.Graph.addErrorNode(missionId, error, missionId);
        Neura.pushEvent(`Mission failed: ${error}`, 'critical');
        Neura.announce(`Mission failed. ${error}`);
        break;
      }
      case 'MissionCancelled': {
        const reason = value(payload, 'error', 'Error') || 'Execution was cancelled.';
        Neura.pushEvent(`Mission cancelled: ${reason}`, 'warning');
        Neura.announce(`Mission cancelled. ${reason}`);
        break;
      }
      case 'TaskFailed':
      case 'ProviderFailure': {
        const error = value(payload, 'error', 'Error') || type;
        if (agentId) Neura.Graph.setAgentState(agentId, 'agent-failed');
        Neura.Graph.addErrorNode(value(payload, 'taskId', 'TaskId') || Date.now(), error, missionId);
        Neura.pushEvent(`${type}: ${error}`, 'critical');
        Neura.announce(`Execution error: ${error}`);
        break;
      }
      default:
        Neura.pushEvent(type);
    }
  }

  function openInspector(title, details, link) {
    const drawer = document.getElementById('inspector-drawer');
    const content = document.getElementById('inspector-content');
    if (!drawer || !content) return;
    content.replaceChildren();

    const heading = document.createElement('h2');
    heading.className = 'panel__title';
    heading.textContent = title;
    content.append(heading);

    const list = document.createElement('dl');
    list.className = 'inspector-list';
    for (const [key, raw] of Object.entries(details || {})) {
      if (raw === null || raw === undefined || typeof raw === 'object') continue;
      const term = document.createElement('dt');
      const description = document.createElement('dd');
      term.textContent = key;
      description.textContent = String(raw);
      list.append(term, description);
    }
    content.append(list);

    if (link) {
      const anchor = document.createElement('a');
      anchor.className = 'btn';
      anchor.href = link;
      anchor.textContent = 'Open record';
      content.append(anchor);
    }
    drawer.classList.add('open');
    drawer.setAttribute('aria-hidden', 'false');
  }

  window.addEventListener('neura:node-selected', (event) => {
    const data = event.detail || {};
    const kind = data.kind || 'Node';
    const link = kind === 'agent' ? `/Agents/Details/${encodeURIComponent(data.id)}`
      : kind === 'task' && data.missionId ? `/Missions/Details/${encodeURIComponent(data.missionId)}`
      : kind === 'handoff' && data.contextPackageId ? `/Context/Details/${encodeURIComponent(data.contextPackageId)}`
      : null;
    openInspector(`${kind.toUpperCase()} / ${data.label || data.id || ''}`, data, link);
  });
  window.addEventListener('neura:edge-selected', (event) => {
    const data = event.detail || {};
    openInspector('NEURAL CONNECTION', {
      Source: data.source,
      Destination: data.target,
      Type: data.kind,
      Payload: data.label || 'Agent/task signal',
      Handoff: data.handoffId || '—'
    });
  });

  const closeInspector = document.getElementById('inspector-close');
  if (closeInspector) closeInspector.addEventListener('click', () => {
    const drawer = document.getElementById('inspector-drawer');
    drawer?.classList.remove('open');
    drawer?.setAttribute('aria-hidden', 'true');
  });

  document.querySelectorAll('[data-filter]').forEach((button) => {
    button.addEventListener('click', () => {
      document.querySelectorAll('[data-filter]').forEach((item) => item.classList.remove('chip--active'));
      button.classList.add('chip--active');
      Neura.Graph.setFilter(button.dataset.filter || 'all');
    });
  });

  document.querySelectorAll('[data-graph-action]').forEach((button) => {
    button.addEventListener('click', () => {
      const action = button.dataset.graphAction;
      if (action === 'zoom-in') Neura.Graph.zoomIn();
      if (action === 'zoom-out') Neura.Graph.zoomOut();
      if (action === 'fit') Neura.Graph.fit();
    });
  });

  const runButton = document.getElementById('run-demo-btn');
  if (runButton) {
    runButton.addEventListener('click', async () => {
      runButton.disabled = true;
      runButton.textContent = 'Running simulation…';
      Neura.pushEvent('Simulation started: Claude Simulator → context exhaustion → validated handoff.');
      try {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const headers = { Accept: 'application/json' };
        if (token) headers.RequestVerificationToken = token;
        const response = await fetch('/Brain/RunSimulationDemo', { method: 'POST', headers });
        const raw = await response.text();
        let data;
        try { data = raw ? JSON.parse(raw) : null; }
        catch { data = { success: false, error: `Server returned ${response.status} without JSON.` }; }
        if (!response.ok || !data) {
          Neura.pushEvent(`Simulation failed: ${data?.error || `HTTP ${response.status}`}`, 'critical');
        } else {
          Neura.pushEvent(data.success
            ? `Simulation complete. Result: ${data.completedBy}. Inspect mission ${data.missionId} for output and replay.`
            : `Simulation failed: ${data.error || 'execution error'}`, data.success ? 'info' : 'critical');
        }
      } catch (error) {
        Neura.pushEvent(`Simulation request failed: ${error?.message || 'network error'}`, 'critical');
      } finally {
        runButton.disabled = false;
        runButton.textContent = '▶ Run Simulation Demo';
      }
    });
  }
});
