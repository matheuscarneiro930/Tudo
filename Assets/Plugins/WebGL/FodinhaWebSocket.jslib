// WebSocket do navegador para a versão web (WebGL) do Fodinha Online.
// O C# (BrowserSocketTransport) consulta o estado e lê as mensagens a cada quadro.
var FodinhaWebSocket = {
  $fws: { sockets: {}, next: 1 },

  // Retorna um id. Estados: 0 = conectando, 1 = aberto, 3 = fechado.
  FodinhaWS_Connect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var id = fws.next++;
    var s = { ws: null, queue: [], state: 0 };
    fws.sockets[id] = s;
    try {
      s.ws = new WebSocket(url);
    } catch (e) {
      s.state = 3;
      return id;
    }
    s.ws.onopen = function () { s.state = 1; };
    s.ws.onmessage = function (ev) { if (typeof ev.data === 'string') s.queue.push(ev.data); };
    s.ws.onclose = function () { s.state = 3; };
    s.ws.onerror = function () { s.state = 3; };
    return id;
  },

  FodinhaWS_State: function (id) {
    var s = fws.sockets[id];
    return s ? s.state : 3;
  },

  FodinhaWS_Send: function (id, textPtr) {
    var s = fws.sockets[id];
    if (s && s.state === 1) {
      try { s.ws.send(UTF8ToString(textPtr)); } catch (e) { s.state = 3; }
    }
  },

  // Próxima mensagem recebida, ou null. A Unity libera a memória da string devolvida.
  FodinhaWS_Poll: function (id) {
    var s = fws.sockets[id];
    if (!s || s.queue.length === 0) return null;
    var str = s.queue.shift();
    var size = lengthBytesUTF8(str) + 1;
    var buffer = _malloc(size);
    stringToUTF8(str, buffer, size);
    return buffer;
  },

  FodinhaWS_Close: function (id) {
    var s = fws.sockets[id];
    if (!s) return;
    try { if (s.ws) s.ws.close(); } catch (e) { }
    delete fws.sockets[id];
  }
};

autoAddDeps(FodinhaWebSocket, '$fws');
mergeInto(LibraryManager.library, FodinhaWebSocket);
