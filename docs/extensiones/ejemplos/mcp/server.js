// Un servidor MCP mínimo, sin dependencias: JSON-RPC por stdin/stdout, un mensaje por línea.
// Claupet lo suma a Claude Code, Codex y los agentes ACP (Gemini CLI, Copilot CLI…) al abrirlos.
// Guarda las notas en NOTAS_CARPETA (el ajuste «carpeta») o, si está vacío, en CLAUPET_DATA.
const fs = require("fs");
const path = require("path");
const readline = require("readline");

const carpeta = process.env.NOTAS_CARPETA || process.env.CLAUPET_DATA || __dirname;
const archivo = path.join(carpeta, "notas.txt");

const herramientas = [
  {
    name: "notas_guardar",
    description: "Guarda una nota para la persona.",
    inputSchema: { type: "object", properties: { texto: { type: "string" } }, required: ["texto"] },
  },
  {
    name: "notas_leer",
    description: "Lee las notas guardadas.",
    inputSchema: { type: "object", properties: {} },
    // Solo lee: el agente puede usarla sin pedir permiso.
    annotations: { readOnlyHint: true },
  },
];

function responder(id, result) {
  process.stdout.write(JSON.stringify({ jsonrpc: "2.0", id, result }) + "\n");
}

function texto(t) {
  return { content: [{ type: "text", text: t }] };
}

function llamar(nombre, args) {
  if (nombre === "notas_guardar") {
    fs.mkdirSync(carpeta, { recursive: true });
    fs.appendFileSync(archivo, `${new Date().toISOString()}  ${args.texto}\n`);
    return texto("Guardada.");
  }
  if (nombre === "notas_leer") {
    return texto(fs.existsSync(archivo) ? fs.readFileSync(archivo, "utf8") : "No hay notas.");
  }
  return { ...texto(`No conozco «${nombre}».`), isError: true };
}

readline.createInterface({ input: process.stdin }).on("line", (linea) => {
  let m;
  try { m = JSON.parse(linea); } catch { return; }
  if (m.id === undefined) return; // notificaciones (initialized…): no llevan respuesta
  switch (m.method) {
    case "initialize":
      return responder(m.id, {
        protocolVersion: m.params?.protocolVersion ?? "2025-06-18",
        capabilities: { tools: {} },
        serverInfo: { name: "notas", version: "1.0.0" },
      });
    case "tools/list":
      return responder(m.id, { tools: herramientas });
    case "tools/call":
      return responder(m.id, llamar(m.params?.name, m.params?.arguments ?? {}));
    case "ping":
      return responder(m.id, {});
    default:
      process.stdout.write(JSON.stringify({ jsonrpc: "2.0", id: m.id, error: { code: -32601, message: "Método desconocido" } }) + "\n");
  }
});
