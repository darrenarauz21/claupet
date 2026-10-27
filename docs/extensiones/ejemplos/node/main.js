// Una extensión de ejemplo en Node.js, sin dependencias: habla JSON-RPC con Claupet por stdin/stdout
// (con el encabezado Content-Length, como LSP). Trae la habilidad «/eco», un «voz a texto» y un
// «texto a voz» de mentira y un asistente que responde por partes. También la usan las pruebas de
// Claupet (Claupet.Tests).
//
// Lo que se escribe en stdout es del protocolo: para mensajes de diagnóstico use console.error
// (Claupet lo guarda en el registro de la extensión).
'use strict';

let buffer = Buffer.alloc(0);
let nextId = 1;
const pending = new Map();

function send(message) {
  const body = Buffer.from(JSON.stringify({ jsonrpc: '2.0', ...message }), 'utf8');
  process.stdout.write(`Content-Length: ${body.length}\r\n\r\n`);
  process.stdout.write(body);
}

// Le pide algo a Claupet (la API del anfitrión) y espera la respuesta.
function call(method, params) {
  const id = nextId++;
  send({ id, method, params });
  return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
}

const progress = (id, value) => send({ method: '$/progress', params: { id, value } });

const handlers = {
  async initialize(params) {
    console.error(`iniciada en ${params.language}, permisos: ${params.permissions.join(', ')}`);
    return { name: 'eco' };
  },
  async 'skill/command'(params) {
    let petSaid = true;
    try { await call('pet/say', { text: params.text }); } catch (e) { petSaid = false; }
    return { reply: `eco: ${params.text}`, petSaid };
  },
  async 'speechToText/transcribe'(params) {
    const samples = Buffer.from(params.audio, 'base64').length / 2;
    return { text: `oí ${samples} muestras en ${params.language}` };
  },
  async 'textToSpeech/synthesize'(params, id) {
    // Dos trozos de silencio de 0,1 s: en una de verdad, el audio de cada frase.
    const chunk = Buffer.alloc(2205 * 2).toString('base64');
    progress(id, { audio: chunk, sampleRate: 22050 });
    progress(id, { audio: chunk, sampleRate: 22050 });
    return {};
  },
  async 'assistant/respond'(params, id) {
    const last = params.messages[params.messages.length - 1].text;
    for (const piece of ['Respondo ', 'a: ', last]) progress(id, { text: piece });
    return { text: `Respondo a: ${last}`, model: 'eco-js' };
  },
  async 'test/hostinfo'() { return await call('host/info', {}); },
  async 'test/forbidden'() { return await call('clipboard/read', {}); },
  async 'test/crash'() { process.exit(3); },
  async shutdown() { return null; },
};

async function dispatch(message) {
  if (message.method === undefined) {
    const p = pending.get(message.id);
    if (!p) return;
    pending.delete(message.id);
    if (message.error) p.reject(new Error(message.error.message)); else p.resolve(message.result);
    return;
  }
  if (message.method === 'exit') process.exit(0);
  if (message.id === undefined) return; // otras notificaciones (skill/event, settings/changed…)
  const handler = handlers[message.method];
  if (!handler) return send({ id: message.id, error: { code: -32601, message: `desconocido: ${message.method}` } });
  try {
    send({ id: message.id, result: (await handler(message.params, message.id)) ?? null });
  } catch (e) {
    send({ id: message.id, error: { code: -32603, message: String(e.message || e) } });
  }
}

process.stdin.on('data', (data) => {
  buffer = Buffer.concat([buffer, data]);
  for (;;) {
    const end = buffer.indexOf('\r\n\r\n');
    if (end < 0) return;
    const match = /Content-Length:\s*(\d+)/i.exec(buffer.subarray(0, end).toString('ascii'));
    if (!match) process.exit(2);
    const length = Number(match[1]);
    if (buffer.length < end + 4 + length) return;
    const body = buffer.subarray(end + 4, end + 4 + length).toString('utf8');
    buffer = buffer.subarray(end + 4 + length);
    dispatch(JSON.parse(body));
  }
});
process.stdin.on('end', () => process.exit(0));
