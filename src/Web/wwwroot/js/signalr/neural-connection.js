window.Neura = window.Neura || {};

Neura.Connection = (function () {
  let connection = null;

  function start(onEvent) {
    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/neural')
      .withAutomaticReconnect()
      .build();

    connection.on('NeuralEvent', (evt) => onEvent(evt));

    connection.start().catch((err) => {
      console.error('NEURA hub connection failed:', err);
      Neura.pushEvent('SignalR connection failed — live updates unavailable.', 'critical');
    });
  }

  return { start };
})();
