"""Una extensión de ejemplo en Python, sin dependencias: habla JSON-RPC con Claupet por stdin/stdout
(con el encabezado Content-Length, como LSP). Trae la habilidad «/nota» (guarda una nota y la mascota
avisa), «/notas» (las lee, por partes) y escucha los avisos de Claupet para anotarlos en su registro.

Lo que se escribe en stdout es del protocolo: para mensajes de diagnóstico use stderr (Claupet lo guarda
en el registro de la extensión, que se ve en Ajustes › Extensiones › Registro).
"""
import json
import os
import sys
import threading

stdin = sys.stdin.buffer
stdout = sys.stdout.buffer
sys.stderr.reconfigure(encoding="utf-8")  # Claupet lee el registro en UTF-8 (en Windows, Python usaría cp1252)
write_lock = threading.Lock()
pending = {}
pending_lock = threading.Lock()
next_id = 0
state = {"settings": {}, "data": os.environ.get("CLAUPET_DATA", "."), "language": "es"}


def log(text):
    print(text, file=sys.stderr, flush=True)


def send(message):
    body = json.dumps({"jsonrpc": "2.0", **message}, ensure_ascii=False).encode("utf-8")
    with write_lock:
        stdout.write(b"Content-Length: %d\r\n\r\n" % len(body))
        stdout.write(body)
        stdout.flush()


def read_message():
    """Un mensaje, o None si Claupet cerró la entrada."""
    length = None
    while True:
        line = stdin.readline()
        if not line:
            return None
        line = line.strip()
        if not line:
            break
        name, _, value = line.decode("ascii").partition(":")
        if name.strip().lower() == "content-length":
            length = int(value.strip())
    if length is None:
        sys.exit(2)
    return json.loads(stdin.read(length).decode("utf-8"))


class HostError(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code


def call(method, params=None):
    """Le pide algo a Claupet (la API del anfitrión) y espera la respuesta."""
    global next_id
    done = threading.Event()
    with pending_lock:
        next_id += 1
        request_id = next_id
        pending[request_id] = {"done": done}
    send({"id": request_id, "method": method, "params": params or {}})
    done.wait()
    with pending_lock:
        reply = pending.pop(request_id)
    if "error" in reply:
        raise HostError(reply["error"].get("code", -32603), reply["error"].get("message", ""))
    return reply.get("result")


def progress(request_id, value):
    send({"method": "$/progress", "params": {"id": request_id, "value": value}})


def texts(es, en):
    return es if state["language"] == "es" else en


def notes_file():
    return os.path.join(state["data"], "notas.json")


def load_notes():
    try:
        with open(notes_file(), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return []


def save_notes(notes):
    os.makedirs(state["data"], exist_ok=True)
    temporary = notes_file() + ".tmp"
    with open(temporary, "w", encoding="utf-8") as f:
        json.dump(notes, f, ensure_ascii=False, indent=2)
    os.replace(temporary, notes_file())


def initialize(params, _):
    state["language"] = params["language"]
    state["data"] = params["extension"]["dataFolder"]
    state["settings"] = params["settings"]
    log("iniciada en %s; permisos: %s" % (params["language"], ", ".join(params["permissions"])))
    return {"name": "notas-python"}


def command(params, request_id):
    text = params["text"].strip()
    notes = load_notes()
    if params["command"] == "nota":
        if not text:
            return {"reply": texts("Escriba la nota después de /nota.", "Type the note after /nota.")}
        limit = int(float(state["settings"].get("maximo") or 50))
        notes = (notes + [text])[-limit:]
        save_notes(notes)
        if state["settings"].get("avisar") != "false":  # los ajustes llegan como texto: "true", "50"…
            try:
                call("pet/say", {"text": texts("¡Anotado!", "Noted!")})
            except HostError as e:
                log("la mascota no habló: %s" % e)  # sin el permiso «pet», o la persona lo quitó
        return {"reply": texts("Nota guardada (hay %d)." % len(notes), "Note saved (%d in total)." % len(notes))}
    if not notes:
        return {"reply": texts("No hay notas todavía.", "No notes yet.")}
    lines = ["%d. %s\n" % (i + 1, n) for i, n in enumerate(notes)]
    for line in lines:
        progress(request_id, {"text": line})  # se ve mientras llega; la respuesta final la reemplaza
    return {"reply": "".join(lines)}


def shutdown(_, __):
    return None


handlers = {"initialize": initialize, "skill/command": command, "shutdown": shutdown}


def on_notification(method, params):
    if method == "exit":
        os._exit(0)
    if method == "settings/changed":
        state["settings"] = params
    elif method == "skill/event" and params["event"] == "notice":
        data = params["data"]
        log("aviso (%s): %s" % (data.get("kind"), data.get("title")))
    # «$/cancelRequest» y las demás: este ejemplo responde enseguida, no hay nada que cancelar.


def handle(message):
    handler = handlers.get(message["method"])
    if handler is None:
        send({"id": message["id"], "error": {"code": -32601, "message": "desconocido: " + message["method"]}})
        return
    try:
        send({"id": message["id"], "result": handler(message.get("params") or {}, message["id"])})
    except Exception as e:  # el error vuelve a Claupet, que lo muestra
        send({"id": message["id"], "error": {"code": -32603, "message": str(e)}})


def main():
    while True:
        message = read_message()
        if message is None:
            return
        if "method" not in message:  # la respuesta a un call()
            with pending_lock:
                waiting = pending.get(message.get("id"))
            if waiting is not None:
                waiting.update(message)
                waiting["done"].set()
        elif "id" not in message:
            on_notification(message["method"], message.get("params") or {})
        else:
            # Cada petición en su hilo: mientras espera a Claupet (call), se siguen leyendo mensajes.
            threading.Thread(target=handle, args=(message,), daemon=True).start()


if __name__ == "__main__":
    main()
